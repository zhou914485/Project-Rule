using System.Collections.Generic;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene.DemonCastle;
using RuleGame.Infrastructure.Time;
using RuleGame.UI.RuleWheel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RuleGame.Development.Enemy
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MinionGalleryControls : MonoBehaviour
    {
        public PlayerController Player;
        public EnemyActor[] Enemies;
        public DemonFirework Firework;
        public Vector2[] Stations;
        public bool Invincible;
        public int StationIndex { get; private set; }
        private static readonly string[] Names = { "石头人", "自爆方块", "发条鼠", "毛绒鸟", "蝙蝠", "抱抱熊", "毛绒海豹", "烟花机关" };
        private static readonly string[] Hints =
        {
            "既有石头人美术保持不变，靠近观察追击与锤击。",
            "接近或攻击触发引信，检查变红警告与爆炸；F3 重开可重复测试。",
            "靠近触发冒出；本批新增独立钻入与死亡动作。",
            "沿用巡飞、保持距离与吐光球行为，换接交付帧。",
            "沿用已有蝙蝠召唤物的追击与接触伤害。",
            "原有坐下、站起、追击与拥抱行为；拥抱不消散，死亡再完整消散。",
            "原有游走、滑行、嚎叫、拍肚子与死亡；冰块改用交付的双帧原画。",
            "踩左侧按钮点火；观察完整烟花外观、引信、爆炸和回溯。"
        };
        private EntityStatus _status;
        private RuleWheelPresenter _wheel;
        private VisualElement _panel;
        private DropdownField _chooser;
        private Label _hint, _enemy, _player;
        private float _nextRefresh;

        private void Start()
        {
            _status = Player.GetComponent<EntityStatus>();
            _wheel = FindAnyObjectByType<RuleWheelPresenter>();
            var root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            _panel = root.Q("galleryPanel"); _chooser = root.Q<DropdownField>("stationChooser");
            _hint = root.Q<Label>("stationHint"); _enemy = root.Q<Label>("enemyState"); _player = root.Q<Label>("playerState");
            _chooser.choices = new List<string>(Names);
            _chooser.RegisterValueChangedCallback(change => SelectStation(_chooser.index));
            root.Q<Button>("previousStation").clicked += () => SelectStation((StationIndex + Stations.Length - 1) % Stations.Length);
            root.Q<Button>("nextStation").clicked += () => SelectStation((StationIndex + 1) % Stations.Length);
            root.Q<Button>("restartGallery").clicked += Restart;
            SetInvincible(Invincible); Refresh();
        }
        private void Update()
        {
            bool selecting = _wheel != null && _wheel.IsOpen;
            if (_panel != null) _panel.style.display = selecting ? DisplayStyle.None : DisplayStyle.Flex;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + .1f;
                float distance = float.PositiveInfinity;
                for (int i = 0; i < Stations.Length; i++)
                {
                    float next = Mathf.Abs(Player.transform.position.x - Stations[i].x);
                    if (next < distance) { distance = next; StationIndex = i; }
                }
                Refresh();
            }
            var keys = Keyboard.current;
            if (keys == null || selecting) return;
            if (keys.f1Key.wasPressedThisFrame) SelectStation((StationIndex + 1) % Stations.Length);
            if (keys.f2Key.wasPressedThisFrame) SelectStation((StationIndex + Stations.Length - 1) % Stations.Length);
            if (keys.f3Key.wasPressedThisFrame) Restart();
            if (keys.f4Key.wasPressedThisFrame) SetPaused(TimeManager.Instance?.IsPaused != true);
            if (keys.f5Key.wasPressedThisFrame) SetInvincible(!Invincible);
        }
        public void SelectStation(int index)
        {
            if (TimeManager.Instance?.IsPaused == true || (_wheel != null && _wheel.IsOpen)) return;
            StationIndex = Mathf.Clamp(index, 0, Stations.Length - 1);
            Player.TeleportTo(Stations[StationIndex]);
            Player.SetRespawn("minion-gallery-" + StationIndex, Stations[StationIndex]);
            Refresh();
        }
        public void SetPaused(bool paused)
        {
            TimeManager.Instance?.SetPaused(paused);
            _status?.SetPaused(paused); Refresh();
        }
        public void SetInvincible(bool value)
        {
            Invincible = value;
            Player.GetComponent<EntityDamageable>().SetInvulnerable(value); Refresh();
        }
        private void Refresh()
        {
            if (_chooser == null || _status == null) return;
            _chooser.SetValueWithoutNotify(Names[StationIndex]); _hint.text = Hints[StationIndex];
            var actor = StationIndex < Enemies.Length ? Enemies[StationIndex] : null;
            _enemy.text = actor != null ? $"{actor.Brain?.StateName ?? "初始化"} · 生命 {actor.Status.Health:0}/{actor.Definition.Health:0}"
                : Firework.Spent ? "烟花已消耗，F3 重开" : Firework.Armed ? $"引信剩余 {Firework.Remaining:0.0} 秒" : "烟花等待按钮点火";
            _player.text = $"玩家生命 {_status.Health:0}/{_status.HealthMax:0} · 无敌{(Invincible ? "开" : "关")} · {(TimeManager.Instance?.IsPaused == true ? "已暂停" : "运行中")}";
        }
        public void Restart()
        {
#if UNITY_EDITOR
            string path = gameObject.scene.path;
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path,
                new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#endif
        }
    }
}
