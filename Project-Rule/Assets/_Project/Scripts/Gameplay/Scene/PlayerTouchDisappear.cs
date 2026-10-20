using System;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 玩家触碰消失器：玩家身体碰到检测区时，**自己**和 Inspector 里指定的
    /// <see cref="_objectToDisappear"/> 一起消失。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用重叠查询</b>：玩家是 Kinematic + <c>Rigidbody2D.Slide</c>，
    /// Trigger 回调不可靠；所以每物理帧用 <see cref="Physics2D.OverlapBox"/> 查检测区里
    /// 有没有玩家身体（<c>PlayerLookup</c> 认人）。只认玩家：敌人 / 铁箱 / 纸飞机碰到不算。</para>
    ///
    /// <para><b>目标</b>：把场景里任意物件拖到「目标」——门、平台、道具、甚至另一个触发器都可以。
    /// 留空 = 只消失自己。目标和自己都会消失；若目标是自己的父物体，会连同自己一起消失。</para>
    ///
    /// <para><b>消失方式</b>：默认 <see cref="DisappearMode.Deactivate"/>（<c>SetActive(false)</c>，
    /// 场景重载可恢复）；也可切 <see cref="DisappearMode.Destroy"/> 永久销毁。</para>
    ///
    /// <para><b>触发一次</b>：<see cref="HasTriggered"/> 置位后不再检测；暂停时冻结。
    /// 想调试：Inspector 的「立即触发」按钮，或右键菜单。</para>
    ///
    /// <para><b>检测区</b>：优先用「检测区」Collider 的包围盒；留空自动取同物体上的 Collider2D；
    /// 再没有就用 Inspector 里的 Fallback Size / Offset。生成工具默认放一个 1×1 的 Trigger。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑳ 玩家触碰消失（同时让目标消失）</c>；
    /// 生成后把要让其消失的物件拖进「目标」。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerTouchDisappear : MonoBehaviour
    {
        /// <summary>消失方式。</summary>
        public enum DisappearMode
        {
            /// <summary><c>SetActive(false)</c>：可恢复（场景重载 / 手动启用）。</summary>
            Deactivate = 0,

            /// <summary><c>Destroy</c>：永久销毁。</summary>
            Destroy = 1,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("目标")]
        [Tooltip("玩家碰到后一起消失的物件。从场景里拖进来；留空 = 只消失自己。")]
        [SerializeField] private GameObject _objectToDisappear = null;

        [Header("检测区")]
        [Tooltip("检测区：按它的包围盒查玩家。留空 = 自动取同物体上的 Collider2D；" +
                 "再没有就用下面的 Fallback Size / Offset。")]
        [SerializeField] private Collider2D _area;

        [Tooltip("没有 Collider 时使用的检测区尺寸（世界单位；根缩放会乘进去）。")]
        [SerializeField] private Vector2 _fallbackSize = Vector2.one;

        [Tooltip("没有 Collider 时检测区相对本物体的偏移。")]
        [SerializeField] private Vector2 _fallbackOffset = Vector2.zero;

        [Header("消失方式")]
        [Tooltip("Deactivate = SetActive(false)，可恢复；Destroy = 永久销毁。")]
        [SerializeField] private DisappearMode _mode = DisappearMode.Deactivate;

        [Header("调试")]
        [Tooltip("触发时打一行 Log。")]
        [SerializeField] private bool _logState = false;

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>玩家碰到、消失之前触发。参数无；监听者可以趁机播特效 / 存档。</summary>
        public event Action OnTriggered;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>查玩家的缓存。</summary>
        private readonly Collider2D[] _hits = new Collider2D[8];

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>已经触发过了（只触发一次）。</summary>
        public bool HasTriggered { get; private set; }

        /// <summary>要一起消失的物件（可能是 <c>null</c>）。</summary>
        public GameObject Target => _objectToDisappear;

        /// <summary>有没有拖目标进来。</summary>
        public bool HasTarget => _objectToDisappear != null;

        /// <summary>当前消失方式。</summary>
        public DisappearMode Mode => _mode;

        /// <summary>状态的中文名（Inspector 显示用）。</summary>
        public string StateName => HasTriggered ? "已触发" : "等待玩家";

        /// <summary>检测区的世界包围盒。</summary>
        public Bounds DetectionBounds
        {
            get
            {
                if (_area != null) return _area.bounds;

                Vector3 scale = transform.lossyScale;
                Vector3 center = transform.TransformPoint(_fallbackOffset);
                var size = new Vector3(
                    Mathf.Abs(_fallbackSize.x * scale.x),
                    Mathf.Abs(_fallbackSize.y * scale.y),
                    0f);

                return new Bounds(center, size);
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_area == null) _area = GetComponent<Collider2D>();
        }

        private void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (HasTriggered) return;

            // 暂停：玩家不动，保持现状。
            if (TimeManager.Instance?.IsPaused == true) return;

            if (!TryFindPlayer()) return;

            Trigger();
        }

        // ==================================================================
        //  检测
        // ==================================================================

        /// <summary>
        /// 检测区里有没有**玩家身体**。
        /// </summary>
        /// <remarks>
        /// <para><c>useTriggers = false</c>：只认玩家身体，不把受击箱 / 攻击盒算成「碰到」。</para>
        /// <para>自己的子物体（白盒 / 标签）跳过。</para>
        /// </remarks>
        private bool TryFindPlayer()
        {
            Bounds bounds = DetectionBounds;
            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(
                (Vector2)bounds.center, (Vector2)bounds.size, 0f, filter, _hits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = _hits[i];
                if (hit == null) continue;

                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (PlayerLookup.IsPlayer(hit)) return true;
            }

            return false;
        }

        // ==================================================================
        //  对外入口
        // ==================================================================

        /// <summary>
        /// 让「自己 + 目标」消失。只触发一次；编辑模式只打 Log。
        /// </summary>
        [ContextMenu("调试/立即触发（Play）")]
        public void Trigger()
        {
            if (HasTriggered) return;

            if (!Application.isPlaying)
            {
                Debug.Log("[PlayerTouchDisappear] 编辑模式不会触发；进 Play 后玩家碰到才会消失。", this);
                return;
            }

            if (!isActiveAndEnabled) return;

            HasTriggered = true;

            if (_logState)
            {
                string targetName = _objectToDisappear != null ? _objectToDisappear.name : "（无）";
                Debug.Log($"[PlayerTouchDisappear] 玩家碰到：消失自己 + 目标「{targetName}」。", this);
            }

            OnTriggered?.Invoke();

            // 目标是父物体时，停用 / 销毁它会连自己一起处理；重复一次也安全。
            if (_objectToDisappear != null && _objectToDisappear != gameObject)
            {
                Disappear(_objectToDisappear);
            }

            Disappear(gameObject);
        }

        private void Disappear(GameObject go)
        {
            if (go == null) return;

            if (_mode == DisappearMode.Destroy) Destroy(go);
            else go.SetActive(false);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        private void OnValidate()
        {
            _fallbackSize.x = Mathf.Max(0.01f, _fallbackSize.x);
            _fallbackSize.y = Mathf.Max(0.01f, _fallbackSize.y);

            if (_area == null) _area = GetComponent<Collider2D>();
        }

        private void OnDrawGizmosSelected()
        {
            Bounds bounds = DetectionBounds;
            Gizmos.color = HasTriggered
                ? new Color(0.35f, 0.95f, 0.45f, 0.9f)
                : new Color(1f, 0.8f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            if (_objectToDisappear == null) return;

            Gizmos.color = new Color(0.95f, 0.35f, 0.95f, 0.9f);
            Gizmos.DrawLine(transform.position, _objectToDisappear.transform.position);
            Gizmos.DrawWireSphere(_objectToDisappear.transform.position, 0.15f);
        }
    }
}
