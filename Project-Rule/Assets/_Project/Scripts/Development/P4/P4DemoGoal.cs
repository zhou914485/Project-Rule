using UnityEngine;

namespace RuleGame.Development.P4
{
    /// <summary>白盒出口的临时触发器；正式接入时由 P1 的关卡流程组件替换。</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class P4DemoGoal : MonoBehaviour
    {
        [SerializeField] private P4DemoController _demo;
        public void Configure(P4DemoController demo) => _demo = demo;

        private void OnTriggerEnter2D(Collider2D other)
        {
            // 仅演示角色能完成路线，普通物件进入出口不会触发通关。
            if (other.TryGetComponent(out P4DemoPawn pawn))
            {
                _demo.NotifyGoalReached();
            }
        }
    }
}
