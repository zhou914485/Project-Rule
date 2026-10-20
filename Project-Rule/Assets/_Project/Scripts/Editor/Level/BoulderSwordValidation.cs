using UnityEditor;
using UnityEngine;
using RuleGame.Editor.Rules;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 巨石断剑机关的**纯判据回归**：把「什么条件下碎、什么条件下拔得出来」钉死。
    /// </summary>
    /// <remarks>
    /// <para><b>不需要 Play 模式</b>：这里只测纯函数（<see cref="GravityBoulder.ShouldBreak"/> /
    /// <see cref="SwordInStone.IsFreeUnder"/>）与组件的形态机，不碰物理。
    /// 整条物理流程（举起来 → 撞按钮 → 落下来碎 → 拔剑）由 Play 模式的
    /// <c>BoulderSwordSmokeTest</c> 跑，见试验场景 <c>Scenes/Test_BoulderSword.unity</c>。</para>
    ///
    /// <para>⚠️ 这里不测「锁死 → 解锁」的运行时行为（<c>Awake</c> 才会把刚体切成静态，
    /// 编辑模式下的临时物体不走 Awake）—— 那一条由 Play 模式的
    /// <c>BoulderSwordSmokeTest</c> 的第 ①②③ 步验。</para>
    ///
    /// <para>最后一段是**参数自洽**：把生成工具里的几个常量代进自由落体公式，
    /// 检查「这套摆位还砸得碎、还撞得响」。以后谁把按钮调高了 / 把巨石调轻了，
    /// 这里会先红。</para>
    ///
    /// <para>用法：菜单 规则游戏/验证/巨石断剑机关。失败的断言按 <c>LogError</c> 输出。</para>
    /// </remarks>
    public static class BoulderSwordValidation
    {
        /// <summary>工程里的重力（与 <c>GravityRule.Normal</c> 一致）。</summary>
        private const float Gravity = 9.81f;

        /// <summary>CityButton 触发区高度（见 <c>Prefabs/BrickCity/CityButton.prefab</c> 的 BoxCollider2D）。</summary>
        private const float ButtonTriggerHeight = 0.35f;

        /// <summary>物理步进频率（Project Settings → Time → Fixed Timestep 默认 0.02）。</summary>
        private const float PhysicsHz = 50f;

        [MenuItem("规则游戏/验证/巨石断剑机关")]
        public static void Run()
        {
            string report = Verify();

            if (report.Contains("✗")) Debug.LogError(report);
            else Debug.Log(report);
        }

        /// <summary>跑一遍并返回报告（不含菜单入口，方便别处复用）。</summary>
        public static string Verify()
        {
            var h = new RuleVerifyHarness();

            // ==============================================================
            //  ① 落地判据
            // ==============================================================
            h.Section("1 落地速度 → 碎不碎");

            h.Check("轻轻落下（1）不碎", !GravityBoulder.ShouldBreak(1f, 6f), "不该碎");
            h.Check("差一点点（5.99）不碎", !GravityBoulder.ShouldBreak(5.99f, 6f), "不该碎");
            h.Check("刚好够（6）就碎", GravityBoulder.ShouldBreak(6f, 6f), "应该碎");
            h.Check("砸得狠（9.2）必碎", GravityBoulder.ShouldBreak(9.2f, 6f), "应该碎");

            // ==============================================================
            //  ①′ 「从下面接住」才算落地 —— 砸到按钮上不算
            // ==============================================================
            h.Section("1′ 从下面接住才算落地（顶到按钮 / 天花板不算）");

            h.Check("落地：接触点在巨石中心下方 → 算",
                    GravityBoulder.IsContactBelow(0f, 1.2f), "不该拒绝");
            h.Check("顶到天花板：接触点在中心**上方** → 不算（这就是那个 bug）",
                    !GravityBoulder.IsContactBelow(7.75f, 6.55f), "不该算落地");
            h.Check("侧着蹭墙：接触点与中心齐平 → 不算",
                    !GravityBoulder.IsContactBelow(1.2f, 1.2f), "不该算落地");
            h.Check("脚下一点点（容差内）→ 不算",
                    !GravityBoulder.IsContactBelow(1.15f, 1.2f), "不该算落地");
            h.Check("脚下够低（超出容差）→ 算",
                    GravityBoulder.IsContactBelow(1.05f, 1.2f), "应该算落地");

            // ==============================================================
            //  ② 解锁判据：反转 ≠ 变小
            // ==============================================================
            h.Section("2 摩擦规则 → 剑松不松");

            AttributeValues normal = AttributeValues.Default;
            AttributeValues inverted = AttributeValues.Default;
            inverted.FrictionInverted = true;          // 「摩擦 × 反转」：倍率仍是 1
            AttributeValues weakened = AttributeValues.Default;
            weakened.FrictionScale = 0.3f;             // 「摩擦 × 削弱」
            AttributeValues nullified = AttributeValues.Default;
            nullified.FrictionScale = 0f;              // 「摩擦 × 失效」
            AttributeValues enhanced = AttributeValues.Default;
            enhanced.FrictionScale = 3f;               // 「摩擦 × 增强」

            h.Check("正常摩擦 → 锁死",
                    !SwordInStone.IsFreeUnder(normal, true, false, 1f), "不该解锁");
            h.Check("摩擦 × 反转 → 解锁（这是机关要的钥匙）",
                    SwordInStone.IsFreeUnder(inverted, true, false, 1f), "应该解锁");
            h.Check("摩擦 × 削弱 → **不**解锁（与生锈齿轮刻意错开）",
                    !SwordInStone.IsFreeUnder(weakened, true, false, 1f), "削弱不该拔得动");
            h.Check("摩擦 × 失效 → 也不解锁",
                    !SwordInStone.IsFreeUnder(nullified, true, false, 1f), "失效不该拔得动");
            h.Check("摩擦 × 增强 → 锁得更死",
                    !SwordInStone.IsFreeUnder(enhanced, true, false, 1f), "不该解锁");
            h.Check("反转被关掉时反转也不解锁",
                    !SwordInStone.IsFreeUnder(inverted, false, false, 1f), "开关没生效");
            h.Check("勾上「也认摩擦变小」之后，削弱能解锁",
                    SwordInStone.IsFreeUnder(weakened, true, true, 1f), "应该解锁");
            h.Check("勾上「也认摩擦变小」之后，失效也能解锁",
                    SwordInStone.IsFreeUnder(nullified, true, true, 1f), "应该解锁");

            // ==============================================================
            //  ③ 形态机
            // ==============================================================
            h.Section("3 巨石形态机");

            var go = new GameObject("BoulderSwordVerify") { hideFlags = HideFlags.HideAndDontSave };
            // ⚠️ 先建**未激活**的物体再加组件：否则 OnEnable 会在编辑模式下跑。
            go.SetActive(false);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<BoxCollider2D>();
            var boulder = go.AddComponent<GravityBoulder>();

            h.Check("初始 = 完整", boulder.CurrentState == GravityBoulder.State.Intact, boulder.CurrentState.ToString());
            h.Check("完整态摔不碎（必须先被按钮震裂）", !boulder.IsBreakable, "完整态不该可碎");

            boulder.Arm();
            h.Check("震裂后 = 开裂", boulder.CurrentState == GravityBoulder.State.Armed, boulder.CurrentState.ToString());
            h.Check("震裂后才摔得碎", boulder.IsBreakable, "开裂态应该可碎");

            boulder.Arm();
            h.Check("重复震裂是幂等的", boulder.CurrentState == GravityBoulder.State.Armed, boulder.CurrentState.ToString());

            boulder.Shatter();
            h.Check("碎开后 = 已碎", boulder.CurrentState == GravityBoulder.State.Shattered, boulder.CurrentState.ToString());
            h.Check("碎开之后不再可碎", !boulder.IsBreakable, "已碎不该还可碎");

            // 没接碎块模板 / 剑时也得能碎（不能 NRE）—— 上面这条已经覆盖了。

            var go2 = new GameObject("BoulderSwordVerifyNoArm") { hideFlags = HideFlags.HideAndDontSave };
            go2.SetActive(false);
            go2.AddComponent<Rigidbody2D>();
            go2.AddComponent<BoxCollider2D>();
            var unarmed = go2.AddComponent<GravityBoulder>();

            var serialized = new SerializedObject(unarmed);
            SerializedProperty requireArming = serialized.FindProperty("_requireArming");
            requireArming.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            h.Check("关掉「必须先震裂」后，完整态也摔得碎",
                    unarmed.IsBreakable, "开关没生效");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);

            // ==============================================================
            //  ④ 白盒标签
            // ==============================================================
            h.Section("4 白盒标签");

            h.Check("完整 → 「击碎巨石」",
                    GravityBoulder.LabelText(GravityBoulder.State.Intact) == "击碎巨石",
                    GravityBoulder.LabelText(GravityBoulder.State.Intact));
            h.Check("开裂 → 「击碎巨石 · 开裂」",
                    GravityBoulder.LabelText(GravityBoulder.State.Armed) == "击碎巨石 · 开裂",
                    GravityBoulder.LabelText(GravityBoulder.State.Armed));
            h.Check("已碎 → 「击碎巨石 · 已碎」",
                    GravityBoulder.LabelText(GravityBoulder.State.Shattered) == "击碎巨石 · 已碎",
                    GravityBoulder.LabelText(GravityBoulder.State.Shattered));
            h.Check("剑锁死 → 「剑 · 锁死」",
                    SwordInStone.LabelText(false) == "剑 · 锁死",
                    SwordInStone.LabelText(false));
            h.Check("剑可拔 → 「剑 · 可拔」",
                    SwordInStone.LabelText(true) == "剑 · 可拔",
                    SwordInStone.LabelText(true));

            // 锁定状态优先于形态：锁着的时候不管裂没裂，都写「锁死」。
            h.Check("锁死 → 「击碎巨石 · 锁死」",
                    GravityBoulder.LabelText(GravityBoulder.State.Intact, anchored: true) == "击碎巨石 · 锁死",
                    GravityBoulder.LabelText(GravityBoulder.State.Intact, anchored: true));
            h.Check("锁死 + 已开裂 → 还是「击碎巨石 · 锁死」",
                    GravityBoulder.LabelText(GravityBoulder.State.Armed, anchored: true) == "击碎巨石 · 锁死",
                    GravityBoulder.LabelText(GravityBoulder.State.Armed, anchored: true));

            // ==============================================================
            //  ⑤ 参数自洽（改常量会不会把谜题弄坏）
            // ==============================================================
            h.Section("5 参数自洽（生成工具的那几个常量）");

            // 巨石至少要升到 CityButton 那儿（+LiftHeight），从那儿落回地面就该够碎。
            float drop = BoulderSwordBuilder.LiftHeight - BoulderSwordBuilder.BoulderSize.y * 0.5f;
            float impact = Mathf.Sqrt(2f * Gravity * drop);

            h.Check($"从按钮高度（{drop:0.##} 米）落回来，落地速度 {impact:0.##} > 碎裂阈值 6",
                    GravityBoulder.ShouldBreak(impact, 6f),
                    $"只有 {impact:0.##}，砸不碎 —— 把 LiftHeight 调高，或把 _breakSpeed 调低");

            // 按钮是**接触式**的（CityButton 的 Trigger），没有动量阈值 ——
            // 但它不能**穿过去**：巨石在最高速时一帧走的距离必须小于触发区高度。
            float perStep = impact / PhysicsHz;
            h.Check($"穿过按钮触发区时一物理帧走 {perStep:0.###} < 触发区高 {ButtonTriggerHeight:0.##}",
                    perStep < ButtonTriggerHeight,
                    $"一帧走 {perStep:0.###}，会直接穿过去不触发 —— 把 LiftHeight 调低，或把触发区做高");

            return h.Report("巨石断剑机关验证");
        }
    }
}
