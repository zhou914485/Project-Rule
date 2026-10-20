using UnityEngine;
using RuleGame.Core.Rules;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>
    /// 有界平台图：从真实地形采样站立点，A* 连接行走、扫掠验证的跳跃/落差。
    /// 仅输出 Motor 意图；不写 Transform、不移动刚体、不改变规则或攻击状态。
    /// </summary>
    public sealed class EnemyPlatformNavigation
    {
        private const int Capacity = 192;
        private const int QueryBudget = 2400;
        private const float Skin = .035f;
        private const float ArcStep = .04f;
        private readonly EnemyActor _actor;
        private readonly Vector2[] _nodes = new Vector2[Capacity];
        private readonly float[] _cost = new float[Capacity];
        private readonly int[] _parents = new int[Capacity];
        private readonly bool[] _closed = new bool[Capacity];
        private readonly float[] _jumps = new float[Capacity];
        private readonly bool[] _drops = new bool[Capacity];
        private readonly int[] _reverse = new int[Capacity];
        private readonly Step[] _path = new Step[Capacity];
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
        private readonly RaycastHit2D[] _sweeps = new RaycastHit2D[8];
        private readonly Collider2D[] _overlaps = new Collider2D[4];
        private ContactFilter2D _filter;
        private Vector2 _size, _up, _landing, _limits, _home;
        private float _repathAt, _launchedAt, _heading, _lastMoveAt, _blockedFor;
        private Vector2 _lastPosition;
        private bool _traversing, _leftGround;
        private int _count, _pathCount, _pathIndex, _queries;
        private EnemyDefinition Config => _actor.Definition;
        private EnemyMotor Motor => _actor.Motor;
        private Vector2 Position => _actor.transform.position;
        private float MaxJump => Config.Kind == EnemyKind.SpringToy
            ? Mathf.Max(Config.MinionHopHeight, Config.MinionHighHopHeight) : Config.MinionJumpHeight;
        private float Speed => Config.MoveSpeed * Motor.SpeedMultiplier * Motor.GroundSpeedScale;
        private float Acceleration => Motor.Grounded ? Motor.GroundAcceleration : 35f * Motor.MassResponse;
        private float AirSpeed => Config.MoveSpeed * Motor.SpeedMultiplier;
        public bool JumpStarted { get; private set; }
        public bool HasRoute => _pathIndex < _pathCount || _traversing;
        public void DrawGizmos()
        {
            Vector2 from = Position;
            if (_traversing)
            {
                Gizmos.color = Color.yellow; Gizmos.DrawLine(from, _landing); Gizmos.DrawWireSphere(_landing, .12f);
                return;
            }
            for (int i = _pathIndex; i < _pathCount; i++)
            {
                Step step = _path[i];
                Gizmos.color = step.Jump > 0f ? Color.yellow : step.Drop ? Color.cyan : Color.green;
                Gizmos.DrawLine(from, step.Position); Gizmos.DrawWireSphere(step.Position, .1f); from = step.Position;
            }
        }

        private struct Step
        {
            public Vector2 Position;
            public float Jump;
            public bool Drop;
        }

        public EnemyPlatformNavigation(EnemyActor actor) { _actor = actor; }
        public void Repath()
        {
            _pathCount = _pathIndex = 0; _repathAt = 0f; _blockedFor = 0f;
        }

        public void Cancel()
        {
            Repath();
            _traversing = false;
            _leftGround = false;
            _blockedFor = 0f;
            _repathAt = 0f;
            _queries = 0;
            Motor.AllowLedgeTraversal = false;
        }

        /// <summary>起跳后锁定落脚点，目标丢失也先完成本跳；不在空中重新搜索。</summary>
        public bool ContinueTraversal(float age)
        {
            if (!_traversing) return false;
            // 摩擦反转时不做跳跃寻路：抛物线和落点都是**世界坐标**算出来的，
            // 而 Motor 会把提交的意图翻掉 —— 跳出去只会落在反方向，还会把
            // AllowLedgeTraversal 锁住直到 4 秒超时（读起来就是「卡住不动」）。
            // 直接取消，让调用方落到 Direct 的兜底走法上。
            if (RuleSystem.Instance?.Attributes.Values.FrictionInverted == true)
            {
                Cancel(); Motor.Intent = Vector2.zero; return false;
            }
            if (Vector2.Dot(_up, CurrentUp()) < .99f || age - _launchedAt > 4f)
            {
                Cancel(); Motor.Intent = Vector2.zero; return false;
            }
            if (!Motor.Grounded || Vector2.Dot(Motor.Velocity, _up) > .1f) _leftGround = true;
            if (_leftGround && age - _launchedAt > .08f && Motor.Grounded && Vector2.Dot(Motor.Velocity, _up) <= .1f)
            {
                _traversing = false; _pathCount = 0; _repathAt = 0f;
                return false;
            }
            float remaining = (_landing.x - Position.x) * _heading;
            float brake = Motor.Velocity.x * Motor.Velocity.x / (2f * Mathf.Max(1f, Acceleration)) + .04f;
            Motor.Intent = Vector2.right * (remaining > brake ? _heading : 0f);
            Motor.AllowLedgeTraversal = true;
            _actor.FaceDirection(Vector2.right * _heading);
            return true;
        }

        public void MoveTo(Vector2 destination, Vector2 home, float age, bool hop = false, float hopHeight = -1f)
        {
            JumpStarted = false;
            if (ContinueTraversal(age)) return;
            _queries = 0;
            if (!Config.MinionNavigationEnabled || _actor.IsFlying) { Motor.Intent = Vector2.zero; return; }
            _up = CurrentUp();
            _home = home;
            // Motor 的自主移动沿世界 X；横向重力/失重时不虚构能到达的平台弧线。
            if (Mathf.Abs(_up.y) < .99f || Physics2D.gravity.sqrMagnitude < .001f)
            {
                Cancel(); Motor.Intent = Vector2.zero; return;
            }
            // 摩擦反转：「按右往左走」是 Motor 的事 —— 它会把这里提交的意图再翻一次，
            // 而导航算的是**世界坐标**路线，翻过之后整条路线就反了（照着走会走反）。
            // 所以这时候不寻路，退回「直接朝目标给意图」：实体照常走、只是走反。
            // ⚠️ 这里**不能**把意图清零 —— 清零 = 实体原地冻住（改之前的表现）。
            if (RuleSystem.Instance?.Attributes.Values.FrictionInverted == true)
            {
                Cancel(); Direct(destination); return;
            }
            _filter = new ContactFilter2D { useTriggers = false };
            _filter.SetLayerMask(Motor.SolidMask);
            _size = new Vector2(Mathf.Max(.05f, Config.BodySize.x - .04f), Mathf.Max(.05f, Config.BodySize.y - .04f));
            float margin = Config.BodySize.x * .5f + Skin;
            _limits = new Vector2(Mathf.Max(home.x - Config.MinionLeashRange, _actor.ArenaX.x + margin),
                Mathf.Min(home.x + Config.MinionLeashRange, _actor.ArenaX.y - margin));
            if (_limits.x >= _limits.y) { Motor.Intent = Vector2.zero; return; }
            if (!Motor.Grounded && !Supported(Position)) { Motor.Intent = Vector2.zero; return; }
            if (age >= _repathAt)
            {
                _queries = 0;
                Build(destination);
                _repathAt = age + Mathf.Max(.2f, Config.MinionRepathInterval);
            }
            if (_pathIndex >= _pathCount) { Motor.Intent = Vector2.zero; return; }
            Step next = _path[_pathIndex];
            float offset = next.Position.x - Position.x;
            if (Mathf.Abs(offset) <= .12f && Mathf.Abs(next.Position.y - Position.y) <= .25f)
            {
                _pathIndex++;
                if (_pathIndex >= _pathCount) { Motor.Intent = Vector2.zero; return; }
                next = _path[_pathIndex]; offset = next.Position.x - Position.x;
            }
            if (next.Jump > 0f || next.Drop || hop)
            {
                if (!Motor.Grounded) { Motor.Intent = Vector2.zero; return; }
                float height = next.Jump;
                Vector2 landing = next.Position;
                if (hop && height == 0f && !next.Drop)
                {
                    height = Mathf.Max(.1f, hopHeight);
                    float g = Physics2D.gravity.magnitude * Config.GravityScale;
                    float reach = AirSpeed * 2f * Motor.JumpSpeed(height) / Mathf.Max(.01f, g) * .7f;
                    landing.x = Position.x + Mathf.Sign(offset) * Mathf.Min(Mathf.Abs(offset), reach);
                }
                _queries = 0;
                // 重算间隔内出现新障碍，也不能沿过期弧线起跳。
                if (!ArcClear(Position, landing, height))
                {
                    _pathCount = 0; Motor.Intent = Vector2.zero; return;
                }
                _landing = landing; _heading = Mathf.Sign(landing.x - Position.x);
                _launchedAt = age; _traversing = true; _leftGround = false;
                if (height > 0f) { Motor.Jump(height); JumpStarted = true; }
                ContinueTraversal(age);
                return;
            }
            float dt = Mathf.Clamp(age - _lastMoveAt, 0f, .1f);
            _blockedFor = (Position - _lastPosition).sqrMagnitude < .0001f ? _blockedFor + dt : 0f;
            _lastPosition = Position; _lastMoveAt = age;
            if (_blockedFor > .6f) { _pathCount = 0; _blockedFor = 0f; }
            // 行走也按真实身体宽度检查下一步支撑，既允许从落地的边缘挪上平台，也不沿过期路线走空。
            _queries = 0;
            if (!Supported(Position + Vector2.right * Mathf.Sign(offset) * Mathf.Max(.15f, Speed * .02f + .05f)))
            {
                _pathCount = 0; Motor.Intent = Vector2.zero; return;
            }
            Motor.AllowLedgeTraversal = true;
            Motor.Intent = Vector2.right * Mathf.Sign(offset);
            _actor.FaceDirection(Motor.Intent);
        }

        /// <summary>
        /// 不做寻路时的兜底走法：朝目标方向直接给意图，撞到墙 / 到边缘就停。
        /// </summary>
        /// <remarks>
        /// 挡路检查走 <see cref="EnemyMotor.HasHorizontalObstacle"/> —— 它内部会把意图换算成
        /// **世界**方向，所以摩擦反转时看的是实体实际要去的哪一侧。
        /// </remarks>
        private void Direct(Vector2 destination)
        {
            float offset = destination.x - Position.x;
            if (Mathf.Abs(offset) <= .12f) { Motor.Intent = Vector2.zero; return; }

            float intent = Mathf.Sign(offset);
            // ⚠️ 用 HasWallAhead（不含悬崖检测）：反转期间实体是被**推着**走的，
            //    必须允许它被逼下坑 —— 带悬崖检测的版本会让它在坑边刹住，坑永远进不去。
            Motor.Intent = Motor.HasWallAhead(intent) ? Vector2.zero : Vector2.right * intent;
            // 反转期间实体是被**推着**走的：允许踏空，否则 Motor 自己的悬崖保护会在坑边刹死它
            // （玩家就没法把它逼进坑里了）。
            Motor.AllowLedgeTraversal = true;
            _actor.FaceDirection(Motor.Intent);
        }

        private static Vector2 CurrentUp() => Physics2D.gravity.sqrMagnitude > .001f ? -Physics2D.gravity.normalized : Vector2.up;
        private bool InBounds(Vector2 point) => point.x >= _limits.x && point.x <= _limits.y
            && Vector2.Distance(point, _home) <= Config.MinionLeashRange
            && point.y >= _actor.ArenaY.x + Config.BodySize.y * .5f - Skin * 2f
            && point.y <= _actor.ArenaY.y - Config.BodySize.y * .5f + Skin * 2f;
        private bool BodyClear(Vector2 point)
        {
            if (++_queries > QueryBudget) return false;
            return Physics2D.OverlapBox(point, _size, 0f, _filter, _overlaps) == 0;
        }
        private bool Supported(Vector2 point)
        {
            if (++_queries > QueryBudget) return false;
            // 与 Motor 的身体接地保持一致，不能把中心稍微悬在平台边缘的合法落地误当成无路。
            int count = Physics2D.BoxCast(point, _size, 0f, -_up, _filter, _sweeps, .2f);
            for (int i = 0; i < count; i++)
                if (Vector2.Dot(_sweeps[i].normal, _up) >= .6f) return true;
            return false;
        }
        private bool Sweep(Vector2 from, Vector2 to, out RaycastHit2D hit)
        {
            hit = default;
            if (++_queries > QueryBudget) return true;
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return false;
            int count = Physics2D.BoxCast(from, _size, 0f, delta.normalized, _filter, _sweeps, delta.magnitude);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (_sweeps[i].distance < nearest) { nearest = _sweeps[i].distance; hit = _sweeps[i]; }
            return count > 0;
        }
        private bool WalkClear(Vector2 from, Vector2 to)
        {
            if (Sweep(from, to, out _)) return false;
            int steps = Mathf.CeilToInt(Vector2.Distance(from, to) / .25f);
            for (int i = 0; i <= steps; i++)
                if (!Supported(Vector2.Lerp(from, to, i / (float)Mathf.Max(1, steps)))) return false;
            return true;
        }

        private bool ArcClear(Vector2 from, Vector2 to, float height)
        {
            float gravity = Physics2D.gravity.magnitude * Config.GravityScale;
            if (gravity < .1f || Speed <= .01f) return false;
            float vertical = height > 0f ? Motor.JumpSpeed(height) : 0f;
            float horizontal = 0f;
            float heading = Mathf.Sign(to.x - from.x);
            bool leftSupport = height > 0f;
            Vector2 position = from;
            for (int i = 0; i < 100 && _queries < QueryBudget; i++)
            {
                float acceleration = Mathf.Max(.001f, leftSupport ? 35f * Motor.MassResponse : Motor.GroundAcceleration);
                float speed = leftSupport ? AirSpeed : Speed;
                float remaining = (to.x - position.x) * heading;
                float brake = horizontal * horizontal / (2f * acceleration) + .04f;
                horizontal = Mathf.MoveTowards(horizontal, remaining > brake ? heading * speed : 0f, acceleration * ArcStep);
                if (!leftSupport && !Supported(position + Vector2.right * horizontal * ArcStep)) leftSupport = true;
                if (leftSupport) vertical -= gravity * ArcStep;
                Vector2 next = position + (Vector2.right * horizontal + _up * vertical) * ArcStep;
                if (Sweep(position, next, out RaycastHit2D hit))
                    return hit.collider != null && vertical < 0f && Vector2.Dot(hit.normal, _up) >= .6f
                        && Mathf.Abs(hit.centroid.y - to.y) <= .18f
                        && Mathf.Abs(hit.centroid.x - to.x) <= Mathf.Max(.16f, Config.MinionNavigationSpacing * .65f);
                if (!InBounds(next)) return false;
                position = next;
                if (Vector2.Dot(position - to, _up) < -.3f && vertical < 0f) return false;
            }
            return false;
        }

        private void Build(Vector2 destination)
        {
            _pathCount = _pathIndex = 0; _count = 1; _nodes[0] = Position;
            float spacing = Mathf.Max(Mathf.Clamp(Config.MinionNavigationSpacing, .3f, 1f), (_limits.y - _limits.x) / 64f);
            // 固定世界网格，避免场地边界余量使采样点每次漂移而漏掉窄小的起跳区。
            float firstColumn = Mathf.Ceil(_limits.x / spacing) * spacing;
            int columns = Mathf.Min(64, Mathf.FloorToInt((_limits.y - firstColumn) / spacing));
            float top = _up.y > 0f ? _actor.ArenaY.y : _actor.ArenaY.x;
            float depth = Mathf.Max(.1f, _actor.ArenaY.y - _actor.ArenaY.x);
            for (int column = 0; column <= columns && _count < Capacity; column++)
            {
                float x = firstColumn + column * spacing;
                if (++_queries > QueryBudget) break;
                int hits = Physics2D.Raycast(new Vector2(x, top), -_up, _filter, _hits, depth);
                for (int i = 0; i < hits && _count < Capacity; i++)
                {
                    if (Vector2.Dot(_hits[i].normal, _up) < .6f) continue;
                    Vector2 center = _hits[i].point + _up * (Config.BodySize.y * .5f + Skin);
                    if (InBounds(center) && BodyClear(center)) _nodes[_count++] = center;
                }
            }
            int goal = 0;
            for (int i = 0; i < _count; i++)
            {
                _cost[i] = float.PositiveInfinity; _parents[i] = -1; _closed[i] = false;
                if ((_nodes[i] - destination).sqrMagnitude < (_nodes[goal] - destination).sqrMagnitude) goal = i;
            }
            if (goal == 0) return;
            if (Mathf.Abs(_nodes[0].y - _nodes[goal].y) < .12f && WalkClear(_nodes[0], _nodes[goal]))
            {
                _path[0] = new Step { Position = _nodes[goal] }; _pathCount = 1; return;
            }
            _cost[0] = 0f;
            int best = 0;
            for (int iteration = 0; iteration < Capacity && _queries < QueryBudget; iteration++)
            {
                int current = -1; float score = float.PositiveInfinity;
                for (int i = 0; i < _count; i++)
                {
                    float candidate = _cost[i] + Vector2.Distance(_nodes[i], _nodes[goal]);
                    if (!_closed[i] && candidate < score) { current = i; score = candidate; }
                }
                if (current < 0) break;
                _closed[current] = true;
                if ((_nodes[current] - destination).sqrMagnitude < (_nodes[best] - destination).sqrMagnitude) best = current;
                if (current == goal) { best = current; break; }
                for (int next = 1; next < _count && _queries < QueryBudget; next++)
                {
                    if (_closed[next]) continue;
                    Vector2 delta = _nodes[next] - _nodes[current];
                    float rise = Vector2.Dot(delta, _up), jump = 0f;
                    bool drop = false;
                    float cost = delta.magnitude;
                    if (_cost[current] + cost >= _cost[next]) continue;
                    bool walkCandidate = Mathf.Abs(delta.x) <= spacing * 1.6f
                        || (Mathf.Abs(rise) < .12f && delta.x * (_nodes[goal].x - _nodes[current].x) > 0f);
                    if (walkCandidate && Mathf.Abs(rise) <= .35f && WalkClear(_nodes[current], _nodes[next])) { }
                    else
                    {
                        // 同层连续地面交给相邻行走边，避免为每对远处平地点重复模拟整条抛物线。
                        Vector2 middle = (_nodes[current] + _nodes[next]) * .5f;
                        if (Mathf.Abs(rise) < .12f && Supported(middle) && BodyClear(middle)) continue;
                        if (rise < -Config.MinionMaxDrop || rise > MaxJump - .1f || Mathf.Abs(delta.x) < .1f) continue;
                        float g = Mathf.Max(.1f, Physics2D.gravity.magnitude * Config.GravityScale);
                        float launch = Mathf.Sqrt(2f * g * MaxJump);
                        float time = (launch + Mathf.Sqrt(Mathf.Max(0f, launch * launch - 2f * g * rise))) / g;
                        if (Mathf.Abs(delta.x) > Speed * time + spacing * .65f) continue;
                        if (rise < -.3f && ArcClear(_nodes[current], _nodes[next], 0f)) drop = true;
                        else
                        {
                            if (MaxJump <= 0f) continue;
                            jump = Config.Kind == EnemyKind.SpringToy ? Config.MinionHopHeight : Mathf.Min(MaxJump, Mathf.Max(.4f, rise + .25f));
                            if (!ArcClear(_nodes[current], _nodes[next], jump))
                            {
                                jump = MaxJump;
                                if (!ArcClear(_nodes[current], _nodes[next], jump)) continue;
                            }
                        }
                        cost += .8f;
                    }
                    if (_cost[current] + cost >= _cost[next]) continue;
                    _cost[next] = _cost[current] + cost; _parents[next] = current;
                    _jumps[next] = jump; _drops[next] = drop;
                }
            }
            // 查询预算用尽时仍使用已验证的可达前沿，不能丢弃刚发现但还没弹出队列的有效路线。
            for (int i = 1; i < _count; i++)
                if (!float.IsPositiveInfinity(_cost[i]) && (_nodes[i] - destination).sqrMagnitude < (_nodes[best] - destination).sqrMagnitude) best = i;
            int length = 0;
            for (int node = best; node > 0 && length < Capacity; node = _parents[node]) _reverse[length++] = node;
            for (int i = length - 1; i >= 0; i--)
            {
                int node = _reverse[i];
                var step = new Step { Position = _nodes[node], Jump = _jumps[node], Drop = _drops[node] };
                // 同一高度连续行走段合并；保留每个跳跃/落差的起点。
                if (_pathCount > 0 && step.Jump == 0f && !step.Drop && _path[_pathCount - 1].Jump == 0f && !_path[_pathCount - 1].Drop
                    && Mathf.Abs(step.Position.y - _path[_pathCount - 1].Position.y) < .05f)
                    _path[_pathCount - 1] = step;
                else _path[_pathCount++] = step;
            }
        }

        private sealed class Data
        {
            public bool Traversing, LeftGround;
            public Vector2 Landing, Up;
            public float Heading, LaunchedAt;
        }
        public object Capture() => new Data { Traversing = _traversing, LeftGround = _leftGround, Landing = _landing, Up = _up, Heading = _heading, LaunchedAt = _launchedAt };
        public void Restore(object state)
        {
            Cancel();
            if (!(state is Data data)) return;
            _traversing = data.Traversing; _landing = data.Landing; _up = data.Up;
            _leftGround = data.LeftGround;
            _heading = data.Heading; _launchedAt = data.LaunchedAt;
            // 图和查询缓存都是派生数据；回溯后按恢复的地形重新采样。
        }
    }
}
