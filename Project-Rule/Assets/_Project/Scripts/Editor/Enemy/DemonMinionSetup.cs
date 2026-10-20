using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Development.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Presentation.Enemy;

namespace RuleGame.Editor.Enemy
{
    public static class DemonMinionSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/MinionLabs/Test_Minion_DiscoTop.unity";
        private const string Art = "Assets/_Project/Art/Enemies/Minions/";
        private const string Data = "Assets/_Project/Data/Enemies/FieldMinions/";
        public static string PrefabPath(EnemyKind kind) => FieldMinionSetup.PrefabFolder + "/" + kind + ".prefab";
        [MenuItem("RuleGame/Enemies/接入迪斯科球和陀螺（保留已有配置）")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("先退出 Play Mode。");
            foreach (var kind in new[] { EnemyKind.DiscoBall, EnemyKind.SpinningTop }) Build(kind);
            AssetDatabase.SaveAssets();
            CreateScene();
        }
        public static void ApplyBatch() { Apply(); EditorApplication.Exit(0); }
        private static void Build(EnemyKind kind)
        {
            bool disco = kind == EnemyKind.DiscoBall;
            string frame1 = Art + kind + "/" + kind + "_01.png", frame2 = Art + kind + "/" + kind + "_02.png";
            var frames = new[] { frame1, frame2 };
            var import = new EnemyArtFrame[2];
            for (int i = 0; i < 2; i++) import[i] = new EnemyArtFrame
            {
                path = frames[i], width = 1280, height = 720,
                pivotX = (disco ? 646f : 627f) / 1280f, pivotY = 1f - (disco ? 313f : 302f) / 720f,
                ppu = disco ? 214f : 205f
            };
            EnemyArtImporter.ImportFrames(import);
            string definitionPath = Data + kind + "_Definition.asset";
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(definitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<EnemyDefinition>();
                definition.Kind = kind; definition.DisplayName = disco ? "迪斯科球" : "陀螺怪";
                definition.BodySize = disco ? Vector2.one * 1.4f : new Vector2(1.25f, 1.2f);
                definition.Health = disco ? 30f : 25f; definition.Damage = disco ? 10f : 8f;
                definition.Energy = 0f; definition.EnergyRegen = 0f;
                definition.MoveSpeed = disco ? 0f : 3.5f; definition.Flying = disco;
                definition.ContactDamage = !disco; definition.MinionNavigationEnabled = false;
                definition.MinionLeashRange = 12f;
                definition.Timings = new[] { new EnemyActionTiming { Action = "Idle", Duration = .5f },
                    new EnemyActionTiming { Action = "Spin", Duration = .25f },
                    new EnemyActionTiming { Action = "Radiate", Duration = .25f },
                    new EnemyActionTiming { Action = "Hurt", Duration = .25f },
                    new EnemyActionTiming { Action = "Dead", Duration = .5f } };
                AssetDatabase.CreateAsset(definition, definitionPath);
            }
            string animationPath = Data + kind + "_Animations.asset";
            var set = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>(animationPath);
            if (set == null) { set = ScriptableObject.CreateInstance<EnemyAnimationSet>(); AssetDatabase.CreateAsset(set, animationPath); }
            Sprite first = AssetDatabase.LoadAssetAtPath<Sprite>(frame1), second = AssetDatabase.LoadAssetAtPath<Sprite>(frame2);
            set.Sequences = new[] { Sequence("Idle", new[] { first }), Sequence(disco ? "Radiate" : "Spin", new[] { first, second }),
                Sequence("Hurt", new[] { first }), Sequence("Dead", new[] { first }) };
            EditorUtility.SetDirty(set);
            const string spriteMaterialPath = Data + "DemonMinionSprite.mat";
            var spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(spriteMaterialPath);
            if (spriteMaterial == null)
            {
                spriteMaterial = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
                AssetDatabase.CreateAsset(spriteMaterial, spriteMaterialPath);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind)) != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath(kind));
                try { contents.transform.Find("Visual").GetComponent<SpriteRenderer>().sharedMaterial = spriteMaterial; PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath(kind)); }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                return;
            }
            var root = new GameObject(kind.ToString()); root.SetActive(false); root.layer = 9;
            try
            {
                var actor = root.AddComponent<EnemyActor>(); actor.Definition = definition;
                actor.UseSpawnRelativeArena = true; actor.ArenaSize = new Vector2(24f, 20f);
                root.GetComponent<BoxCollider2D>().size = definition.BodySize;
                root.GetComponent<Rigidbody2D>().mass = disco ? 2f : 1.5f;
                root.AddComponent<RuleAffectedBody>();
                var hurt = new GameObject("Hurtbox"); hurt.transform.SetParent(root.transform, false);
                var area = hurt.AddComponent<BoxCollider2D>(); area.size = definition.BodySize; area.isTrigger = true;
                hurt.AddComponent<Hurtbox>();
                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
                var body = visual.AddComponent<SpriteRenderer>(); body.sprite = first; body.sortingOrder = 5;
                body.sharedMaterial = spriteMaterial;
                var animator = visual.AddComponent<EnemySpriteAnimator>(); animator.Actor = actor; animator.AnimationSet = set;
                if (disco)
                {
                    const string materialPath = Data + "DiscoRadiation.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null) { material = new Material(Shader.Find("RuleGame/DiscoRadiation")); AssetDatabase.CreateAsset(material, materialPath); }
                    var radiation = new GameObject("Radiation"); radiation.transform.SetParent(root.transform, false);
                    radiation.AddComponent<MeshFilter>();
                    var renderer = radiation.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.sortingOrder = 2;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    radiation.AddComponent<DiscoRadiationView>().Actor = actor;
                }
                root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(kind));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static EnemySpriteSequence Sequence(string action, Sprite[] frames) => new EnemySpriteSequence
            { Action = action, Frames = frames, NativeFrameRate = true, FrameRate = 8f, FacesRight = true };
        private static void CreateScene()
        {
            if (File.Exists(ScenePath)) return;
            if (!AssetDatabase.CopyAsset("Assets/_Project/Scenes/EnemyLabs/Test_Enemy_BatMinion.unity", ScenePath))
                throw new InvalidOperationException("缺少现有可玩测试场。");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); SceneManager.SetActiveScene(scene);
            try
            {
                var player = Find<PlayerController>(scene); var combat = Find<CombatSystem>(scene);
                var controls = Find<EnemyLabControls>(scene);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var actor in root.GetComponentsInChildren<EnemyActor>(true)) UnityEngine.Object.DestroyImmediate(actor.gameObject);
                player.transform.position = new Vector3(-10f, .65f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
                Resize(scene, "Floor", new Vector3(0f, -1f), new Vector3(34f, 2f, 1f));
                Resize(scene, "Ceiling", new Vector3(0f, 12f), new Vector3(34f, 1f, 1f));
                Resize(scene, "LeftWall", new Vector3(-17f, 5.5f), new Vector3(1f, 14f, 1f));
                Resize(scene, "RightWall", new Vector3(17f, 5.5f), new Vector3(1f, 14f, 1f));
                var disco = Place(EnemyKind.DiscoBall, new Vector2(-5f, 5.5f), scene);
                var top = Place(EnemyKind.SpinningTop, new Vector2(7f, .65f), scene);
                top.ArenaX = new Vector2(1f, 16f); top.UseSpawnRelativeArena = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(top);
                controls.Enemy = disco;
                var divider = Geometry("FixedCover", new Vector2(0f, 1.8f), new Vector2(.7f, 3.6f));
                var shield = Geometry("MovingRadiationCover", new Vector2(-9f, 2.8f), new Vector2(3.5f, .5f));
                shield.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                var moving = shield.AddComponent<MovingPlatform>();
                var serialized = new SerializedObject(moving);
                serialized.FindProperty("_start").vector2Value = new Vector2(-9f, 2.8f);
                serialized.FindProperty("_end").vector2Value = new Vector2(-2f, 2.8f);
                serialized.FindProperty("_speed").floatValue = 1.5f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var crate = Geometry("BounceCrate", new Vector2(11f, .8f), new Vector2(1f, 1.5f));
                crate.layer = 9; crate.AddComponent<Rigidbody2D>().mass = 2f; crate.AddComponent<RuleAffectedBody>();
                Label("迪斯科球：跟随移动平台的阴影躲避；停火时通过", new Vector3(-6f, 9f), controls.Font);
                Label("陀螺：碰撞双方反弹；铁箱也可以撞开", new Vector3(8f, 5.5f), controls.Font);
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
        }
        private static EnemyActor Place(EnemyKind kind, Vector2 position, Scene scene)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind)), scene);
            root.transform.position = position; PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            return root.GetComponent<EnemyActor>();
        }
        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects()) { var found = root.GetComponentInChildren<T>(true); if (found != null) return found; }
            throw new InvalidOperationException("测试场缺少 " + typeof(T).Name);
        }
        private static void Resize(Scene scene, string name, Vector3 position, Vector3 scale)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == name) { child.position = position; child.localScale = scale; }
        }
        private static GameObject Geometry(string name, Vector2 position, Vector2 size)
        {
            var root = new GameObject(name) { layer = 8 }; root.transform.position = position;
            root.AddComponent<BoxCollider2D>().size = size;
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            renderer.drawMode = SpriteDrawMode.Tiled; renderer.size = size; renderer.color = new Color(.3f, .28f, .4f); renderer.sortingOrder = 4;
            return root;
        }
        private static void Label(string text, Vector3 position, Font font)
        {
            var label = new GameObject("Instructions").AddComponent<TextMesh>(); label.transform.position = position;
            label.text = text; label.font = font; label.fontSize = 36; label.characterSize = .07f; label.anchor = TextAnchor.MiddleCenter;
            label.color = new Color(.18f, .16f, .22f); label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}
