using System;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using RuleGame.UI.RuleWheel;
using RuleGame.Presentation.RuleWheel;
using RuleGame.Development.RuleWheel;
using RuleGame.Development.P3;
using RuleGame.Core.Rules.Debugging;
using System.Linq;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Development.BrickCity;

namespace RuleGame.Editor.Level
{
    /// <summary>只生成积木城专用 prefab 和独立实验场，不修改正式地图、运行骨架或敌人资产。</summary>
    public static class BrickCityBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test_BrickCityComponents.unity";
        public const string Folder = "Assets/_Project/Prefabs/BrickCity";
        private static Sprite _square, _circle, _stone, _platform;
        private static Material _spriteMaterial;
        private static PhysicsMaterial2D _bounce;

        [MenuItem("Tools/规则游戏/积木城/生成组件与白盒")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Folder);
            _square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            _circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/WhiteBoxCircle.png");
            _spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            if (_square == null || _circle == null) throw new InvalidOperationException("缺少项目已有的白盒 Sprite。");
            const string materialPath = Folder + "/MarbleBounce.physicsMaterial2D";
            _bounce = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(materialPath);
            if (_bounce == null)
            {
                var template = Resources.Load<PhysicsMaterial2D>("RuleDefaultMaterial2D");
                if (template == null) throw new InvalidOperationException("缺少 RuleDefaultMaterial2D 材质模板。");
                _bounce = UnityEngine.Object.Instantiate(template); _bounce.name = "MarbleBounce";
                AssetDatabase.CreateAsset(_bounce, materialPath);
            }
            _bounce.friction = 0f; _bounce.bounciness = 1f; EditorUtility.SetDirty(_bounce);
            _stone = LoadSprite("Assets/_Project/Art/Sprites/Tiles/Stone/Ground_Stone_Slab_04.png");
            _platform = LoadSprite("Assets/_Project/Art/Sprites/Tiles/Stone/Platform_Stone_Slab_03.png");
            BuildPrefabs(); BuildScene(); AssetDatabase.SaveAssetIfDirty(_bounce);
            Debug.Log("[BrickCity] 已生成 15 种组件 prefab 与 " + ScenePath);
        }

        [MenuItem("Tools/规则游戏/积木城/打开组件白盒")]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        private static GameObject Shape(string name, Vector2 size, Color color, bool circle = false, bool trigger = false, int layer = 8)
        {
            var go = new GameObject(name); go.layer = layer;
            Collider2D shape;
            if (circle) { var col = go.AddComponent<CircleCollider2D>(); col.radius = size.x * .5f; shape = col; }
            else { var col = go.AddComponent<BoxCollider2D>(); col.size = size; shape = col; }
            shape.isTrigger = trigger;
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false);
            var sprite = visual.AddComponent<SpriteRenderer>(); sprite.sprite = circle ? _circle : _square;
            sprite.sharedMaterial = _spriteMaterial; sprite.color = color; sprite.sortingOrder = 2;
            visual.transform.localScale = new Vector3(size.x / sprite.sprite.bounds.size.x, size.y / sprite.sprite.bounds.size.y, 1f);
            return go;
        }
        private static void Save(GameObject go)
        {
            PrefabUtility.SaveAsPrefabAsset(go, Folder + "/" + go.name + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
        private static void BuildPrefabs()
        {
            var lift = Shape("CityLift", new Vector2(3f, .5f), new Color(.35f, .6f, .75f));
            lift.AddComponent<CityPathMotor>(); Save(lift);
            var gate = Shape("CityGate", new Vector2(.8f, 3f), new Color(.4f, .5f, .55f));
            var gm = gate.AddComponent<CityPathMotor>(); gm.EndOffset = Vector2.up * 4f; gm.CarryRiders = false; gm.BlockActors = true; Save(gate);
            var lever = Shape("CityLever", new Vector2(.65f, 1f), new Color(.85f, .6f, .2f), trigger: true, layer: 0);
            lever.AddComponent<CitySwitch>().Prompt = "切换升降方向"; Save(lever);
            var button = Shape("CityButton", new Vector2(.9f, .35f), new Color(.85f, .3f, .2f), trigger: true, layer: 0);
            var bs = button.AddComponent<CitySwitch>(); bs.Toggle = false; bs.Prompt = "按下按钮"; Save(button);
            var hammer = Shape("CityHammer", new Vector2(1.6f, 1f), new Color(.55f, .35f, .3f), trigger: true, layer: 0);
            hammer.AddComponent<CityHammer>(); Save(hammer);
            var marble = Shape("CityMarble", Vector2.one * .7f, new Color(.75f, .3f, .2f), true, layer: 9);
            marble.AddComponent<CityMarble>(); marble.GetComponent<Rigidbody2D>().sharedMaterial = _bounce;
            marble.GetComponent<Rigidbody2D>().gravityScale = .65f; marble.GetComponent<Rigidbody2D>().collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Save(marble);
            var bubble = Shape("CityBubble", new Vector2(2f, .4f), new Color(.45f, .75f, .85f, .6f));
            bubble.AddComponent<CityBubble>(); Save(bubble);
            var refill = Shape("CityRefill", new Vector2(1f, 1.6f), new Color(.35f, .55f, .85f), trigger: true, layer: 0);
            refill.AddComponent<CityRefill>().Prompt = "补充能量"; Save(refill);
            // 落点 = 存档点（2026-10-18 合并）：走近不再自动记录，要攻击一下才会存档。
            var spawn = Shape("PlayerSpawnPoint", new Vector2(1.4f, 1.8f), new Color(.4f, .7f, .45f), trigger: true, layer: 0);
            spawn.AddComponent<PlayerSpawnPoint>(); Save(spawn);
            var chest = Shape("CityChest", new Vector2(1.3f, .85f), new Color(.8f, .6f, .25f), trigger: true, layer: 0);
            chest.AddComponent<CityRewardSource>().Prompt = "打开宝箱"; Save(chest);
            var reward = Shape("CityReward", Vector2.one * .5f, new Color(.8f, .35f, .3f), true, true, 0);
            var rs = reward.AddComponent<CityRewardSource>(); rs.PickupVisual = true; rs.Prompt = "收集龙珠"; Save(reward);
            var train = Shape("CityTrain", new Vector2(4f, .6f), new Color(.55f, .45f, .7f));
            var tm = train.AddComponent<CityPathMotor>(); tm.EndOffset = Vector2.right * 14f; tm.AutoReturn = true;
            tm.TargetEnd = true; tm.Speed = 3f; tm.StationWait = 2f; Save(train);
            var bomb = Shape("CityBomb", Vector2.one, new Color(.4f, .4f, .4f), true, true, 0);
            bomb.AddComponent<CityBomb>(); Save(bomb);
            // 巡逻炸弹：轨道逻辑在 TrackMotor，炸弹自己只管「碰到就炸」。
            var patrol = Shape("CityTrackBomb", Vector2.one, new Color(.55f, .3f, .45f), true, true, 0);
            patrol.AddComponent<TrackMotor>().SetPoints(new[] { new Vector2(0f, 2.4f) });
            patrol.AddComponent<CityTrackBomb>(); Save(patrol);
            var thorns = Shape("CityThorns", new Vector2(3f, .5f), new Color(.7f, .2f, .3f), trigger: true, layer: 0);
            thorns.AddComponent<CityDamageZone>(); thorns.AddComponent<CityMechanism>(); Save(thorns);
        }
        private static T Place<T>(string prefab, Vector2 point) where T : Component
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + prefab + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset); go.transform.position = point;
            return go.GetComponent<T>();
        }
        private static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) throw new InvalidOperationException("缺少地图纹理：" + path);
            return sprite;
        }
        // 保留原来的碰撞尺寸；视觉按实际瓦片宽度平铺，不把一张石头图拉成长条。
        private static GameObject Block(string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name); go.layer = 8; go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
            bool ground = name == "Ground";
            var visual = new GameObject("石头地形"); visual.transform.SetParent(go.transform, false);
            var sprite = visual.AddComponent<SpriteRenderer>();
            sprite.sprite = ground ? _stone : _platform; sprite.sharedMaterial = _spriteMaterial;
            sprite.drawMode = SpriteDrawMode.Tiled;
            sprite.tileMode = SpriteTileMode.Continuous;
            sprite.size = ground ? new Vector2(size.x, 4f) : size;
            // 地图原图使用左上角锚点。按实际 pivot 对齐碰撞体，不能假设图片中心即物体中心。
            Vector2 pivot = sprite.sprite.pivot / sprite.sprite.rect.size;
            Vector2 pivotOffset = (pivot - Vector2.one * .5f) * sprite.size;
            visual.transform.localPosition = (Vector3)(pivotOffset + (ground ? Vector2.down * 1.5f : Vector2.zero));
            return go;
        }
        private static void SetReference(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(property).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void BuildScene()
        {
            Scene previous = SceneManager.GetActiveScene();
            bool additive = !string.IsNullOrEmpty(previous.path) && previous.path != ScenePath;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                var runtimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/LevelRuntime.prefab");
                var runtime = (GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab);
                foreach (var overlay in runtime.GetComponentsInChildren<P3DebugOverlay>(true)) overlay.enabled = false;
                foreach (var debugInput in runtime.GetComponentsInChildren<RuleSystemTestInput>(true)) debugInput.enabled = false;
                var player = runtime.GetComponentInChildren<PlayerController>();
                if (player == null) throw new InvalidOperationException("LevelRuntime 缺少主角。");
                player.transform.position = new Vector3(-3f, .85f);
                var combat = runtime.GetComponentInChildren<CombatSystem>();
                var controls = new GameObject("BrickCityLabControls").AddComponent<BrickCityLabControls>();
                var interactor = player.gameObject.AddComponent<CityInteractor>();
                interactor.SetReadInteractKey(false); // 白盒 BrickCityLabControls 自己读 E，别再抢一遍
                controls.Interactor = interactor; controls.PlayerStatus = player.GetComponent<EntityStatus>();
                controls.Stations = new[] { new Vector2(-3f, .85f), new Vector2(14f, .85f), new Vector2(26f, .85f),
                    new Vector2(41f, .85f), new Vector2(51f, .85f), new Vector2(67f, .85f), new Vector2(77f, .85f), new Vector2(90f, 1.3f) };
                var wheel = runtime.GetComponentInChildren<RuleWheelPresenter>(true);
                if (wheel == null) throw new InvalidOperationException("LevelRuntime 缺少规则轮盘。");
                controls.Wheel = wheel;
                var wheelData = new SerializedObject(wheel);
                wheelData.FindProperty("_selectionScale").floatValue = RuleWheelPresenter.DefaultSelectionScale;
                wheelData.ApplyModifiedPropertiesWithoutUndo();
                var orb = wheel.gameObject.AddComponent<RuleSelectionOrbView>();
                SetReference(orb, "_art", AssetDatabase.LoadAssetAtPath<RuleOrbArt>("Assets/_Project/Data/RuleWheel/RuleOrbArt.asset"));
                var hud = new GameObject("积木城演示提示").AddComponent<UIDocument>();
                hud.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/RuleWheel/RuleWheelPanelSettings.asset");
                hud.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/BrickCity/BrickCityLab.uxml");
                hud.sortingOrder = 1; controls.Hud = hud;
                var vitals = hud.gameObject.AddComponent<RuleWheelLabHUD>();
                SetReference(vitals, "_status", controls.PlayerStatus); SetReference(vitals, "_wheel", wheel);
                var camera = runtime.GetComponentInChildren<Camera>();
                if (camera != null)
                {
                    camera.backgroundColor = new Color(.105f, .12f, .14f);
                    camera.transform.position = player.transform.position + new Vector3(0f, 1.2f, -10f);
                }
                Block("Ground", new Vector2(51f, -.5f), new Vector2(116f, 1f));

                var lift = Place<CityPathMotor>("CityLift", new Vector2(1f, .3f));
                var lower = Place<CitySwitch>("CityLever", new Vector2(-.5f, .65f)); lower.FollowMotor = lift;
                Block("LiftUpperLanding", new Vector2(4.5f, 5f), new Vector2(3f, .5f));
                var upper = Place<CitySwitch>("CityLever", new Vector2(3.7f, 5.7f)); upper.FollowMotor = lift;

                var gate = Place<CityPathMotor>("CityGate", new Vector2(17f, 1.5f));
                var gateSwitch = Place<CitySwitch>("CityLever", new Vector2(14f, .65f)); gateSwitch.FollowMotor = gate;
                var bomb = Place<CityBomb>("CityBomb", new Vector2(21f, .6f)); bomb.Combat = combat;
                var button = Place<CitySwitch>("CityButton", new Vector2(19f, .4f));
                UnityEventTools.AddPersistentListener(button.OnChanged, bomb.SetOn);

                // 在荆棘前面上下巡逻：过这一段要卡时机，碰到就炸。
                var patrol = Place<CityTrackBomb>("CityTrackBomb", new Vector2(31f, .8f)); patrol.Combat = combat;

                var hammer = Place<CityHammer>("CityHammer", new Vector2(28f, 4.6f));
                hammer.GetComponent<CityDamageZone>().Combat = combat;
                var thorns = Place<CityDamageZone>("CityThorns", new Vector2(33f, .25f)); thorns.Combat = combat;

                Block("MarbleLeftWall", new Vector2(38f, 3.5f), new Vector2(.5f, 7f));
                Block("MarbleRightWall", new Vector2(48f, 3.5f), new Vector2(.5f, 7f));
                Block("MarbleCeiling", new Vector2(43f, 7f), new Vector2(10f, .5f));
                var marble = Place<CityMarble>("CityMarble", new Vector2(42f, 2f));
                marble.GetComponent<CityDamageZone>().Combat = combat;

                Place<CityBubble>("CityBubble", new Vector2(53f, 1.5f));
                Place<CityBubble>("CityBubble", new Vector2(56.5f, 3f));
                Block("BubbleLanding", new Vector2(60f, 4.5f), new Vector2(3f, .5f));

                Place<CityRefill>("CityRefill", new Vector2(67f, .8f));
                var point = Place<PlayerSpawnPoint>("PlayerSpawnPoint", new Vector2(71f, .9f)); point.SetId("brick-city-lab-rest");
                var chest = Place<CityRewardSource>("CityChest", new Vector2(77f, .45f));
                chest.Kind = CityRewardKind.Health;      // 战斗力那条奖励已删，演示箱改用生命
                chest.Receiver = controls; chest.SourceId = "brick-city-lab-chest"; chest.Amount = 3;

                // 三色龙珠只剩两色：战斗力退役（枚举 0 号空着，不再有成员）。
                CityRewardKind[] tokenKinds = { CityRewardKind.Health, CityRewardKind.Energy };
                Color[] tokenColors = { Color.green, Color.blue };
                for (int i = 0; i < tokenKinds.Length; i++)
                {
                    var token = Place<CityRewardSource>("CityReward", new Vector2(80f + i * 2f, .8f));
                    token.Receiver = controls; token.SourceId = "brick-city-lab-token-" + i; token.Kind = tokenKinds[i];
                    token.GetComponentInChildren<SpriteRenderer>().color = tokenColors[i];
                }
                Place<CityPathMotor>("CityTrain", new Vector2(90f, .4f));
                Physics2D.SyncTransforms();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("白盒场景保存失败。");
            }
            finally
            {
                if (additive) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }
}
