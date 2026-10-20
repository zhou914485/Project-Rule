using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using RuleGame.Gameplay.Travel;

namespace RuleGame.UI.Train
{
    /// <summary>
    /// 火车线路面板：列表、导航、确认 / 取消。**只管表现与输入，不认识传送细节**。
    /// </summary>
    /// <remarks>
    /// <para><b>依赖方向</b>：本组件在 <c>RuleGame.UI</c>，只认 <see cref="TrainTravelService"/> 的
    /// 公开接口（<c>Opened</c> / <c>Destinations</c> / <c>Travel</c> / <c>Close</c>）——
    /// 玩法层不认识任何 <c>VisualElement</c>。</para>
    /// <para><b>为什么直接读设备</b>：面板开着时玩家的输入已经被
    /// <c>BlockGameplayInput()</c> 收走，WASD 不会再驱动角色，可以放心拿来当导航键；
    /// 与白盒里的 F1/F2 是同一套做法，不用动团队共享的 <c>.inputactions</c>。</para>
    /// <para><b>保护帧</b>：叫出面板的那一次挥砍可能与确认键同帧，前几帧统一吞掉。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrainTravelPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;
        [SerializeField] private TrainTravelService _service;

        [Tooltip("打开后吞掉输入的帧数，避免「砍一下就出发」。")]
        [SerializeField, Min(0)] private int _openGuardFrames = 3;

        [Tooltip("失败提示停留的秒数。")]
        [SerializeField, Min(0.5f)] private float _messageSeconds = 2.5f;

        private const string HiddenClass = "train-hidden";
        private const string RowClass = "train-row";
        private const string SelectedClass = "train-row-selected";
        private const string CurrentClass = "train-row-current";
        private const string LockedClass = "train-row-locked";
        private const string DefaultHint = "↑↓ 选择 · Enter / E 出发 · Esc 取消";

        private VisualElement _screen;
        private VisualElement _list;
        private Label _hint;
        private Button _close;
        private readonly List<VisualElement> _rows = new List<VisualElement>();
        private readonly List<TrainDestination> _items = new List<TrainDestination>();

        private int _index;
        private int _guard;
        private float _messageUntil;

        private bool Visible => _screen != null && !_screen.ClassListContains(HiddenClass);

        /// <summary>面板是否开着。给 ESC 仲裁用（暂停菜单要先看这一帧的 Esc 归谁）。</summary>
        public bool IsVisible => Visible;

