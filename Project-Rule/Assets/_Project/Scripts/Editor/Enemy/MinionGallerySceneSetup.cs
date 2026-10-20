using System;
using System.IO;
using System.Linq;
using RuleGame.Development.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.DemonCastle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Enemy
{
    public static class MinionGallerySceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/MinionLabs/Test_Minion_Gallery.unity";
        [MenuItem("RuleGame/Enemies/创建小怪集中测试场景（不覆盖）")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (File.Exists(ScenePath)) { Debug.Log("集中测试场景已存在：" + ScenePath); return; }
            if (!AssetDatabase.CopyAsset("Assets/_Project/Scenes/EnemyLabs/Test_Enemy_BatMinion.unity", ScenePath))
                throw new InvalidOperationException("缺少已有敌人测试场景。");
            Scene previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var player = Find<PlayerController>(scene);
                var combat = Find<CombatSystem>(scene);
                var oldControls = Find<EnemyLabControls>(scene);
                Font font = oldControls.Font;
                UnityEngine.Object.DestroyImmediate(oldControls);
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var actor in root.GetComponentsInChildren<EnemyActor>(true)) UnityEngine.Object.DestroyImmediate(actor.gameObject);
                player.transform.position = new Vector3(-5f, .65f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
                Resize(scene, "Floor", new Vector3(70f, -1f), new Vector3(164f, 2f, 1f));
                Resize(scene, "Ceiling", new Vector3(70f, 11f), new Vector3(164f, 1f, 1f));
                Resize(scene, "LeftWall", new Vector3(-12f, 5f), new Vector3(1f, 12f, 1f));
                Resize(scene, "RightWall", new Vector3(152f, 5f), new Vector3(1f, 12f, 1f));
                var square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
                var ui = new GameObject("MinionGalleryControls");
                var document = ui.AddComponent<UIDocument>();
                document.panelSettings = Find<UIDocument>(scene).panelSettings;
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/MinionGallery/MinionGallery.uxml");
                document.sortingOrder = 20;
                var controls = ui.AddComponent<MinionGalleryControls>(); controls.Player = player;
                controls.Stations = new Vector2[MinionArtSetup.Kinds.Length + 1]; controls.Enemies = new EnemyActor[MinionArtSetup.Kinds.Length];
                for (int i = 0; i < controls.Stations.Length; i++)
                {
                    float center = i * 20f;
                    controls.Stations[i] = new Vector2(center - 5f, .65f);
                    string name = i < controls.Enemies.Length ? MinionArtSetup.Kinds[i].ToString() : "FireworkMechanism";
                    var zone = new GameObject("Zone_" + (i + 1).ToString("00") + "_" + name);
                    var strip = new GameObject("AreaMarker").AddComponent<SpriteRenderer>();
                    strip.transform.SetParent(zone.transform); strip.transform.position = new Vector3(center, -.04f);
                    strip.transform.localScale = new Vector3(18f, .08f, 1f);
                    strip.sprite = square; strip.sharedMaterial = material;
                    strip.color = Color.HSVToRGB(i / 9f, .4f, .7f); strip.sortingOrder = 1;
                    var label = new GameObject("AreaName").AddComponent<TextMesh>();
                    label.transform.SetParent(zone.transform); label.transform.position = new Vector3(center, 6.7f);
                    label.text = (i + 1).ToString("00") + "  " + name;
                    label.font = font; label.fontSize = 48; label.characterSize = .09f;
                    label.anchor = TextAnchor.MiddleCenter; label.color = new Color(.2f, .2f, .24f);
                    label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    if (i < controls.Enemies.Length)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MinionArtSetup.PrefabPath(MinionArtSetup.Kinds[i]));
                        var actor = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<EnemyActor>();
                        controls.Enemies[i] = actor;
                        actor.transform.SetParent(zone.transform, true);
                        float y = actor.Definition.Flying ? 3f : actor.Definition.BodySize.y * .5f + .05f;
                        actor.transform.position = new Vector3(center + 2f, y);
                        actor.Target = player.transform; actor.Combat = combat; actor.Summoner = null;
                        actor.UseSpawnRelativeArena = true; actor.ArenaSize = new Vector2(16f, 10f); actor.ArenaOffset = new Vector2(-2f, 5f - y);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
                    }
                    else
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/DemonCastle/DemonFireworkSwitch.prefab");
                        var firework = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        firework.transform.SetParent(zone.transform, true); firework.transform.position = new Vector3(center, .25f);
                        controls.Firework = firework.GetComponentInChildren<DemonFirework>(); controls.Firework.Combat = combat;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(firework.transform);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(controls.Firework);
                    }
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[MinionGallery] 七种已有小怪与既有烟花机关的集中场景已创建。");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            throw new InvalidOperationException("测试场景缺少 " + typeof(T).Name);
        }
        private static void Resize(Scene scene, string name, Vector3 position, Vector3 scale)
        {
            var root = scene.GetRootGameObjects().First(item => item.name == name);
            root.transform.position = position; root.transform.localScale = scale;
        }
    }
}
