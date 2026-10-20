using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 输入：把输入资源翻译成「一帧的意图」，并管输入门禁。只读键，不懂物理。
    /// </summary>
    /// <remarks>
    /// <para><b>两个时间尺度</b>：<see cref="Tick"/> 跑在视觉帧（<c>Update</c>），
    /// <see cref="Sample"/> 跑在物理帧（<c>FixedUpdate</c>）。一个渲染帧里可能没有物理帧、
    /// 也可能有多个，所以四个边沿标记是**粘滞**的：按下就置位，直到 <see cref="Sample"/> 取走。
    /// 不粘滞的话，渲染帧率高于物理帧率时按键会被整帧吃掉。</para>
    /// <para><see cref="Move"/> 是**保持值**，不是边沿 —— 按住多久都算，随 <see cref="Tick"/> 刷新，
    /// 不被 <see cref="Sample"/> 清掉。</para>
    /// <para><b>门禁</b>：选择器打开时接管输入，但**不暂停物理**。释放后的那一帧仍要吞掉按键，
    /// 否则同一次确认键会既关掉界面又挥出一刀。</para>
    /// </remarks>
    public sealed class PlayerInput
    {
        private readonly InputAction _moveAction;
        private readonly InputAction _jumpAction;
        private readonly InputAction _attackAction;
        private readonly InputAction _dashAction;

        private Vector2 _move;
        private bool    _jumpPressed;
        private bool    _jumpReleased;
        private bool    _dashPressed;
        private bool    _attackPressed;
        private bool    _rewindPressed;
        private bool    _throwPressed;

        private int _blockCount;
        private int _resumeFrame;

        public PlayerInput(InputActionAsset actions)
        {
            if (actions == null) return;

            _moveAction   = actions.FindAction("Player/Move",   false);
            _jumpAction   = actions.FindAction("Player/Jump",   false);
            _attackAction = actions.FindAction("Player/Attack", false);

            // ⚠️ 冲刺主键位是**鼠标右键**（在 Tick 里直接读 Mouse.current，不走输入资源）。
            //    这里保留 Player/Crouch（C 键 / 手柄 buttonEast）作为**手柄与键盘的后备** ——
            //    它本来就没人用，保留它不必动团队共享的 .inputactions，手柄也还有得用。
            //    等输入方案定稿，把它重命名成 Dash 再改这一行即可（详见 Doc/玩家动作设计.md §3.6）。
            _dashAction   = actions.FindAction("Player/Crouch", false);
        }

        /// <summary>四个动作是否都找到了。没找到时 <see cref="Move"/> 恒为零。</summary>
        public bool IsReady => _moveAction != null;

        /// <summary>当前按住的方向（**保持值**，未经摩擦反转换算）。</summary>
        public Vector2 Move => _move;

        /// <summary>门禁正在生效，或刚释放、恢复帧还没过。</summary>
        public bool IsSuppressed => _blockCount > 0 || Time.frameCount < _resumeFrame;

        /// <summary>
        /// 门禁<b>确实</b>被谁拿着（不含释放后的「恢复帧」）。
        /// </summary>
        /// <remarks>
        /// <para>给「能不能在别人的模态上面再开一层」用：过场 / 传送 / 对话期间必须拒绝，
        /// 但轮盘自己关掉的那一帧就应该能马上重新打开（那一帧 <see cref="IsSuppressed"/> 仍为 true）。</para>
        /// </remarks>
        public bool IsBlocked => _blockCount > 0;

        public void Enable()
        {
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _attackAction?.Enable();
            _dashAction?.Enable();
        }

        public void Disable()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _attackAction?.Disable();
            _dashAction?.Disable();
        }

        /// <summary>
        /// 本帧是否有<b>新的</b>玩法输入（按下边沿，不含暂停键）。
        /// </summary>
        /// <remarks>
        /// <para>给过场 / 演出做「按任意键继续」用：它<b>不受 <see cref="IsSuppressed"/></b> 影响 ——
        /// 过场正拿着门禁，但仍然要能感知玩家想结束它。只读 <c>WasPressedThisFrame</c>，
        /// 所以触发过场的那一次攻击不会被当成「再次输入」。</para>
        /// <para>不含 ESC / 手柄 Start：那两个归暂停菜单；暂停期间由调用方自己决定要不要看输入。</para>
        /// </remarks>
        public bool AnyGameplayInputThisFrame()
        {
            if (_moveAction != null && _moveAction.WasPressedThisFrame()) return true;
            if (_jumpAction != null && _jumpAction.WasPressedThisFrame()) return true;
            if (_attackAction != null && _attackAction.WasPressedThisFrame()) return true;
            if (_dashAction != null && _dashAction.WasPressedThisFrame()) return true;

            // 冲刺主键位 / 投掷 / 回溯 / 交互是直接读设备的（与 Tick 同一套键位）。
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) return true;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.wasPressedThisFrame
                    || Keyboard.current.fKey.wasPressedThisFrame
                    || Keyboard.current.eKey.wasPressedThisFrame) return true;
            }

            if (Gamepad.current != null)
            {
                if (Gamepad.current.leftShoulder.wasPressedThisFrame
                    || Gamepad.current.rightShoulder.wasPressedThisFrame
                    || Gamepad.current.buttonNorth.wasPressedThisFrame) return true;
            }

            return false;
        }

        /// <summary>
        /// 视觉帧：读键。
        /// </summary>
        /// <param name="suppressed">暂停 / 已阵亡 / 被 UI 接管 / 恢复帧 —— 都不读键，并清空粘滞标记。</param>
        public void Tick(bool suppressed)
        {
            if (suppressed)
            {
                _move          = Vector2.zero;
                _jumpPressed   = false;
                _jumpReleased  = false;
                _dashPressed   = false;
                _attackPressed = false;
                _rewindPressed = false;
                _throwPressed  = false;
                return;
            }

            _move = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;

            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())     _jumpPressed   = true;
            if (_jumpAction != null && _jumpAction.WasReleasedThisFrame())    _jumpReleased  = true;   // 可变跳跃高度：松键那一刻削速
            // 冲刺：鼠标右键是主键位（直接读设备，不走输入资源），Player/Crouch 作手柄/键盘后备。
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) _dashPressed = true;
            if (_dashAction != null && _dashAction.WasPressedThisFrame())     _dashPressed   = true;
            if (_attackAction != null && _attackAction.WasPressedThisFrame()) _attackPressed = true;

            // 时间恒定的「定点回溯」：**独占一个键**（键盘 Q / 手柄 LB）。
            // ⚠️ 不用 R —— RuleWheelLabHUD 的 R 是「清空规则」，会撞车。
            // ⚠️ 不走 .inputactions 的 Interact —— 那个动作带 Hold 交互，
            //    按住才会触发，不适合「按一下退一步」。
            if (Keyboard.current?.qKey.wasPressedThisFrame == true
                || Gamepad.current?.leftShoulder.wasPressedThisFrame == true) _rewindPressed = true;

            // 投掷（飞刀原型）：键盘 F / 手柄 RB。
            // 与冲刺右键同一条做法：**直接读设备**，先不动团队共享的 .inputactions；
            // 键位定稿后再补成正式的 "Player/Throw" 动作（见 Doc/玩家动作设计.md）。
            if (Keyboard.current?.fKey.wasPressedThisFrame == true
                || Gamepad.current?.rightShoulder.wasPressedThisFrame == true) _throwPressed = true;
        }

        /// <summary>
        /// 物理帧：取走本帧意图。边沿标记取走即清 —— 所以**每物理帧只能调一次**。
        /// </summary>
        public State Sample()
        {
            var state = new State(_move, _jumpPressed, _jumpReleased, _dashPressed, _attackPressed,
                                  _rewindPressed, _throwPressed);

            _jumpPressed   = false;
            _jumpReleased  = false;
            _dashPressed   = false;
            _attackPressed = false;
            _rewindPressed = false;
            _throwPressed  = false;

            return state;
        }

        /// <summary>接管输入。可重入计数，配套的令牌释放后恢复。</summary>
        public IDisposable Block()
        {
            _blockCount++;
            return new Token(this);
        }

        private sealed class Token : IDisposable
        {
            private PlayerInput _owner;

            public Token(PlayerInput owner) => _owner = owner;

            public void Dispose()
            {
                if (_owner == null) return;

                _owner._blockCount = Mathf.Max(0, _owner._blockCount - 1);

                // 选择器在玩家 Update 前结束时，这一帧的攻击键仍属于 UI，不能再次挥刀。
                _owner._resumeFrame = Time.frameCount + 1;
                _owner = null;
            }
        }

        /// <summary>
        /// 本物理帧的意图。
        /// </summary>
        /// <remarks>边沿字段只在**被取走的那一帧**为真；<see cref="Move"/> 是保持值。</remarks>
        public readonly struct State
        {
            public readonly Vector2 Move;
            public readonly bool    JumpPressed;
            public readonly bool    JumpReleased;
            public readonly bool    DashPressed;
            public readonly bool    AttackPressed;

            /// <summary>时间恒定的定点回溯键（Q / LB）。窗口没开时是空操作。</summary>
            public readonly bool    RewindPressed;

            /// <summary>投掷键（F / RB）。原型：投一把飞刀。</summary>
            public readonly bool    ThrowPressed;

            public State(Vector2 move, bool jumpPressed, bool jumpReleased, bool dashPressed, bool attackPressed,
                         bool rewindPressed = false, bool throwPressed = false)
            {
                Move          = move;
                JumpPressed   = jumpPressed;
                JumpReleased  = jumpReleased;
                DashPressed   = dashPressed;
                AttackPressed = attackPressed;
                RewindPressed = rewindPressed;
                ThrowPressed  = throwPressed;
            }
        }
    }
}
