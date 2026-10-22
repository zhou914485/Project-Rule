#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using RuleGame.Infrastructure.Rules;
using RuleGame.Core.Rules;

namespace RuleGame.Core.Rules.Debugging
{
    /// <summary>
    /// 规则系统的临时测试面板。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ <b>这是临时脚本，提交前删除。</b></para>
    /// <para>用 OnGUI 画按钮，**不依赖 Input System**，所以不需要给 asmdef 加包引用。</para>
    /// </remarks>
    public class RuleSystemTestInput : MonoBehaviour
    {
        [Header("测试目标")]
        [SerializeField] private AttributeId _attribute = AttributeId.Gravity;
        [SerializeField] private RelationId  _relation  = RelationId.Revert;

        private static readonly RelationId[] AllRelations =
        {
            RelationId.Revert,
            RelationId.Nullify,
            RelationId.Constant,
            RelationId.Enhance,
            RelationId.Weaken,
        };

        /// <summary>
        /// 重力每个关系应有的值。
        /// </summary>
        /// <remarks>
        /// ⚠️ 这些值和 <c>GravityRule</c> 里的常量必须一致。
        /// 改算法就要同步改这里，否则测试会误报失败。
        /// </remarks>
        private static readonly (RelationId Relation, Vector2 Expected)[] GravityExpectations =
        {
            (RelationId.Revert,   new Vector2(0f,   9.81f)),
            (RelationId.Nullify,  new Vector2(0f,   0f)),
            (RelationId.Constant, new Vector2(0f,  -9.81f)),
            (RelationId.Enhance,  new Vector2(0f, -19.62f)),
            (RelationId.Weaken,   new Vector2(0f,  -2.943f)),
        };

        private const float Tolerance = 0.01f;

        private string _lastMessage = "";
        private readonly List<string> _testLog = new List<string>();

        private void Start()
        {
            Debug.Log("[测试] RuleSystemTestInput 已启动，屏幕上应该出现测试面板。");
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, 10f, 360f, 620f), GUI.skin.box);
            GUILayout.Label("=== 规则系统测试面板 ===");

            var sys = RuleSystem.Instance;
            if (sys == null)
            {
                GUILayout.Label("✗ RuleSystem 未初始化");
                GUILayout.Label("场景里有没有挂 RuleSystemDriver？");
                GUILayout.Label("它的 Catalog 字段是不是空的？");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label($"属性：{_attribute}");

            GUILayout.Space(6f);
            GUILayout.Label("选择关系：");
            foreach (var rel in AllRelations)
            {
                bool selected = rel == _relation;
                if (GUILayout.Button((selected ? "▶ " : "   ") + rel))
                    _relation = rel;
            }

            GUILayout.Space(10f);
            if (GUILayout.Button("请求规则生效"))
            {
                var result = sys.Request(_attribute, _relation, RuleSource.Player);
                _lastMessage = result.Succeeded
                    ? $"✓ {_attribute} + {_relation} 生效"
                    : $"✗ {_attribute} + {_relation} 失败：{result.Status}";
                Debug.Log("[测试] " + _lastMessage);
            }

            if (GUILayout.Button("清空所有规则"))
            {
                sys.ClearAll();
                _lastMessage = "已清空所有规则";
                Debug.Log("[测试] " + _lastMessage);
            }

            GUILayout.Space(10f);
            if (GUILayout.Button("★ 一键跑完 5 个关系"))
                RunAllGravityTests(sys);

            if (!string.IsNullOrEmpty(_lastMessage))
            {
                GUILayout.Space(8f);
                GUILayout.Label("上次结果：" + _lastMessage);
            }

            GUILayout.Space(10f);
            GUILayout.Label("--- 当前生效的规则 ---");
            int count = 0;
            foreach (var pair in sys.Table.Slots)
            {
                count++;
                float remain = pair.Value.Remaining;
                string time = remain < 0f ? "永久" : $"{remain:F1}s";
                GUILayout.Label($"  {pair.Key} = {pair.Value.Relation}  ({time})");
            }
            if (count == 0) GUILayout.Label("  （无）");

            GUILayout.Space(6f);
            GUILayout.Label($"重力当前值：{Physics2D.gravity}");

            if (_testLog.Count > 0)
            {
                GUILayout.Space(10f);
                GUILayout.Label("--- 自动测试结果 ---");
                foreach (var line in _testLog) GUILayout.Label(line);
            }

            GUILayout.EndArea();
        }

        // ==================================================================
        //  自动测试
        // ==================================================================

        /// <summary>
        /// 依次测试重力的 5 个关系，验证两件事：
        /// ① 请求后重力等于期望值
        /// ② 清空后重力恢复默认值
        /// </summary>
        private void RunAllGravityTests(RuleSystem sys)
        {
            _testLog.Clear();

            // 基线：清空后的重力（应当是默认值）
            sys.ClearAll();
            var baseline = Physics2D.gravity;
            _testLog.Add($"基线重力：{baseline}");

            int passed = 0;

            foreach (var (relation, expected) in GravityExpectations)
            {
                var sb = new StringBuilder();

                // ① 请求生效
                sys.ClearAll();
                var result = sys.Request(AttributeId.Gravity, relation, RuleSource.Player);

                if (!result.Succeeded)
                {
                    _testLog.Add($"✗ {relation}  请求失败：{result.Status}");
                    continue;
                }

                var actual = Physics2D.gravity;
                bool applied = Vector2.Distance(actual, expected) < Tolerance;

                // ② 清空后应恢复
                sys.ClearAll();
                bool restored = Vector2.Distance(Physics2D.gravity, baseline) < Tolerance;

                if (applied && restored)
                {
                    passed++;
                    _testLog.Add($"✓ {relation}  生效={actual}  已恢复");
                }
                else if (!applied)
                {
                    _testLog.Add($"✗ {relation}  期望 {expected}，实际 {actual}");
                }
                else
                {
                    _testLog.Add($"✗ {relation}  生效正确但清空后未恢复（{Physics2D.gravity}）");
                }
            }

            sys.ClearAll();
            _lastMessage = $"自动测试：{passed}/{GravityExpectations.Length} 通过";
            Debug.Log("[测试] " + _lastMessage);
        }
    }
}
#endif
