using System;
using System.Linq;
using RuleGame.Gameplay.Scene;
using RuleGame.Presentation.Audio;
using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 背景层的一次性接线：生成三份配置、把 Backdrop 挂进 LevelRuntime、在积木王国摆两个区域。
    /// </summary>
    /// <remarks>
    /// <para><b>菜单 ①</b> 可以重复跑：已有配置只补空字段，不覆盖手调过的颜色和时长。</para>
    /// <para><b>菜单 ②</b> 每次都会重建两个区域（按名字删除再加），矩形按场景实际几何自动算，
    /// 所以地形改过之后重跑一次就对齐了。区域上的 <c>Music</c> 按名字继承，重跑不会丢。</para>
    /// </remarks>
    public static class BackdropSetupTool
    {
        public const string ArtRoot = "Assets/_Project/Art/Sprites/Backdrops";
        public const string GradientFolder = ArtRoot + "/Gradient";
        public const string PaintedFolder = ArtRoot + "/Painted";
        public const string DataFolder = "Assets/_Project/Data/Backdrops";
        public const string RuntimePrefabPath = "Assets/_Project/Prefabs/LevelRuntime.prefab";
        public const string GameRootPrefabPath = "Assets/_Project/Prefabs/GameRoot.prefab";
        public const string BuildingScenePath = "Assets/_Project/Scenes/BuildingBlocks/Level_Building_01.unity";

        public const string StartProfile = "Start_Backdrop";
        public const string BrickProfile = "Brick_Backdrop";
        public const string BrickBossProfile = "BrickBoss_Backdrop";

        /// <summary>一份背景配置的生成参数。</summary>
        private sealed class ProfilePlan
        {
            public ProfilePlan(string name, string sprite, Color clearColor)
            {
                Name = name;
                Sprite = sprite;
                Tint = Color.white;
                ClearColor = clearColor;
            }

            public string Name;
            public string Sprite;
            public Color Tint;
            public Color ClearColor;
        }

        /// <summary>重制版 12 张：外景 + 内景（Boss / 协会 / 宫殿）。</summary>
        private static readonly ProfilePlan[] Plans =
        {
            new ProfilePlan("Start_Backdrop", "Start_Backdrop", new Color(0.545f, 0.737f, 0.682f)),
            new ProfilePlan("StartGrove_Backdrop", "StartGrove_Backdrop", new Color(0.765f, 0.906f, 0.761f)),
            new ProfilePlan("GuildHall_Backdrop", "GuildHall_Backdrop", new Color(0.608f, 0.643f, 0.596f)),
            new ProfilePlan("Station_Backdrop", "Station_Backdrop", new Color(0.576f, 0.773f, 0.851f)),
            new ProfilePlan("Brick_Backdrop", "Brick_Backdrop", new Color(0.894f, 0.827f, 0.859f)),
            new ProfilePlan("BrickBoss_Backdrop", "BrickBoss_Backdrop", new Color(0.882f, 0.788f, 0.808f)),
            new ProfilePlan("Clockwork_Backdrop", "Clockwork_Backdrop", new Color(0.914f, 0.788f, 0.537f)),
            new ProfilePlan("ClockworkBoss_Backdrop", "ClockworkBoss_Backdrop", new Color(0.933f, 0.831f, 0.584f)),
            new ProfilePlan("Plush_Backdrop", "Plush_Backdrop", new Color(0.867f, 0.824f, 0.910f)),
            new ProfilePlan("PlushBoss_Backdrop", "PlushBoss_Backdrop", new Color(0.827f, 0.733f, 0.867f)),
            new ProfilePlan("Demon_Backdrop", "Demon_Backdrop", new Color(0.584f, 0.624f, 0.890f)),
            new ProfilePlan("DemonHall_Backdrop", "DemonHall_Backdrop", new Color(0.443f, 0.435f, 0.851f)),
        };

        public const string BrickZoneName = "BackdropZone_Brick";
        public const string BrickBossZoneName = "BackdropZone_BrickBoss";

        private const float BossZoneWidth = 40f;

        [MenuItem("Tools/规则游戏/背景/① 接通背景层（配置 + 预制体）", false, 1)]
        public static void Build()
        {
            EnsureFolder(DataFolder);
            EnsureFolder(ArtRoot);
            EnsureFolder(GradientFolder);
            EnsureFolder(PaintedFolder);

            var missing = new System.Collections.Generic.List<string>();
            BackdropProfile startProfile = null;

            foreach (ProfilePlan plan in Plans)
            {
                Sprite sprite = LoadSprite(PaintedFolder + "/" + plan.Sprite + ".png");
                if (sprite == null)
                {
                    missing.Add(plan.Sprite + ".png");
                    continue;
                }

                BackdropProfile profile = EnsureProfile(plan.Name, sprite, plan.Tint, plan.ClearColor);
                if (plan.Name == StartProfile) startProfile = profile;
            }

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "这些背景贴图还没导入：" + string.Join("、", missing) +
                    "。先跑 Tools/Art/prepare_backdrops.py，再回 Unity 等导入完成。");
            }

            if (startProfile == null) throw new InvalidOperationException("Start_Backdrop 配置没有生成。");

            WireRuntime(startProfile);
            AssetDatabase.SaveAssets();

            Debug.Log("[背景] ① 完成：" + Plans.Length + " 份配置 + LevelRuntime/Main Camera/Backdrop（默认 " + StartProfile + "）");
        }

        [MenuItem("Tools/规则游戏/背景/② 在积木王国摆背景区域", false, 2)]
        public static void PlaceZones()
        {
            Scene active = SceneManager.GetActiveScene();
            Scene building = SceneManager.GetSceneByPath(BuildingScenePath);
            if (building.IsValid() && building.isDirty)
            {
                throw new InvalidOperationException("积木王国场景有未保存修改，先保存再摆背景区域。");
            }

            BackdropProfile brick = AssetDatabase.LoadAssetAtPath<BackdropProfile>(DataFolder + "/" + BrickProfile + ".asset");
            BackdropProfile boss = AssetDatabase.LoadAssetAtPath<BackdropProfile>(DataFolder + "/" + BrickBossProfile + ".asset");
            if (brick == null || boss == null)
            {
                throw new InvalidOperationException("先跑菜单 ①，背景配置还没生成。");
            }

            bool openedHere = !building.IsValid();
            if (openedHere) building = EditorSceneManager.OpenScene(BuildingScenePath, OpenSceneMode.Additive);

            try
            {
                EditorSceneManager.SetActiveScene(building);

                // 重跑会删掉旧区域，先把音乐字段记下来，别让手调过的配置跟着一起没。
                MusicProfile brickMusic = FindZoneMusic(building, BrickZoneName);
                MusicProfile bossMusic = FindZoneMusic(building, BrickBossZoneName);

                // 已经手拆成多段（异形）的区域不再重建 —— 那是人摆过的形状，工具算不出来。
                bool brickManual = HasMultiPartZone(building, BrickZoneName);
                bool bossManual = HasMultiPartZone(building, BrickBossZoneName);

                if (brickManual) Debug.Log("[背景] " + BrickZoneName + " 已是多段区域，保留不重建。");
                else RemoveRoot(building, BrickZoneName);
                if (bossManual) Debug.Log("[背景] " + BrickBossZoneName + " 已是多段区域，保留不重建。");
                else RemoveRoot(building, BrickBossZoneName);

                Rect area = ComputeTerrainRect(building);
                if (!brickManual)
                {
                    CreateZone(building, BrickZoneName, brick, area.center, area.size, 0, brickMusic);
                    Debug.Log("[背景] 积木区域 " + BrickZoneName + " center=" + area.center + " size=" + area.size);
                }

                if (TryBossRect(building, out Vector2 bossCenter, out Vector2 bossSize))
                {
                    if (!bossManual)
                    {
                        CreateZone(building, BrickBossZoneName, boss, bossCenter, bossSize, 10, bossMusic);
                        Debug.Log("[背景] BOSS 区域 " + BrickBossZoneName + " center=" + bossCenter + " size=" + bossSize);
                    }
                }
                else
                {
                    Debug.LogWarning("[背景] 场景里找不到 TrapFloor 或 boss_room 落点，BOSS 区域没有摆。");
                }

                EditorSceneManager.SaveScene(building);
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(building, true);
                if (active.IsValid()) EditorSceneManager.SetActiveScene(active);
            }
        }

        // ----------------------------------------------------------------
        //  配置
        // ----------------------------------------------------------------

        private static BackdropProfile EnsureProfile(string name, Sprite sprite, Color tint, Color clearColor)
        {
            string path = DataFolder + "/" + name + ".asset";
            BackdropProfile profile = AssetDatabase.LoadAssetAtPath<BackdropProfile>(path);
            bool created = profile == null;

            if (created)
            {
                profile = ScriptableObject.CreateInstance<BackdropProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            var serialized = new SerializedObject(profile);
            SerializedProperty spriteProperty = serialized.FindProperty("_sprite");
            bool needsSprite = spriteProperty.objectReferenceValue == null;
            if (!needsSprite && sprite != null)
            {
                // 老的纯色渐变已经退役：只在这种情况自动换图，不覆盖手挑过的背景。
                string current = AssetDatabase.GetAssetPath(spriteProperty.objectReferenceValue);
                needsSprite = current.Replace('\\', '/').Contains("/Gradient/");
            }

            if (needsSprite && sprite != null) spriteProperty.objectReferenceValue = sprite;

            if (created)
            {
                serialized.FindProperty("_tint").colorValue = tint;
                serialized.FindProperty("_clearColor").colorValue = clearColor;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ----------------------------------------------------------------
        //  预制体
        // ----------------------------------------------------------------

        private static void WireRuntime(BackdropProfile defaultProfile)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(RuntimePrefabPath);

            try
            {
                Transform cameraTransform = FindDeep(root.transform, "Main Camera");
                if (cameraTransform == null)
                {
                    throw new InvalidOperationException("LevelRuntime 里找不到 Main Camera。");
                }

                Transform backdrop = cameraTransform.Find("Backdrop");
                if (backdrop == null)
                {
                    var host = new GameObject("Backdrop");
                    host.transform.SetParent(cameraTransform, false);
                    backdrop = host.transform;
                }

                SpriteRenderer front = EnsureRenderer(backdrop, "Backdrop_Front", BackdropDirector.FrontOrder);
                SpriteRenderer back = EnsureRenderer(backdrop, "Backdrop_Back", BackdropDirector.BackOrder);

                BackdropDirector director = backdrop.GetComponent<BackdropDirector>();
                if (director == null) director = backdrop.gameObject.AddComponent<BackdropDirector>();

                var serialized = new SerializedObject(director);
                serialized.FindProperty("_defaultProfile").objectReferenceValue = defaultProfile;
                serialized.FindProperty("_front").objectReferenceValue = front;
                serialized.FindProperty("_back").objectReferenceValue = back;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, RuntimePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static SpriteRenderer EnsureRenderer(Transform parent, string name, int order)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }

            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = child.gameObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            renderer.enabled = false;
            return renderer;
        }

        // ----------------------------------------------------------------
        //  区域
        // ----------------------------------------------------------------

        /// <summary>这个名字的区域是否已经被手拆成多段（工具不再重建它）。</summary>
        private static bool HasMultiPartZone(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != name) continue;
                BackdropZone zone = root.GetComponent<BackdropZone>();
                return zone != null && zone.HasParts && zone.Parts.Count > 1;
            }
            return false;
        }

        private static MusicProfile FindZoneMusic(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != name) continue;
                BackdropZone zone = root.GetComponent<BackdropZone>();
                return zone != null ? zone.Music : null;
            }
            return null;
        }

        private static void CreateZone(Scene scene, string name, BackdropProfile profile,
            Vector2 center, Vector2 size, int priority, MusicProfile music = null)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = new Vector3(center.x, center.y, 0f);

            BackdropZone zone = go.AddComponent<BackdropZone>();
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("_profile").objectReferenceValue = profile;
            if (music != null) serialized.FindProperty("_music").objectReferenceValue = music;
            serialized.FindProperty("_size").vector2Value = size;

            // 从第一段开始：之后可以在 Inspector 里继续加矩形拼异形。
            SerializedProperty parts = serialized.FindProperty("_parts");
            parts.arraySize = 1;
            SerializedProperty part = parts.GetArrayElementAtIndex(0);
            part.FindPropertyRelative("Center").vector2Value = Vector2.zero;
            part.FindPropertyRelative("Size").vector2Value = size;

            serialized.FindProperty("_priority").intValue = priority;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>地形范围：取「有 TilemapCollider2D 的层」实际有瓦片的格子并集，再向外留 4 米。</summary>
        /// <remarks>
        /// <para><b>为什么不用 Renderer.bounds</b>：Mask 层画到 x −95…281、y −110…46，
        /// 还有一颗游离的 CityTrackBomb 在 y≈−12552 —— 用包围盒会把区域撑到把出生地也罩进去。</para>
        /// <para><b>为什么只取有碰撞的层</b>：Logic 才是玩家走得到的地形（积木王国 x −15…257、y −95…−19）；
        /// 遮罩 / 装饰只是画上去的。</para>
        /// </remarks>
        private static Rect ComputeTerrainRect(Scene scene)
        {
            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Tilemap map in root.GetComponentsInChildren<Tilemap>(true))
                {
                    if (map.GetComponent<TilemapCollider2D>() == null) continue;
                    BoundsInt cells = map.cellBounds;
                    foreach (Vector3Int cell in cells.allPositionsWithin)
                    {
                        if (map.GetTile(cell) == null) continue;
                        Vector3 world = map.transform.TransformPoint(cell);
                        minX = Mathf.Min(minX, world.x);
                        minY = Mathf.Min(minY, world.y);
                        maxX = Mathf.Max(maxX, world.x);
                        maxY = Mathf.Max(maxY, world.y);
                        any = true;
                    }
                }
            }

            if (!any) throw new InvalidOperationException("场景里没有带碰撞的 Tilemap，算不出积木区域。");

            const float margin = 4f;
            return new Rect(minX - margin, minY - margin,
                maxX - minX + margin * 2f, maxY - minY + margin * 2f);
        }

        /// <summary>BOSS 区域：塌陷地板上方 3 米 到 坑底落点下方 3 米。</summary>
        private static bool TryBossRect(Scene scene, out Vector2 center, out Vector2 size)
        {
            TrapFloor floor = null;
            PlayerSpawnPoint spawn = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (floor == null) floor = root.GetComponentInChildren<TrapFloor>(true);
                if (spawn == null)
                {
                    spawn = root.GetComponentsInChildren<PlayerSpawnPoint>(true)
                        .FirstOrDefault(point => point.Id == "boss_room");
                }
            }

            if (floor == null || spawn == null)
            {
                center = default;
                size = default;
                return false;
            }

            Vector3 top = floor.transform.position;
            Vector3 bottom = spawn.transform.position;
            float topY = top.y + 3f;
            float bottomY = bottom.y - 3f;

            center = new Vector2((top.x + bottom.x) * 0.5f, (topY + bottomY) * 0.5f);
            size = new Vector2(BossZoneWidth, Mathf.Max(10f, topY - bottomY));
            return true;
        }

        // ----------------------------------------------------------------
        //  杂项
        // ----------------------------------------------------------------

        private static void RemoveRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindDeep(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogWarning("[背景] 找不到 Sprite：" + path);
            return sprite;
        }

        // 说明：背景层接线工具，菜单 ① ②。
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = path.Substring(0, path.LastIndexOf('/'));
            string leaf = path.Substring(path.LastIndexOf('/') + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}