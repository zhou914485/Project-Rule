using System;
using System.Collections.Generic;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace RuleGame.Gameplay.Npcs
{
    /// <summary>玩家近距离交互入口，输入占用复用 PlayerController 的可重入令牌。</summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerController))]
    public sealed class NpcPlayerInteraction : MonoBehaviour
    {
        [SerializeField] private DialogueHost _host;
        [SerializeField] private NpcActor[] _npcs = Array.Empty<NpcActor>();
        [SerializeField] private UnityEvent _onAdvanceDialogue = new UnityEvent();
        private PlayerController _player;
        private EntityStatus _status;
        private InputAction _interact;
        private InputAction _cancel;
        private IDisposable _inputLock;
        public NpcActor NearbyNpc { get; private set; }
        public NpcActor ActiveNpc { get; private set; }
        public bool IsTrading => ActiveNpc != null && ActiveNpc.State == NpcState.Sell;
        public bool IsReading => _host != null && _host.IsPlaying;
        public UnityEvent AdvanceDialogue => _onAdvanceDialogue;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _status = GetComponent<EntityStatus>();
            _interact = new InputAction("NPC Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/buttonNorth");
            _cancel = new InputAction("NPC Cancel", InputActionType.Button);
            _cancel.AddBinding("<Keyboard>/escape");
            _cancel.AddBinding("<Gamepad>/buttonEast");
        }

        private void OnEnable()
        {
            _interact.Enable();
            _cancel.Enable();
            if (_host != null) _host.PlayingChanged += OnPlayingChanged;
        }

        private void OnPlayingChanged(bool playing)
        {
            if (playing)
            {
                // 守卫可能由范围自动启动，也需要占用同一个玩家门禁。
                if (ActiveNpc == null)
                {
                    foreach (NpcActor npc in _npcs)
                        if (npc != null && npc.IsSpeaking) { ActiveNpc = npc; break; }
                    if (ActiveNpc == null)
                        foreach (NpcActor npc in NpcActor.All)
                            if (npc != null && npc.IsSpeaking) { ActiveNpc = npc; break; }
                }
                AcquireInput();
            }
        }

        private void Update()
        {
            RefreshNearby();
            if (_status != null && _status.Health <= 0f)
            {
                if (IsReading) _host.StopDialogue();
                if (IsTrading) CloseTrade();
                ReleaseInput();
                return;
            }
            if (IsReading)
            {
                AcquireInput();
                if (_cancel.WasPressedThisFrame()) _host.StopDialogue();
                else if (_interact.WasPressedThisFrame()) _onAdvanceDialogue.Invoke();
                return;
            }
            if (IsTrading)
            {
                AcquireInput();
                if (_cancel.WasPressedThisFrame() || _interact.WasPressedThisFrame()) CloseTrade();
                return;
            }
            if (_inputLock != null)
            {
                ActiveNpc = null;
                ReleaseInput();
                return; // 释放当帧不把同一个 E 再交给下一轮交谈。
            }
            if (_interact.WasPressedThisFrame()) TryInteractNearest();
        }

        public void RefreshNearby()
        {
            NearbyNpc = null;
            float nearest = float.PositiveInfinity;
            foreach (NpcActor npc in _npcs) ConsiderNearby(npc, ref nearest);
            foreach (NpcActor npc in NpcActor.All) ConsiderNearby(npc, ref nearest);
        }

        private void ConsiderNearby(NpcActor npc, ref float nearest)
        {
            if (npc == null || !npc.isActiveAndEnabled || npc.Definition == null) return;
            float distance = ((Vector2)(npc.transform.position - transform.position)).sqrMagnitude;
            float radius = npc.Definition.InteractionRadius;
            if (distance > radius * radius || distance >= nearest) return;
            nearest = distance;
            NearbyNpc = npc;
        }

        public bool TryInteractNearest()
        {
            if (!isActiveAndEnabled || _host == null || IsReading || IsTrading
                || _player.IsGameplayInputSuppressed || TimeManager.Instance?.IsPaused == true
                || (_status != null && (_status.IsPaused || _status.Health <= 0f))) return false;
            RefreshNearby();
            if (NearbyNpc == null) return false;
            ActiveNpc = NearbyNpc;
            AcquireInput();
            if (ActiveNpc.TryInteract(transform)) return true;
            ActiveNpc = null;
            ReleaseInput();
            return false;
        }

        public void CloseTrade()
        {
            if (IsTrading) ActiveNpc.CloseTrade();
        }

        private void AcquireInput()
        {
            if (_inputLock == null) _inputLock = _player.BlockGameplayInput();
        }

        private void ReleaseInput()
        {
            _inputLock?.Dispose();
            _inputLock = null;
        }

        private void OnDisable()
        {
            if (_host != null)
            {
                _host.PlayingChanged -= OnPlayingChanged;
                if (IsTrading) CloseTrade();
                if (ActiveNpc != null && IsReading) _host.StopDialogue();
            }
            _interact.Disable();
            _cancel.Disable();
            ReleaseInput();
            ActiveNpc = null;
            NearbyNpc = null;
        }

        private void OnDestroy()
        {
            _interact?.Dispose();
            _cancel?.Dispose();
        }
    }
}