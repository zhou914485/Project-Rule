using System.Reflection;
using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RuleGame.Editor.Verification
{
    /// <summary>
    /// <see cref="WorldHintText"/> 的隔离场景自检：只用编辑模式 + <c>Tick()</c>，不进入 Play。
    /// </summary>
    /// <remarks>
    /// <para>为什么不开 Play：工程的 Enter Play Mode Options 关掉了 Scene Reload，Play 里的对象与编辑场景是同一批。
    /// 这里只在一个**附加的空场景**里造对象，测完关掉，不碰用户正在编辑的关卡。</para>
    /// <para>菜单：规则游戏/验证/文字提示（组件 + 隔离场景）。</para>
    /// </remarks>
    public static class WorldHintTextValidation
    {
        private const string FontPath = "Assets/_Project/UI/Fonts/NotoSansCJKsc-Regular.otf";

        [MenuItem("规则游戏/验证/文字提示（组件 + 隔离场景）", false, 74)]
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            int passed = 0;

            try
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
                Check(font != null, "中文字体可加载", ref passed);

                var root = new GameObject("WorldHintTextValidation");
                var mesh = root.AddComponent<TextMesh>();
                mesh.font = font;
                var hint = root.AddComponent<WorldHintText>();

                var renderer = root.GetComponent<MeshRenderer>();
                Check(renderer != null && renderer.sortingOrder == -10, "默认排序 −10（背景层）", ref passed);

                Transform shadow = root.transform.Find("Shadow");
                Check(shadow != null, "自动创建阴影子物体", ref passed);
                var shadowMesh = shadow != null ? shadow.GetComponent<TextMesh>() : null;
                Check(shadowMesh != null && shadowMesh.text == mesh.text, "阴影同步文字", ref passed);
                var shadowRenderer = shadow != null ? shadow.GetComponent<MeshRenderer>() : null;
                Check(shadowRenderer != null && shadowRenderer.sortingOrder == -11, "阴影排序在文字后一位", ref passed);
                Check(shadow != null &&
                      Mathf.Abs(shadow.localPosition.x - 0.035f) < 1e-4f &&
                      Mathf.Abs(shadow.localPosition.y + 0.035f) < 1e-4f, "阴影偏移", ref passed);

                // 半径逻辑只读 Transform，造一个真实 PlayerController 当「玩家」即可（编辑模式不会跑 Awake）。
                var playerGo = new GameObject("FakePlayer");
                playerGo.transform.position = new Vector3(2f, 0f, 0f);
                playerGo.AddComponent<BoxCollider2D>();
                playerGo.AddComponent<Rigidbody2D>();
                var player = playerGo.AddComponent<RuleGame.Gameplay.Player.PlayerController>();

                SetField(hint, "_player", player);
                SetField(hint, "_triggerRadius", 5f);
                SetField(hint, "_fadeDuration", 0.2f);
                SetField(hint, "_showOnce", true);
                SetField(hint, "_holdDuration", 0f);
                SetField(hint, "_showDelay", 0f);
                SetField(hint, "_bobAmplitude", 0f);
                hint.RefreshVisual();

                int shown = 0, hidden = 0;
                hint.OnShown += () => shown++;
                hint.OnHidden += () => hidden++;

                Ticks(hint, 20, 0.2f);
                Check(hint.IsVisible && hint.HasShown && shown == 1, "走进半径 → 淡入并触发 OnShown", ref passed);

                playerGo.transform.position = new Vector3(20f, 0f, 0f);
                Ticks(hint, 20, 0.2f);
                Check(!hint.IsVisible && hidden == 1, "离开半径 → 淡出并触发 OnHidden", ref passed);

                playerGo.transform.position = new Vector3(2f, 0f, 0f);
                Ticks(hint, 20, 0.2f);
                Check(!hint.IsVisible, "只显示一次：再进入不再出现", ref passed);

                hint.ResetHint();
                Ticks(hint, 10, 0.2f);
                Check(hint.IsVisible, "ResetHint 后可以重新显示", ref passed);

                hint.Hide();
                Ticks(hint, 10, 0.2f);
                Check(!hint.IsVisible, "Hide 收起", ref passed);

                hint.ShowFor(0.5f);
                Ticks(hint, 2, 0.2f);
                Check(hint.IsVisible, "ShowFor 期间强制显示", ref passed);
                Ticks(hint, 3, 0.2f);
                Check(!hint.IsVisible, "ShowFor 到点收起", ref passed);

                hint.Text = "新提示";
                Check(mesh.text == "新提示" && shadowMesh != null && shadowMesh.text == "新提示",
                      "Text setter 同步文字与阴影", ref passed);

                SetField(hint, "_shadow", false);
                hint.RefreshVisual();
                Check(shadow != null && !shadow.gameObject.activeSelf, "关闭阴影 → 子物体停用", ref passed);
                SetField(hint, "_shadow", true);
                hint.RefreshVisual();
                Check(shadow != null && shadow.gameObject.activeSelf, "重新打开阴影 → 子物体启用", ref passed);

                SetField(hint, "_triggerRadius", 0f);
                hint.ResetHint();
                Ticks(hint, 5, 0.2f);
                Check(hint.IsVisible, "半径 0 → 一直显示", ref passed);
                playerGo.transform.position = new Vector3(50f, 0f, 0f);
                Ticks(hint, 10, 0.2f);
                Check(hint.IsVisible, "半径 0 → 玩家走远仍然显示", ref passed);

                SetField(hint, "_triggerRadius", 5f);
                SetField(hint, "_holdDuration", 0.5f);
                playerGo.transform.position = new Vector3(2f, 0f, 0f);
                hint.ResetHint();
                Ticks(hint, 12, 0.2f);
                Check(!hint.IsVisible, "停留到 hold 时长 → 自动收起", ref passed);

                SetField(hint, "_holdDuration", 0f);
                SetField(hint, "_showDelay", 0.4f);
                hint.ResetHint();
                Ticks(hint, 1, 0.2f);
                Check(!hint.IsVisible, "showDelay 未到 → 不显示", ref passed);
                Ticks(hint, 4, 0.2f);
                Check(hint.IsVisible, "showDelay 到点 → 显示", ref passed);

                Debug.Log("[WorldHintText] 通过 " + passed + " 项");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Ticks(WorldHintText hint, int count, float dt)
        {
            for (int i = 0; i < count; i++) hint.Tick(dt);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new System.InvalidOperationException("[WorldHintText] 找不到字段：" + name);
            field.SetValue(target, value);
        }

        private static void Check(bool condition, string label, ref int passed)
        {
            if (!condition) throw new System.InvalidOperationException("[WorldHintText] 失败：" + label);
            passed++;
        }
    }
}
