using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Gameplay.Travel;
using RuleGame.Presentation.SceneComponents;
using RuleGame.UI.Train;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 火车站（快速传送）的预制体与摆放工具。
    /// </summary>
    /// <remarks>
    /// <para><b>车是布景</b>：直接拿现成的 <c>CityTrain.prefab</c> 拆掉会动的部分
    /// （刚体 / 路径电机 / 规则登记 / 实心碰撞），只留视觉，再加一个 Trigger 给攻击盒命中。
    /// 这样美术对齐与原来一模一样，不用重新对像素。</para>
    /// <para><b>不要做成 <c>CityMechanism</c></b>：布景不该有刚体、不该进快照。</para>
    /// </remarks>
    public static class TrainStationBuilder
    {
        public const string Folder = "Assets/_Project/Prefabs/BrickCity";
        public const string PrefabPath = Folder + "/TrainStation.prefab";
        public const string CatalogPath = "Assets/_Project/Data/Config/TrainStationCatalog.asset";

        private const string SourcePrefab = Folder + "/CityTrain.prefab";

        private const string MenuRoot = "Tools/规则游戏/地图组件/";

        [MenuItem(MenuRoot + "⑯ 火车站（快速传送 · 生成预制体）", false, 70)]
        public static void BuildPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null) throw new System.InvalidOperationException("缺少源预制体：" + SourcePrefab);

            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            root.name = "TrainStation";
            root.layer = 0;
            root.transform.position = Vector3.zero;

            // 车不再动：路径电机、刚体、规则登记、实心碰撞体全部去掉。
            foreach (CityPathMotor motor in root.GetComponents<CityPathMotor>()) Object.DestroyImmediate(motor);
            foreach (RuleAffectedBody affected in root.GetComponents<RuleAffectedBody>()) Object.DestroyImmediate(affected);
            foreach (Collider2D shape in root.GetComponents<Collider2D>()) Object.DestroyImmediate(shape);

            // ⚠️ 刚体要在最后清，而且要循环清：CityMechanism 带 [RequireComponent(typeof(Rigidbody2D))]，
            // 删掉基类之后 Unity 有可能把刚体补回来（实测第一次生成留下了一个 Rigidbody2D）。
            for (int guard = 0; guard < 8; guard++)
            {
                Rigidbody2D body = root.GetComponent<Rigidbody2D>();
                if (body == null) break;
                Object.DestroyImmediate(body, true);
            }

            if (root.GetComponent<Rigidbody2D>() != null)
                throw new System.InvalidOperationException("车站预制体里仍有 Rigidbody2D —— 布景不允许带刚体。");

            // 只剩一个 Trigger：给攻击盒 / 纸飞机命中用。放 Default 层，
            // 层 8 会被接地探测看见，层 10 会被 CombatSystem 当成受击箱。
            var trigger = root.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(4f, 1.2f);
            trigger.offset = new Vector2(0f, 0.3f);

            var ride = new GameObject("RidePoint");
            ride.transform.SetParent(root.transform, false);
            ride.transform.localPosition = new Vector3(0f, 0.55f, 0f);

            // 复活点放在落点上：传送到达即存档（不依赖攻击激活那条路径）。
            var spawn = ride.AddComponent<PlayerSpawnPoint>();
            spawn.SetId("station-00");

            var station = root.AddComponent<TrainStation>();
            station.Configure("station-00", "车站 00", 0, ride.transform, spawn);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[火车站] 已生成 " + PrefabPath + "（布景 + Trigger + 落点 + 复活点）");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        [MenuItem(MenuRoot + "⑯b 摆放火车站（在场景里加一台）", false, 71)]
        public static void Place()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[火车站] 先退出 Play 再摆放。");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                BuildPrefab();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 point = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.pivot
                : Vector3.zero;
            point.z = 0f;
            instance.transform.position = point;

            var station = instance.GetComponent<TrainStation>();
            string id = "station-" + Mathf.RoundToInt(point.x) + "-" + Mathf.RoundToInt(point.y);
            var spawn = instance.GetComponentInChildren<PlayerSpawnPoint>(true);
            Transform ride = instance.transform.Find("RidePoint");
            station.Configure(id, "车站 " + Mathf.RoundToInt(point.x), 0, ride, spawn);
            if (spawn != null) spawn.SetId(id);

            Undo.RegisterCreatedObjectUndo(instance, "Place Train Station");
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;

            Debug.Log("[火车站] 已摆放，编号 " + id + "。记得把编号同步进站点目录（⑯c）。");
        }

        [MenuItem(MenuRoot + "⑯d 一键接线（服务 + 面板 + 黑场）", false, 73)]
        public static void WireScene()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[火车站] 先退出 Play 再接线。");
                return;
            }

            var existing = Object.FindAnyObjectByType<TrainTravelService>();
            if (existing != null)
            {
                Debug.Log("[火车站] 场景里已经有 TrainTravelService（" + existing.name + "），只补缺失的引用。");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<TrainStationCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TrainStationCatalog>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                AssetDatabase.SaveAssets();
            }

            var root = existing != null ? existing.gameObject : new GameObject("TrainTravel");
            var service = existing != null ? existing : root.AddComponent<TrainTravelService>();

            Transform fadeRoot = root.transform.Find("ScreenFade");
            if (fadeRoot == null)
            {
                var fadeGo = new GameObject("ScreenFade");
                fadeGo.transform.SetParent(root.transform, false);
                fadeRoot = fadeGo.transform;
            }

            if (fadeRoot.GetComponent<SwingCheckpointFade>() == null) fadeRoot.gameObject.AddComponent<SwingCheckpointFade>();
            var screen = fadeRoot.GetComponent<SwingCheckpointFadeScreen>();
            if (screen == null) screen = fadeRoot.gameObject.AddComponent<SwingCheckpointFadeScreen>();

            Transform panelRoot = root.transform.Find("TrainTravelPanel");
            if (panelRoot == null)
            {
                var panelGo = new GameObject("TrainTravelPanel");
                panelGo.transform.SetParent(root.transform, false);
                panelRoot = panelGo.transform;
            }

            var document = panelRoot.GetComponent<UIDocument>();
            if (document == null) document = panelRoot.gameObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/RuleWheel/RuleWheelPanelSettings.asset");
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Train/TrainTravel.uxml");
            document.sortingOrder = 5;

            var panel = panelRoot.GetComponent<TrainTravelPanel>();
            if (panel == null) panel = panelRoot.gameObject.AddComponent<TrainTravelPanel>();

            var so = new SerializedObject(service);
            so.FindProperty("_catalog").objectReferenceValue = catalog;
            so.FindProperty("_fadeBehaviour").objectReferenceValue = screen;
            WorldStreamer streamer = Object.FindAnyObjectByType<WorldStreamer>();
            if (streamer != null) so.FindProperty("_streamer").objectReferenceValue = streamer;
            so.ApplyModifiedPropertiesWithoutUndo();

            var pso = new SerializedObject(panel);
            pso.FindProperty("_document").objectReferenceValue = document;
            pso.FindProperty("_service").objectReferenceValue = service;
            pso.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "Wire Train Travel");
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;

            Debug.Log("[火车站] 接线完成：TrainTravel（服务）/ ScreenFade（黑场）/ TrainTravelPanel（线路面板）。"
                + (streamer == null ? " 场景里没有 WorldStreamer，跨场景传送会在运行时自动找。" : ""));
        }

        [MenuItem(MenuRoot + "⑯c 用当前场景刷新车站目录", false, 72)]
        public static void RefreshCatalog()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[火车站] 先退出 Play 再刷新目录。");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<TrainStationCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TrainStationCatalog>();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var stations = Object.FindObjectsByType<TrainStation>()
                .OrderBy(s => s.Order).ThenBy(s => s.Id, System.StringComparer.Ordinal).ToArray();

            var entries = new List<TrainStationCatalog.Entry>(stations.Length);
            var seen = new HashSet<string>();
            foreach (TrainStation station in stations)
            {
                if (!seen.Add(station.Id))
                {
                    Debug.LogError("[火车站] 编号重复：" + station.Id + "（" + station.name + "），目录只保留第一个。", station);
                    continue;
                }

                entries.Add(new TrainStationCatalog.Entry
                {
                    Id = station.Id,
                    DisplayName = station.DisplayName,
                    ScenePath = station.gameObject.scene.path,
                    Order = station.Order,
                });
            }

            catalog.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            Debug.Log("[火车站] 站点目录已刷新：" + entries.Count + " 站 → " + CatalogPath);
        }
    }
}
