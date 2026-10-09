using UnityEngine;
using UnityEngine.InputSystem;

namespace RuleGame.Development.P4
{
    /// <summary>
    /// P4 验证关卡用的运动学角色，不作为 P3 正式控制器。
    /// 自行积分世界重力，以扫掠限制位移；物理模拟由 P4DemoController 统一推进。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class P4DemoPawn : MonoBehaviour
    {
        [SerializeField] private P4DemoController _demo;
        [SerializeField] private Transform _spawnPoint;
        [SerializeField, Min(0f)] private float _moveSpeed = 6f;
        [SerializeField, Min(0f)] private float _jumpSpeed = 12f;
        [SerializeField, Min(0f)] private float _gravityScale = 3f;
        [SerializeField, Min(0.001f)] private float _skinWidth = 0.02f;
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];
        private readonly RaycastHit2D[] _motionHits = new RaycastHit2D[16];
        private Rigidbody2D _body;
        private BoxCollider2D _collider;
        private float _move;
        private bool _jumpRequested;
        private Vector2 _velocity;
        private Vector2 _down = Vector2.down;

        public float GravityScale => _gravityScale;
        public Vector2 Velocity => _velocity;
        public bool IsGrounded { get; private set; }

        public void Configure(P4DemoController demo, Transform spawnPoint)
        {
            _demo = demo;
            _spawnPoint = spawnPoint;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f; // 引擎不施加重力，使用下方的速度积分。
            _body.useFullKinematicContacts = true;
        }

        private void OnEnable() => _demo.OnDemoReset += ResetPawn;
        private void OnDisable() => _demo.OnDemoReset -= ResetPawn;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            _move = 0f;
            if (keyboard == null || _demo.IsWorldPaused)
            {
                _jumpRequested = false;
                return;
            }
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                _move -= 1f;
            }
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                _move += 1f;
            }
            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                RequestJump();
            }
        }

        private void FixedUpdate()
        {
            _body.simulated = !_demo.IsWorldPaused;
            if (!_body.simulated)
            {
                return;
            }
            _velocity.x = _move * _moveSpeed;
            _velocity += Physics2D.gravity * _gravityScale * Time.fixedDeltaTime;
            // 零重力时保留上一条重力方向，防止接地与跳跃方向变成零向量。
            if (Physics2D.gravity.sqrMagnitude > 0.001f)
            {
                _down = Physics2D.gravity.normalized;
            }
            var filter = new ContactFilter2D();
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            filter.useTriggers = false;
            // 沿当前重力方向检测支撑面：反转后天花板也可以作为起跳面。
            int hitCount = _collider.Cast(_down, filter, _groundHits, 0.12f);
            bool grounded = false;
            for (int index = 0; index < hitCount; index++)
            {
                grounded |= Vector2.Dot(_groundHits[index].normal, -_down) > 0.5f;
            }
            IsGrounded = grounded;
            if (_jumpRequested && grounded)
            {
                _velocity.y = -_down.y * _jumpSpeed;
            }
            _jumpRequested = false;
            Vector2 wanted = _velocity * Time.fixedDeltaTime;
            Vector2 movement = Vector2.zero;
            // 分轴扫掠，Y 查询从 X 移动后的形状位置出发，使贴墙时仍能竖直移动。
            movement.x = Sweep(new Vector2(wanted.x, 0f), movement, filter).x;
            movement.y = Sweep(new Vector2(0f, wanted.y), movement, filter).y;
            if (Mathf.Abs(movement.x - wanted.x) > 0.0001f)
            {
                _velocity.x = 0f;
            }
            if (Mathf.Abs(movement.y - wanted.y) > 0.0001f)
            {
                _velocity.y = 0f;
            }
            _body.MovePosition(_body.position + movement);
            if (_body.position.y < -8f || _body.position.y > 12f)
            {
                ResetPawn();
            }
        }

        private Vector2 Sweep(Vector2 movement, Vector2 offset, ContactFilter2D filter)
        {
            // Kinematic 的 MovePosition 不会替我们阻挡静态墙体，提前限制移动距离。
            float distance = movement.magnitude;
            if (distance < 0.00001f)
            {
                return Vector2.zero;
            }
            Vector2 direction = movement / distance;
            int count = Physics2D.BoxCast((Vector2)_collider.bounds.center + offset,
                _collider.bounds.size, _body.rotation, direction, filter, _motionHits, distance + _skinWidth);
            float allowed = distance;
            for (int index = 0; index < count; index++)
            {
                RaycastHit2D hit = _motionHits[index];
                // BoxCast 也会命中自身；只保留法线迎着移动方向的障碍。
                if (hit.collider == _collider || Vector2.Dot(hit.normal, direction) >= -0.001f)
                {
                    continue;
                }
                allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - _skinWidth));
            }
            return direction * allowed;
        }

        public void RequestJump() => _jumpRequested = true;
        /// <summary>为演示或自动验证提供输入入口，位移仍统一经过 FixedUpdate 的碰撞处理。</summary>
        public void SetMoveInput(float direction) => _move = Mathf.Clamp(direction, -1f, 1f);

        public void ResetPawn()
        {
            // 重试属于瞬移，同时更新刚体与 Transform，并清除上次移动、跳跃的状态。
            _body.simulated = true;
            _body.position = _spawnPoint.position;
            transform.position = _spawnPoint.position;
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _velocity = Vector2.zero;
            _down = Physics2D.gravity.sqrMagnitude > 0.001f ? Physics2D.gravity.normalized : Vector2.down;
            _move = 0f;
            _jumpRequested = false;
        }
    }
}
