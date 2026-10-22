using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 玩家触碰激活器：玩家身体碰到检测区时，把 Inspector 里指定的物件
    /// （例如毛绒兔子 Boss 的根物体）<c>SetActive(true)</c>，并发一次事件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么用重叠查询</b>：玩家是 Kinematic + <c>Rigidbody2D.Slide</c>，
    /// Trigger 回调不可靠；所以每物理帧用 <see cref="Physics2D.OverlapBox"/> 查检测区里
    /// 有没有玩家身体（<c>PlayerLookup</c> 认人）。只认玩家：敌人 / 铁箱 / 纸飞机碰到不算。</para>
    ///
    /// <para><b>目标</b>：场景里预先摆好、**根物体关掉**的物件（与 <c>BossEncounter</c>
    /// 的「预摆 inactive」同一条纪律）。留空只发事件，不激活任何东西。</para>
    ///
    /// <para><b>触发一次</b>：默认只触发一次（<see cref="HasTriggered"/> 置位）；暂停时冻结。
    /// 想调试：Inspector 的「立即触发」按钮，或右键菜单。</para>
    ///
    /// <para><b>检测区</b>：优先用「检测区」Collider 的包围盒；留空自动取同物体上的 Collider2D；
    /// 再没有就用 Inspector 里的 Fallback Size / Offset。生成时放一个 1×1 的 Trigger 即可。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerTouchActivator : MonoBehaviour
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("目标")]
        [Tooltip("玩家碰到后 SetActive(true) 的物件。从场景里拖进来（毛绒兔子 Boss 的根物体），" +
                 "并保持它开局是关掉的。留空 = 只发事件。")]
        [SerializeField] private GameObject _objectToActivate = null;

        [Header("检测区")]
        [Tooltip("检测区：按它的包围盒查玩家。留空 = 自动取同物体上的 Collider2D；" +
                 "再没有就用下面的 Fallback Size / Offset。")]
        [SerializeField] private Collider2D _area;

        [Tooltip("没有 Collider 时使用的检测区尺寸（世界单位；根缩放会乘进去）。")]
        [SerializeField] private Vector2 _fallbackSize = Vector2.one;

        [Tooltip("没有 Collider 时检测区相对本物体的偏移。")]
        [SerializeField] private Vector2 _fallbackOffset = Vector2.zero;

        [Header("触发")]
        [Tooltip("只触发一次。关掉 = 玩家每次进入检测区都会发事件（目标已经激活时不重复激活）。")]
        [SerializeField] private bool _oneShot = true;

        [Header("事件")]
        [Tooltip("触发时调用。目标激活之后再发，监听者可以直接操作目标。")]
        [SerializeField] private UnityEvent _onTriggered = new UnityEvent();

        [Header("调试")]
        [Tooltip("触发时打一行 Log。")]
        [SerializeField] private bool _logState = false;

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>玩家碰到、目标激活之后触发。参数无；监听者可以播特效 / 音效 / 运镜。</summary>
        public event Action OnTriggered;

        /// <summary>Instantiate 在 Inspector 里挂的事件。</summary>
        public UnityEvent TriggeredEvent => _onTriggered;

        // ==================================================================
        //  运行时
        // ==================================================================

        /// <summary>查玩家的缓存。</summary>
        private readonly Collider2D[] _hits = new Collider2D[8];

        /// <summary>上一帧检测区里有没有玩家：只在「进入」那一帧发事件。</summary>
        private bool _playerInside;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>已经触发过了（一次性模式下不再检测）。</summary>
        public bool HasTriggered { get; private set; }

        /// <summary>要激活的物件（可能是 <c>null</c>）。</summary>
        public GameObject Target => _objectToActivate;

        /// <summary>有没有拖目标进来。</summary>
        public bool HasTarget => _objectToActivate != null;

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
            if (_oneShot && HasTriggered) return;

            // 暂停：玩家不动，保持现状。
            if (TimeManager.Instance?.IsPaused == true) return;

            bool inside = TryFindPlayer();
            if (inside && !_playerInside) Trigger();

            _playerInside = inside;
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
        /// 激活目标并发事件。一次性模式下只触发一次；编辑模式只打 Log。
        /// </summary>
        [ContextMenu("调试/立即触发（Play）")]
        public void Trigger()
        {
            if (_oneShot && HasTriggered) return;

            if (!Application.isPlaying)
            {
                Debug.Log("[PlayerTouchActivator] 编辑模式不会触发；进 Play 后玩家碰到才会激活。", this);
                return;
            }

            if (!isActiveAndEnabled) return;

            HasTriggered = true;

            if (_objectToActivate != null && !_objectToActivate.activeSelf)
            {
                _objectToActivate.SetActive(true);
            }

            if (_logState)
            {
                string targetName = _objectToActivate != null ? _objectToActivate.name : "（无）";
                Debug.Log($"[PlayerTouchActivator] 玩家碰到：激活目标「{targetName}」。", this);
            }

            _onTriggered.Invoke();
            OnTriggered?.Invoke();
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

            if (_objectToActivate == null) return;

            Gizmos.color = new Color(0.95f, 0.35f, 0.95f, 0.9f);
            Gizmos.DrawLine(transform.position, _objectToActivate.transform.position);
            Gizmos.DrawWireSphere(_objectToActivate.transform.position, 0.15f);
        }
    }
}