        private void Awake()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            if (_service == null) _service = FindAnyObjectByType<TrainTravelService>();
        }

        private void OnEnable()
        {
            if (_service == null) _service = FindAnyObjectByType<TrainTravelService>();
            if (_service != null)
            {
                _service.Opened += HandleOpened;
                _service.Closed += HandleClosed;
                _service.Failed += HandleFailed;
            }

            Hide();
        }

        private void OnDisable()
        {
            if (_service != null)
            {
                _service.Opened -= HandleOpened;
                _service.Closed -= HandleClosed;
                _service.Failed -= HandleFailed;
            }
        }

        private void Start()
        {
            EnsureBound();
            Hide();
        }

        // ------------------------------------------------------------------
        //  绑定
        // ------------------------------------------------------------------

        private bool EnsureBound()
        {
            if (_screen != null) return true;
            if (_document == null || _document.rootVisualElement == null) return false;

            VisualElement root = _document.rootVisualElement;
            _screen = root.Q("train-screen");
            _list = root.Q("train-list");
            _hint = root.Q<Label>("train-hint");

            // 误触了攻击、又不想传送：给一个能点的关闭按钮（Esc / 手柄东键仍然有效）。
            _close = root.Q<Button>("train-close");
            if (_close != null) _close.clicked += Cancel;

            return _screen != null && _list != null;
        }

        // ------------------------------------------------------------------
        //  服务回调
        // ------------------------------------------------------------------

        private void HandleOpened(TrainStation station)
        {
            if (!EnsureBound()) return;

            _guard = _openGuardFrames;
            _messageUntil = 0f;
            if (_hint != null) _hint.text = DefaultHint;

            Rebuild();
            Show();

            // 站在一座没解锁的站前面打开面板：直接把原因写在提示行，别让玩家以为按键坏了。
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].Current) continue;
                if (!_items[i].Locked) break;
                if (_hint != null) _hint.text = "这座车站还没解锁。先想办法把它启动，再来叫车。";
                break;
            }
        }

        private void HandleClosed()
        {
            Hide();
        }

        private void HandleFailed(string message)
        {
            // 传送失败 = 人还在原地：把面板放回来，并把原因写在提示行上。
            if (!EnsureBound()) return;

            Rebuild();
            Show();
            if (_hint != null) _hint.text = message;
            _messageUntil = UnityEngine.Time.unscaledTime + _messageSeconds;
        }

        // ------------------------------------------------------------------
        //  列表
        // ------------------------------------------------------------------

        private void Rebuild()
        {
            if (_list == null || _service == null) return;

            _list.Clear();
            _rows.Clear();
            _items.Clear();

            IReadOnlyList<TrainDestination> source = _service.Destinations;
            for (int i = 0; i < source.Count; i++)
            {
                _items.Add(source[i]);
                _list.Add(BuildRow(i, source[i]));
            }

            // 默认选中第一个「不是当前位置」的站。
            _index = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Current) continue;
                _index = i;
                break;
            }

            RefreshSelection();
        }

        private VisualElement BuildRow(int index, TrainDestination destination)
        {
            var row = new VisualElement();
            row.AddToClassList(RowClass);
            if (destination.Current) row.AddToClassList(CurrentClass);
            if (destination.Locked) row.AddToClassList(LockedClass);

            var name = new Label(destination.Name);
            name.AddToClassList("train-name");

            var state = new Label(StateText(destination));
            state.AddToClassList("train-state");

            row.Add(name);
            row.Add(state);

            row.RegisterCallback<ClickEvent>(_ =>
            {
                _index = index;
                RefreshSelection();
                Confirm();
            });
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                _index = index;
                RefreshSelection();
            });

            _rows.Add(row);
            return row;
        }

        private static string StateText(TrainDestination destination)
        {
            if (destination.Locked) return "未解锁";
            if (destination.Current) return "当前位置";
            return destination.Loaded ? "已就位" : "需要加载";
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].EnableInClassList(SelectedClass, i == _index);
            }
        }

        private void Move(int direction)
        {
            if (_rows.Count == 0) return;

            int start = _index;
            for (int step = 0; step < _rows.Count; step++)
            {
                _index = (_index + direction + _rows.Count) % _rows.Count;
                if (!_items[_index].Current) break;
            }

            if (_index != start) RefreshSelection();
        }

        private void Confirm()
        {
            if (_service == null || _index < 0 || _index >= _items.Count) return;

            TrainDestination destination = _items[_index];
            if (destination.Locked)
            {
                if (_hint != null) _hint.text = "「" + destination.Name + "」还没解锁：先到现场把它启动。";
                return;
            }

            if (destination.Current)
            {
                if (_hint != null) _hint.text = "已经在这座车站了。";
                return;
            }

            Hide();
            _service.Travel(destination.Id);
        }

        private void Cancel()
        {
            Hide();
            _service?.Close();
        }

        // ------------------------------------------------------------------
        //  输入
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!Visible) return;

            if (_guard > 0)
            {
                _guard--;
                return;
            }

            if (_messageUntil > 0f && UnityEngine.Time.unscaledTime > _messageUntil && _hint != null)
            {
                _messageUntil = 0f;
                _hint.text = DefaultHint;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) Move(-1);
                if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) Move(1);
                if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
                    || keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) Confirm();
                if (keyboard.escapeKey.wasPressedThisFrame) Cancel();
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                if (gamepad.dpad.up.wasPressedThisFrame || gamepad.leftStick.up.wasPressedThisFrame) Move(-1);
                if (gamepad.dpad.down.wasPressedThisFrame || gamepad.leftStick.down.wasPressedThisFrame) Move(1);
                if (gamepad.buttonSouth.wasPressedThisFrame) Confirm();
                if (gamepad.buttonEast.wasPressedThisFrame) Cancel();
            }
        }

        // ------------------------------------------------------------------
        //  显示
        // ------------------------------------------------------------------

        private void Show()
        {
            _screen?.RemoveFromClassList(HiddenClass);
        }

        private void Hide()
        {
            _screen?.AddToClassList(HiddenClass);
        }
    }
}
