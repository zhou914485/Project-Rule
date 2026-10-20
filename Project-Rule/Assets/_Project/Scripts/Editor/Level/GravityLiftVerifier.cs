using UnityEditor;
using UnityEngine;
using RuleGame.Editor.Rules;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 五档滑轨档位映射的验证器：把「重力票 + 质量票 → 五档」的对应关系钉死。
    /// </summary>
    /// <remarks>
    /// <para><b>不需要 Play 模式</b>：判据是纯函数 <see cref="GravityLift.ResolveTier"/>
    /// （不读字段、不碰 Unity 对象），摆位那部分也只是给五段高度排序。
    /// 所以改完重力 / 质量数值或档位映射，随时点一下就能回归。</para>
    /// <para>重力向量用的是 <c>GravityRule</c> 里那四个常量的实际值；质量倍率用的是 <c>MassRule</c> 的常量
    /// —— 改那边就要同步改这里。</para>
    /// <para>用法：菜单 规则游戏/验证/五档滑轨（重力×质量）。失败的断言按 <c>LogError</c> 输出。</para>
    /// </remarks>
    public static class GravityLiftVerifier
    {
        /// <summary>工程默认重力（与 <c>GravityRule.Normal</c> 同一口径）。</summary>
        private const float RefG = 9.81f;

        /// <summary>重力默认（向下分量 9.81）。</summary>
        private static readonly Vector2 GravityNormal = new Vector2(0f, -9.81f);

        /// <summary>重力 ×2（向下分量 19.62）。</summary>
        private static readonly Vector2 GravityHeavy = new Vector2(0f, -19.62f);

        /// <summary>重力 ×0.3（向下分量 2.943）。</summary>
        private static readonly Vector2 GravityLight = new Vector2(0f, -2.943f);

        /// <summary>重力反转（朝上）。</summary>
        private static readonly Vector2 GravityReverted = new Vector2(0f, 9.81f);

        [MenuItem("规则游戏/验证/五档滑轨（重力×质量）")]
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
            //  ① 单因子：只动重力 / 只动质量 → 各一档
            // ==============================================================
            h.Section("1 单因子 → 一档");

            TierCase(h, "无规则（重力 1×、质量 1×）",
                     GravityNormal, Mass(1f), 1f, GravityLift.Tier.Middle);

            TierCase(h, "只动重力：×2（变重）",
                     GravityHeavy, Mass(1f), 1f, GravityLift.Tier.Low);
            TierCase(h, "只动重力：×0.3（变轻）",
                     GravityLight, Mass(1f), 1f, GravityLift.Tier.High);
            TierCase(h, "只动重力：×0（失效）",
                     Vector2.zero, Mass(1f), 1f, GravityLift.Tier.High);
            TierCase(h, "只动重力：反转（g 朝上）",
                     GravityReverted, Mass(1f), 1f, GravityLift.Tier.High);

            TierCase(h, "只动质量：×3（变重）",
                     GravityNormal, Mass(3f), 1f, GravityLift.Tier.Low);
            TierCase(h, "只动质量：×0.3（变轻）",
                     GravityNormal, Mass(0.3f), 1f, GravityLift.Tier.High);
            TierCase(h, "只动质量：×0.05（失效）",
                     GravityNormal, Mass(0.05f), 1f, GravityLift.Tier.High);

            // 质量反转是逐台的：基准 3 变轻、基准 0.3 变重。
            AttributeValues inverted = Mass(1f);
            inverted.MassInverted = true;
            TierCase(h, "质量反转 + 基准 3（重的变轻）",
                     GravityNormal, inverted, 3f, GravityLift.Tier.High);
            TierCase(h, "质量反转 + 基准 0.3（轻的变重）",
                     GravityNormal, inverted, 0.3f, GravityLift.Tier.Low);
            TierCase(h, "质量反转 + 基准 1（不偏不倚）",
                     GravityNormal, inverted, 1f, GravityLift.Tier.Middle);

            // ==============================================================
            //  ② 叠加：同时减弱上两档 / 同时增强下两档 / 一加一减抵消
            // ==============================================================
            h.Section("2 两因子叠加");

            TierCase(h, "重力×0.3 + 质量×0.3 → 上两档",
                     GravityLight, Mass(0.3f), 1f, GravityLift.Tier.Highest);
            TierCase(h, "重力×0 + 质量×0.05 → 上两档",
                     Vector2.zero, Mass(0.05f), 1f, GravityLift.Tier.Highest);
            TierCase(h, "重力反转 + 质量×0.3 → 上两档",
                     GravityReverted, Mass(0.3f), 1f, GravityLift.Tier.Highest);
            TierCase(h, "重力×2 + 质量×3 → 下两档",
                     GravityHeavy, Mass(3f), 1f, GravityLift.Tier.Lowest);

            TierCase(h, "重力×2 + 质量×0.3 → 抵消回中档",
                     GravityHeavy, Mass(0.3f), 1f, GravityLift.Tier.Middle);
            TierCase(h, "重力×0.3 + 质量×3 → 抵消回中档",
                     GravityLight, Mass(3f), 1f, GravityLift.Tier.Middle);

            // ==============================================================
            //  ③ 判据细节
            // ==============================================================
            h.Section("3 判据细节");

            TierCase(h, "水平分量不参与（(9, −9.81) 仍是中档）",
                     new Vector2(9f, -9.81f), Mass(1f), 1f, GravityLift.Tier.Middle);
            TierCase(h, "基准内的小偏差 (−9.815) 仍是中档",
                     new Vector2(0f, -9.815f), Mass(1f), 1f, GravityLift.Tier.Middle);

            h.Check("重力票：+1 / −1 / 0",
                    GravityLift.GravityVote(GravityLight, RefG) == 1
                    && GravityLift.GravityVote(GravityHeavy, RefG) == -1
                    && GravityLift.GravityVote(GravityNormal, RefG) == 0,
                    $"{GravityLift.GravityVote(GravityLight, RefG)} / " +
                    $"{GravityLift.GravityVote(GravityHeavy, RefG)} / " +
                    $"{GravityLift.GravityVote(GravityNormal, RefG)}");

            h.Check("质量票：+1 / −1 / 0",
                    GravityLift.MassVote(Mass(0.3f), 1f) == 1
                    && GravityLift.MassVote(Mass(3f), 1f) == -1
                    && GravityLift.MassVote(Mass(1f), 1f) == 0,
                    $"{GravityLift.MassVote(Mass(0.3f), 1f)} / " +
                    $"{GravityLift.MassVote(Mass(3f), 1f)} / " +
                    $"{GravityLift.MassVote(Mass(1f), 1f)}");

            h.Check("档位序号 0..4 连续", GravityLift.TierCount == 5,
                    GravityLift.TierCount.ToString());

            // ==============================================================
            //  ④ 五档高度
            // ==============================================================
            h.Section("4 五档高度");

            // ⚠️ 先建**未激活**的物体再加组件：否则 OnEnable 会在编辑模式下跑，
            //    组件找不到滑块会往控制台丢一条「没有滑块」的报错，污染验证输出。
            var go = new GameObject("GravityLiftVerify") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            var lift = go.AddComponent<GravityLift>();

            lift.SetTiers(2f, 1f, 4f);   // 中 2、每档升 1、每档沉 4（故意不对称）
            Expect(h, lift, GravityLift.Tier.Middle,  2f);
            Expect(h, lift, GravityLift.Tier.High,    3f);
            Expect(h, lift, GravityLift.Tier.Highest, 4f);
            Expect(h, lift, GravityLift.Tier.Low,    -2f);
            Expect(h, lift, GravityLift.Tier.Lowest, -6f);

            lift.SetTiers(0f, -1f, -1f);
            h.Check("负的档高被夹到 0（不会倒过来走）",
                    lift.RiseHeight == 0f && lift.SinkDepth == 0f,
                    $"升={lift.RiseHeight}, 沉={lift.SinkDepth}");

            lift.SetReferenceGravity(-5f);
            h.Check("负的重力基准被夹到正数（判定不会整体反过来）",
                    lift.ReferenceGravity > 0f,
                    lift.ReferenceGravity.ToString("0.###"));

            Object.DestroyImmediate(go);

            return h.Report("五档滑轨（重力×质量）验证");
        }

        /// <summary>构造一组质量属性。</summary>
        private static AttributeValues Mass(float scale)
        {
            AttributeValues v = AttributeValues.Default;
            v.MassScale = scale;
            return v;
        }

        private static void TierCase(RuleVerifyHarness h, string name,
                                     Vector2 gravity, AttributeValues mass, float baseMass,
                                     GravityLift.Tier want)
        {
            GravityLift.Tier got = GravityLift.ResolveTier(gravity, RefG, mass, baseMass);
            h.Check($"{name} → {want}", got == want, $"得到 {got}");
        }

        private static void Expect(RuleVerifyHarness h, GravityLift lift, GravityLift.Tier tier, float want)
        {
            float got = lift.HeightOf(tier);
            h.Check($"{GravityLift.TierName(tier)} 本地 y = {want:0.###}",
                    Mathf.Abs(got - want) < RuleVerifyHarness.Tolerance,
                    got.ToString("0.###"));
        }
    }
}
