using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成**跷跷板**的预制体：两端比质量的杠杆机关。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是预制体</b>：跷跷板在关卡里要摆好几个（载人上高台 / 挡门的重物 / 谜题的一环），
    /// 每个都要「支点 + 会转的板 + Kinematic 刚体 + Ground 层碰撞体」齐全，
    /// 手搭一遍最容易漏掉的就是**层**（放错层玩家直接穿过去）和**刚体类型**。</para>
    ///
    /// <para><b>结构（三层，缺一不可）</b>：</para>
    /// <list type="number">
    /// <item><b>根 <c>Seesaw</c></b> —— 位置 = **支点**，住 <c>Ground</c> 层，挂 <see cref="Seesaw"/>。
    /// 它自己**不转**（所以下面的支点装饰才是直的）。</item>
    /// <item><b>子物体 <c>Board</c></b> —— 真正会转的板：Kinematic 刚体 + 实心碰撞体 + <c>Ground</c> 层。
    /// 它的 <c>localPosition</c> 是零，所以绕自己的原点转 = **绕支点转**。</item>
    /// <item><b>子物体 <c>Pivot</c></b> —— 支点装饰（白盒方块转 45°），纯视觉、没有碰撞体。</item>
    /// </list>
    ///
    /// <para>⚠️ 板的尺寸写在 <c>localScale</c> 上（碰撞体 <c>size</c> 保持 1×1）——
    /// 本工程的白盒方块是 1×1 单位，两者正好一致；把尺寸同时写进 <c>size</c> 和 <c>localScale</c>
    /// 会被平方放大（实测踩过这个坑）。要改板长就改 <c>Board</c> 的 <c>localScale.x</c>，
    /// 检测盒与转动惯量会跟着走。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑰ 跷跷板（两端比质量）</c> 生成
    /// <c>Prefabs/Seesaw.prefab</c>，关卡里拖任意多个。</para>
    /// </remarks>
    public static class SeesawBuilder
    {
        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";

        /// <summary>
        /// 板**专用**物理材质：摩擦 1、**Combine = Maximum**。
        /// </summary>
        /// <remarks>
        /// 不耐磨的板（默认 μ0.4）在 25° 时，石头的重力分量超过静摩擦，会顺着斜面滑下去。
        /// Maximum = 两个接触面里取大的那个：石头自己的 0.5 压不住，板这边 1.0 一定生效。
        /// </remarks>
        private const string MaterialPath = "Assets/_Project/Data/Physics/SeesawMaterial2D.asset";

        /// <summary>板必须落这个层：玩家的 Slide 与接地探测只认它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "Seesaw";
        private const string BoardName = "Board";
        private const string PivotName = "Pivot";

        /// <summary>板的世界尺寸（单位）：6 × 0.35 ≈ 12 格长、七分格厚。</summary>
        private static readonly Vector2 BoardSize = new Vector2(6f, 0.35f);

        /// <summary>支点装饰的边长（单位）。</summary>
        private const float PivotSize = 0.7f;

        private static readonly Color BoardColor = new Color(0.86f, 0.72f, 0.52f, 1f);
        private static readonly Color PivotColor = new Color(0.36f, 0.34f, 0.40f, 1f);

        /// <summary>预制体路径。菜单只认这一个。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/Seesaw.prefab";

        [MenuItem("Tools/规则游戏/地图组件/⑰ 跷跷板（两端比质量）", false, 65)]
        public static void CreatePrefab()
        {
            GameObject prefab = SavePrefab(PrefabPath);
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[地图组件] 已生成预制体 {PrefabPath}：板 {BoardSize.x} × {BoardSize.y} 单位，支点在正中，Ground 层、高摩擦材质。\n" +
                      "· 用法：拖进场景，位置就是**支点**；两端各放一个重物（石头 18 kg / 铁箱 8 kg），重的那头沉\n" +
                      "· **玩法全在规则上**：「质量 × 反转」把两端轻重互换 → 跷跷板自己翻过来\n" +
                      "· 手感在 Inspector：最大倾角 25°、翻动速度 90°/秒\n" +
                      "· 板长改 Board 子物体的 localScale.x（**别**改碰撞体的 size，会平方放大）");
        }

        /// <summary>把跷跷板装成预制体（旁边开个临时空场景装，不碰当前场景）。</summary>
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

        /// <summary>往 <paramref name="root"/> 上装一个跷跷板。返回 false = 资源 / 层缺失（已打日志）。</summary>
        private static bool Build(GameObject root)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return false;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，跷跷板放下去玩家会直接穿过去。");
                return false;
            }

            root.layer = layer;

            Transform board = BuildBoard(root.transform, sprite, layer);
            BuildPivot(root.transform, sprite, layer);

            var seesaw = root.AddComponent<Seesaw>();
            seesaw.SetBoard(board);
            EditorUtility.SetDirty(seesaw);

            return true;
        }

        /// <summary>
        /// 会转的板：<b>localPosition 保持零</b>（转自己的原点 = 绕支点转），尺寸写在 localScale 上。
        /// </summary>
        private static Transform BuildBoard(Transform parent, Sprite sprite, int layer)
        {
            var go = new GameObject(BoardName);
            go.transform.SetParent(parent, false);
            go.layer = layer;

            // ⚠️ 尺寸只写在 localScale 上，碰撞体 size 保持 1×1。
            //    两个都写 = 世界尺寸变成平方（本项目老坑）。
            go.transform.localScale = new Vector3(BoardSize.x, BoardSize.y, 1f);

            var box = go.AddComponent<BoxCollider2D>();
            box.size = Vector2.one;
            box.offset = Vector2.zero;

            // 高摩擦：东西摆在板上被抬起来时不会顺着斜面滑下去。
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
            if (material != null) box.sharedMaterial = material;
            else
            {
                Debug.LogWarning("[地图组件] 找不到 " + MaterialPath +
                                 "，跷跷板会用默认摩擦（0.4），上面的重物在倾斜时可能滑下去。");
            }

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = BoardColor;
            renderer.sortingOrder = -1;   // 压在地面美术后面：站上去时人是挡在板前面的

            return go.transform;
        }

        /// <summary>支点装饰：一个转 45° 的方块，摆在板下面。**没有碰撞体**。</summary>
        private static void BuildPivot(Transform parent, Sprite sprite, int layer)
        {
            var go = new GameObject(PivotName);
            go.transform.SetParent(parent, false);
            go.layer = layer;

            // 顶面顶到板的底面：板的半厚 + 支点的一半。
            float y = -(BoardSize.y * 0.5f + PivotSize * 0.5f);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            go.transform.localScale = new Vector3(PivotSize, PivotSize, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = PivotColor;
            renderer.sortingOrder = -3;
        }
    }
}
