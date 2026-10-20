using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成**拳击手套**的预制体：开关一响就弹出去，把出拳方向上碰到的东西推飞。
    /// </summary>
    /// <remarks>
    /// <para><b>层级</b>（根 scale 恒为 1，尺寸写在子物体的局部缩放上）：
    /// <code>
    /// PunchFist               ← 根：挂 PunchFist，没有碰撞体（手套不挡路）
    /// ├─ Base                 ← 底座（CityHammer_Base）
    /// ├─ Spring               ← 弹簧（CityHammer_Spring，运行时按弹出距离拉长）
    /// └─ Head                 ← 手套（CityHammer_Glove），弹出去的就是它
    /// </code>
    /// 美术沿用积木城的摆锤素材（`Art/Sprites/Components/CityHammer_*`，PPU 130），
    /// 不另存副本 —— 与 `CityArtSetup` 的约定一致。</para>
    ///
    /// <para><b>用法</b>：拖进场景 → 旁边放一个 `CityButton` / `CityLever` →
    /// 把本物体拖到开关的 `FollowMotor` 上。拉杆（`Toggle = true`）可以反复出拳，
    /// 按钮（按下即锁死）只出一次。手套默认朝**右**打，要朝左 / 朝上就把根节点转过去。</para>
    ///
    /// <para>⚠️ <b>手套没有碰撞体</b>：它只负责推人，不挡路；玩家、敌人、纸飞机都能穿过去。
    /// 要一块「会挡路 / 能站」的机关请用 ⑭ 回弹块。</para>
    /// </remarks>
    public static class PunchFistBuilder
    {
        private const string BaseSpritePath = "Assets/_Project/Art/Sprites/Components/CityHammer_Base.png";
        private const string SpringSpritePath = "Assets/_Project/Art/Sprites/Components/CityHammer_Spring.png";
        private const string GloveSpritePath = "Assets/_Project/Art/Sprites/Components/CityHammer_Glove.png";

        private const string RootName = "PunchFist";
        private const string HeadName = "Head";
        private const string SpringName = "Spring";
        private const string BaseName = "Base";

        /// <summary>手套静止时离底座多远（世界单位）。弹出去的距离由组件上的「弹出距离」管。</summary>
        private const float RestDistance = 0.55f;

        /// <summary>弹簧的粗细缩放（贴图是竖图：宽 0.246 单位 × 0.8 ≈ 0.2 单位）。</summary>
        private const float SpringThicknessScale = 0.8f;

        private static readonly Vector3 GloveScale = new Vector3(0.55f, 0.55f, 1f);
        private static readonly Vector3 BaseScale = new Vector3(0.7f, 0.7f, 1f);

        /// <summary>排序：底座在最后面、弹簧其次、手套在最前面（角色是 order 0）。</summary>
        private const int BaseOrder = 1;
        private const int SpringOrder = 2;
        private const int GloveOrder = 3;

        /// <summary>预制体路径。菜单只认这一个。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/PunchFist.prefab";

        [MenuItem("Tools/规则游戏/地图组件/⑲ 拳击手套（开关驱动出拳）", false, 71)]
        public static void CreatePrefab()
        {
            GameObject prefab = SavePrefab(PrefabPath);
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[地图组件] 已生成预制体 {PrefabPath}：默认朝**右**出拳，弹出 2 个单位、用时 0.08 s，" +
                      "停留 0.25 s 期间持续推人（500 m/s²），然后 0.35 s 收回。\n" +
                      "· 用法：拖进场景 → 旁边放 CityButton / CityLever → 把它拖到开关的 FollowMotor 上\n" +
                      "· 拉杆（Toggle）可以反复出拳；按钮按下即锁死，只出一次\n" +
                      "· 要朝别的方向打：转根节点（出拳方向是局部 +X）");
        }

        /// <summary>把拳击手套装成预制体（旁边开个临时空场景装，不碰当前场景）。</summary>
        public static GameObject SavePrefab(string assetPath)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene temp = default;

            try
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
            catch (System.InvalidOperationException)
            {
                temp = default;   // 当前场景没保存过 —— 退回落地方案
            }

            GameObject root = null;
            try
            {
                root = new GameObject(RootName);
                if (temp.IsValid()) SceneManager.MoveGameObjectToScene(root, temp);

                if (!Build(root)) return null;

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                if (temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
                else if (root != null) Object.DestroyImmediate(root);

                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        /// <summary>往 <paramref name="root"/> 上装一套手拳。返回 false = 素材缺失（已打日志）。</summary>
        private static bool Build(GameObject root)
        {
            Sprite baseSprite = LoadSprite(BaseSpritePath);
            Sprite springSprite = LoadSprite(SpringSpritePath);
            Sprite gloveSprite = LoadSprite(GloveSpritePath);

            if (baseSprite == null || springSprite == null || gloveSprite == null) return false;

            Transform glove = BuildSprite(root.transform, HeadName, gloveSprite, GloveScale, GloveOrder);
            glove.localPosition = new Vector3(RestDistance, 0f, 0f);

            // 弹簧先按「静止长度」摆好：运行时组件会按实际弹出距离继续拉长。
            // ⚠️ CityHammer_Spring 是**竖图**（长边在 Y）：先转 90° 让长边朝 +X，再沿 Y 拉伸。
            Vector2 springSize = springSprite.bounds.size;
            bool springAlongY = springSize.y >= springSize.x;
            float springUnitLength = Mathf.Max(0.0001f, springAlongY ? springSize.y : springSize.x);

            Vector3 springScale = springAlongY
                ? new Vector3(SpringThicknessScale, RestDistance / springUnitLength, 1f)
                : new Vector3(RestDistance / springUnitLength, SpringThicknessScale, 1f);

            Transform spring = BuildSprite(root.transform, SpringName, springSprite, springScale, SpringOrder);
            if (springAlongY) spring.localRotation = Quaternion.Euler(0f, 0f, -90f);
            spring.localPosition = new Vector3(RestDistance * 0.5f, 0f, 0f);

            BuildSprite(root.transform, BaseName, baseSprite, BaseScale, BaseOrder);

            var fist = root.AddComponent<PunchFist>();
            Wire(fist, "_head", glove);
            Wire(fist, "_spring", spring);

            EditorUtility.SetDirty(fist);
            return true;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) Debug.LogError("[地图组件] 找不到精灵：" + path);
            return sprite;
        }

        /// <summary>一个只负责显示的子物体（没有碰撞体 —— 手套不挡路）。</summary>
        private static Transform BuildSprite(Transform parent, string name, Sprite sprite, Vector3 scale, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;

            return go.transform;
        }

        /// <summary>把私有序列化字段接上（组件自己不发公开 setter，预制体靠这里接线）。</summary>
        private static void Wire(Object target, string fieldName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning($"[地图组件] {target.GetType().Name} 上找不到字段 {fieldName}，预制体接线不完整。");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
