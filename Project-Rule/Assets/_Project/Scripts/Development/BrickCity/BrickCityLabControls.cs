using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using RuleGame.UI.RuleWheel;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Development.BrickCity
{
    /// <summary>只在白盒使用的输入/奖励适配器。正式关卡接 P3 输入和 P1 奖励服务即可移除此组件。</summary>
    public sealed class BrickCityLabControls : MonoBehaviour, ICityRewardReceiver
    {
        public CityInteractor Interactor;
        public EntityStatus PlayerStatus;
        public UIDocument Hud;
        public RuleWheelPresenter Wheel;
        private VisualElement _guide, _bottom;
        private bool _wheelWasOpen;
        private Label _prompt, _rewards, _area, _instruction;
        private static readonly string[] AreaNames = { "升降台", "门闩与引信", "拳套与荆棘", "弹珠室", "泡泡阶梯", "休息处", "宝箱与龙珠", "两站小火车" };
        private static readonly string[] Instructions = {
            "上下两处摇杆控制同一座升降台。站上平台再切换方向。",
            "摇杆开关门闩；红色按钮点燃炸弹，请留出距离。",
            "拳套变黄时预警，变红时会造成伤害。跳过地面的荆棘。",
            "攻击可击退弹珠。用规则改变它的重力、质量或摩擦；按 F1 离开。",
            "踩住后起跳，泡泡才会破裂。先按 F6 留影，再按 F7 还原。",
            "靠近石像按 E 补能；攻击绿色落脚点才会记录复活点，F8 回到那里。",
            "宝箱与三色龙珠仅记录演示奖励，回溯不会重复领取。",
            "站上小火车，在两站之间往返。到站事件预留正式转场接入。"
        };
        public Vector2[] Stations;
        public int HealthTokens, EnergyTokens;
        private readonly HashSet<string> _claimed = new HashSet<string>();
        private Snapshot _initial, _mark;
        private float _nextLabel;
        private int _station;
        private bool _movingStation;
        public bool Ready => _initial != null;
        private IEnumerator Start()
        {
            if (Hud != null)
            {
                var root = Hud.rootVisualElement;
                _guide = root.Q("cityGuide"); _bottom = root.Q("cityBottom");
                _prompt = root.Q<Label>("cityPrompt"); _rewards = root.Q<Label>("cityRewards");
                _area = root.Q<Label>("cityArea"); _instruction = root.Q<Label>("cityInstruction");
            }
            yield return null; // 等全部机关/主角 Start 结束，避免把尚未初始化的弹珠速度保存下来。
            _initial = SnapshotSystem.Instance?.Capture();
        }
        private void Update()
        {
            bool open = Wheel != null && Wheel.IsOpen;
            if (open != _wheelWasOpen)
            {
                _wheelWasOpen = open;
                _guide?.EnableInClassList("city-hidden", open);
                _bottom?.EnableInClassList("city-hidden", open);
            }
            var keys = Keyboard.current;
            if (keys != null && !CityMechanism.Paused)
            {
                if (keys.f1Key.wasPressedThisFrame) BrowseStation(1);
                if (keys.f2Key.wasPressedThisFrame) BrowseStation(-1);
                if (keys.eKey.wasPressedThisFrame && Interactor != null)
                    Interactor.TryInteract();
                if (keys.f5Key.wasPressedThisFrame && _initial != null)
                {
                    _claimed.Clear(); HealthTokens = EnergyTokens = 0;
                    SnapshotSystem.Instance.Restore(_initial);
                }
                if (keys.f6Key.wasPressedThisFrame) _mark = SnapshotSystem.Instance?.Capture();
                if (keys.f7Key.wasPressedThisFrame && _mark != null) SnapshotSystem.Instance?.Restore(_mark);
                // F8 = 回到最近记录的落点（落点 = 存档点，位置存在玩家身上）。
                if (keys.f8Key.wasPressedThisFrame) Player?.Respawn();
                if (keys.f9Key.wasPressedThisFrame && PlayerStatus != null) PlayerStatus.TryConsumeEnergy(30f);
            }
            if (_prompt != null && Time.unscaledTime >= _nextLabel)
            {
                _nextLabel = Time.unscaledTime + .15f;
                var nearest = Interactor != null ? Interactor.FindNearest() : null;
                int area = 0;
                if (Stations != null && Stations.Length > 0 && PlayerStatus != null)
                {
                    float distance = float.PositiveInfinity;
                    for (int i = 0; i < Stations.Length; i++)
                    {
                        float d = Mathf.Abs(PlayerStatus.transform.position.x - Stations[i].x);
                        if (d < distance) { distance = d; area = i; }
                    }
                }
                if (!_movingStation) _station = area;
                _prompt.text = nearest != null ? "E · " + nearest.Prompt : "向右探索 · F1 下一处 / F2 上一处";
                _area.text = "积木城  /  " + (area + 1).ToString("00") + "  " + AreaNames[area];
                _instruction.text = Instructions[area];
                _rewards.text = "演示龙珠  生命 " + HealthTokens + "  能量 " + EnergyTokens;
            }
        }
        private void BrowseStation(int direction)
        {
            if (_movingStation || Stations == null || Stations.Length == 0 || PlayerStatus == null) return;
            _station = (_station + direction + Stations.Length) % Stations.Length;
            StartCoroutine(MoveStation());
        }
        private IEnumerator MoveStation()
        {
            _movingStation = true;
            PlayerStatus.SetPaused(true);
            yield return new WaitForFixedUpdate(); // 由主角正常的暂停分支清理自己的速度，不反射/强写私有移动状态。
            var body = PlayerStatus.GetComponent<Rigidbody2D>();
            body.position = Stations[_station]; Physics2D.SyncTransforms();
            PlayerStatus.SetPaused(false); _movingStation = false;
        }
        private PlayerController Player => Interactor != null ? Interactor.GetComponent<PlayerController>() : null;
        public bool IsClaimed(string id) => _claimed.Contains(id);
        public bool TryGrant(string id, CityReward reward)
        {
            if (string.IsNullOrWhiteSpace(id) || _claimed.Contains(id)) return false;

            switch (reward.Kind)
            {
                case CityRewardKind.Health: HealthTokens += reward.Amount; break;
                case CityRewardKind.Energy: EnergyTokens += reward.Amount; break;
                default: return false;
            }

            _claimed.Add(id);
            Debug.Log("[积木城演示] 已领取奖励：" + reward.Kind + " +" + reward.Amount + "（" + id + "）");
            return true;
        }
    }
}
