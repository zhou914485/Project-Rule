using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成**危险跳绳**的预制体：一条碰到就受伤的旋转曲线。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是预制体</b>：跳绳在关卡里会摆好几条（走廊 / 竖井 / 平台之间的缺口），
    /// 每条都要「绳子 + 两个端点装饰」齐全。手搭最容易漏的是**段用的精灵**和
    /// **端点装饰的位置** —— 前者漏了运行时只剩一条几乎看不见的细线，后者漏了就分不清
    /// 「两端点距离」调的是哪两个点。</para>
    ///
    /// <para><b>结构</b>：根 <c>SwingRope</c>（挂 <see cref="SwingRope"/>，无碰撞体）
    /// + 子物体 <c>AnchorA</c> / <c>AnchorB</c>（白盒圆，纯视觉）。</para>
    ///
    /// <para>⚠️ 绳子**没有碰撞体**：它不是墙，不用跳过去，而是「跳过判定」。
    /// 判定只认受击箱，所以玩家擦着绳子边走过去不会掉血。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑱ 危险跳绳（旋转曲线陷阱）</c> 生成
    /// <c>Prefabs/SwingRope.prefab</c>，关卡里拖任意多个。</para>
    /// </remarks>
    public static class SwingRopeBuilder
    {
        /// <summary>段用的精灵：1×1 单位的白盒方块，被拉长成绳子的一节。</summary>
        private const string SegmentSpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";

        /// <summary>端点装饰：1 单位的白盒圆。</summary>
        private const string AnchorSpritePath = "Assets/_Project/Art/Sprites/WhiteBoxCircle.png";

        private const string RootName = "SwingRope";
        private const string AnchorAName = "AnchorA";
        private const string AnchorBName = "AnchorB";

        /// <summary>端点装饰的直径（单位）。</summary>
        private const float AnchorSize = 0.45f;

        private static readonly Color AnchorColor = new Color(0.24f, 0.20f, 0.17f, 1f);

        /// <summary>预制体路径。菜单只认这一个。</summary>
        public const string PrefabPath = "Assets/_Project/Prefabs/SwingRope.prefab";

        [MenuItem("Tools/规则游戏/地图组件/⑱ 危险跳绳（旋转曲线陷阱）", false, 66)]
        public static void CreatePrefab()
        {
            GameObject prefab = SavePrefab(PrefabPath);
            if (prefab == null) return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            Debug.Log($"[地图组件] 已生成预制体 {PrefabPath}：一条 6 米宽、垂 2 米的旋转曲线。\n" +
                      "· 用法：拖进场景，位置就是**绳子中点**；根物体的旋转可以把整根绳倾斜\n" +
                      "· 三个手感参数：两端点距离（_span）· 曲线到中轴的长度（_sag）· 转速（_spinSpeed 度/秒）\n" +
                      "· 碰到就受伤，伤害 / 击退与尖刺同一条路（默认 10 / 6，0.5 秒内同一目标不重复结算）\n" +
                      "· 绳子**没有碰撞体**：不用跳过去，而是跳过判定；判定只认受击箱\n" +
                      "· ⚠️ 场景里要有 CombatSystem（没有的话绳子打不出伤害，Console 会警告）");
        }

        /// <summary>把跳绳装成预制体（旁边开个临时空场景装，不碰当前场景）。</summary>
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

        /// <summary>往 <paramref name="root"/> 上装一条跳绳。返回 false = 资源缺失（已打日志）。</summary>
        private static bool Build(GameObject root)
        {
            Sprite segmentSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SegmentSpritePath);
            if (segmentSprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SegmentSpritePath);
                return false;
            }

            Sprite anchorSprite = AssetDatabase.LoadAssetAtPath<Sprite>(AnchorSpritePath);
            if (anchorSprite == null)
            {
                Debug.LogWarning("[地图组件] 找不到白盒圆：" + AnchorSpritePath + "，端点装饰会留空。");
            }

            Transform anchorA = BuildAnchor(root.transform, AnchorAName, anchorSprite, -1f);
            Transform anchorB = BuildAnchor(root.transform, AnchorBName, anchorSprite, 1f);

            var rope = root.AddComponent<SwingRope>();
            rope.Configure(anchorA, anchorB, segmentSprite);
            EditorUtility.SetDirty(rope);

            return true;
        }

        /// <summary>端点装饰：一个白盒圆。位置由 <see cref="SwingRope"/> 按「两端点距离」摆。</summary>
        private static Transform BuildAnchor(Transform parent, string name, Sprite sprite, float side)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // 先摆个默认位置（3 米 = 预制体默认 6 米跨度的一半），运行时会被组件重算。
            go.transform.localPosition = new Vector3(side * 3f, 0f, 0f);
            go.transform.localScale = new Vector3(AnchorSize, AnchorSize, 1f);

            if (sprite == null) return go.transform;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = AnchorColor;
            renderer.sortingOrder = 5;

            return go.transform;
        }
    }
}
