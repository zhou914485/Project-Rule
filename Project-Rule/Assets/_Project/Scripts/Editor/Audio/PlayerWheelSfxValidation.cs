using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Core.Rules;
using RuleGame.Development.RuleWheel;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Rules;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Audio;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Audio
{
    [InitializeOnLoad]
    public static class PlayerWheelSfxValidation
    {
        private const string Pending = "RuleGame.PlayerWheelSfxValidation";
        private const string ScenesKey = Pending + ".Scenes";
        private static readonly List<string> Results = new List<string>();
        private static readonly int[] Counts = new int[Enum.GetValues(typeof(SfxCue)).Length];
        private static PlayerController _player;
        private static PlayerSfxPresenter _playerView;
        private static EntityDamageable _damageable;
        private static EntityStatus _status;
        private static CombatSystem _combat;
        private static RuleWheelPresenter _wheel;
        private static SfxPlayer _audio;
        private static Keyboard _keyboard, _oldKeyboard;
        private static Mouse _mouse, _oldMouse;
        private static GameObject _root;
        private static SfxBank _testBank;
        private static AudioClip _longClip;
        private static AudioSource _pausedSource;
        private static int _step, _landCount, _sample;
        private static double _due, _deadline;
        private static bool _running;
        private static float _actualDamage;
        private static Snapshot _snapshot;
        [Serializable] private sealed class Setup { public SceneSetup[] Scenes; }

        static PlayerWheelSfxValidation() => EditorApplication.playModeStateChanged += OnPlayState;

        [MenuItem("Tools/规则游戏/音频/⑤ 验证玩家与轮盘音效（PlayMode）")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("请先保存当前场景。");
            SessionState.SetString(ScenesKey, JsonUtility.ToJson(new Setup { Scenes = EditorSceneManager.GetSceneManagerSetup() }));
            SessionState.SetBool(Pending, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode) Begin();
            if (change != PlayModeStateChange.EnteredEditMode) return;
            SessionState.SetBool(Pending, false);
            string json = SessionState.GetString(ScenesKey, "");
            if (!string.IsNullOrEmpty(json)) EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(json).Scenes);
        }

        private static void Begin()
        {
            Results.Clear(); Array.Clear(Counts, 0, Counts.Length);
            _step = 0; _actualDamage = 0f; _running = true;
            _deadline = Now + 80d;
            try
            {
                Application.runInBackground = true;
                _oldKeyboard = Keyboard.current; _oldMouse = Mouse.current;
                _keyboard = InputSystem.AddDevice<Keyboard>(); _mouse = InputSystem.AddDevice<Mouse>();
                var ground = new GameObject("SfxValidation_Ground", typeof(BoxCollider2D));
                ground.layer = LayerMask.NameToLayer("Ground");
                ground.transform.position = new Vector3(0f, -1f, 0f);
                ground.GetComponent<BoxCollider2D>().size = new Vector2(200f, 2f);
                _root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/LevelRuntime.prefab"));
                _player = _root.GetComponentInChildren<PlayerController>();
                _playerView = _player.GetComponent<PlayerSfxPresenter>();
                _status = _player.GetComponent<EntityStatus>();
                _damageable = _player.GetComponent<EntityDamageable>();
                _damageable.SetInvulnerable(false);
                _combat = _root.GetComponentInChildren<CombatSystem>();
                _wheel = _root.GetComponentInChildren<RuleWheelPresenter>();
                SetReference(_wheel, "_unlocks", _root.AddComponent<RuleWheelLabUnlocks>());
                _audio = _root.GetComponent<SfxPlayer>();
                _audio.OnPlayed += cue => Counts[(int)cue]++;
                _damageable.OnDamageTaken += info => _actualDamage = info.Amount;
                _player.TeleportTo(new Vector2(0f, 1.5f));
                Check(_playerView != null && _wheel.GetComponent<RuleWheelSfxPresenter>() != null,
                    "正式 LevelRuntime 同时接入玩家和轮盘表现组件");
                foreach (SfxCue cue in Enum.GetValues(typeof(SfxCue)))
                {
                    SfxBank.Entry entry = _audio.Bank.Find(cue);
                    Check(entry != null && entry.Clips.Length > 0 && entry.Clips.All(c => c != null), cue + " 真实素材引用完整");
                }
                Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/GameRoot.prefab")
                    .GetComponentsInChildren<SfxPlayer>(true).Length == 1, "GameRoot 嵌套继承唯一声源池");
                _due = Now + 1d;
                EditorApplication.update += Tick;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static double Now => EditorApplication.timeSinceStartup;
        private static int Count(SfxCue cue) => Counts[(int)cue];
        private static AudioSource[] SfxSources() => _audio.transform.Find("PlayerWheel_SfxVoices").GetComponents<AudioSource>();
        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        private static void Buttons(ushort buttons) => InputSystem.QueueStateEvent(_mouse, new MouseState { buttons = buttons });

        private static void Tick()
        {
            if (!_running) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (Now > _deadline) { Fail(new TimeoutException("音效验证超时 step=" + _step)); return; }
            if (Now < _due) return;
            try
            {
                switch (_step++)
                {
                    case 0:
                        Check(_player.IsGrounded && Count(SfxCue.Land) == 1, "真实物理首次接地只播放一次落地声");
                        _landCount = Count(SfxCue.Land);
                        Keys(Key.Space); _due = Now + 0.13d; break;
                    case 1:
                        Check(Count(SfxCue.Jump) == 1 && !_player.IsGrounded, "真实空格输入触发跳跃声与实际离地");
                        Keys(); _due = Now + 0.12d; break;
                    case 2:
                        Keys(Key.Space); _due = Now + 0.13d; break;
                    case 3:
                        Check(Count(SfxCue.AirJump) == 1, "真实二段跳有独立声音");
                        Keys(); _due = Now + 1.7d; break;
                    case 4:
                        Check(_player.IsGrounded && Count(SfxCue.Land) == _landCount + 1, "起跳后真实落地仅增加一次落地声");
                        Buttons(2); _due = Now + 0.12d; break;
                    case 5:
                        Check(Count(SfxCue.Dash) == 1, "真实右键冲刺触发一次声音");
                        Buttons(0); _due = Now + 0.5d; break;
                    case 6:
                        Buttons(1); _due = Now + 0.07d; break;
                    case 7:
                        Check(Count(SfxCue.SwordLight) == 0, "轻击起手阶段尚未误播挥剑声");
                        Buttons(0); _due = Now + 0.24d; break;
                    case 8:
                        Check(Count(SfxCue.SwordLight) == 1, "实际攻击开窗播放一次刀刃声，空挥也成立");
                        _due = Now + 0.55d; break;
                    case 9:
                        Check(Count(SfxCue.SwordLight) == 1, "同一挥剑在后续生效与收招帧不重复响");
                        Keys(Key.S); Buttons(1); _due = Now + 0.5d; break;
                    case 10:
                        Check(Count(SfxCue.SwordHeavy) == 1 && Count(SfxCue.SwordLight) == 1,
                            "真实下键加攻击触发重击声音，不叠加轻击声");
                        Keys(); Buttons(0);
                        _combat.ApplyDamage(null, _damageable, 10f, 0f);
                        Check(Count(SfxCue.Hurt) == 1 && _actualDamage == 10f, "实际扣血后播放受击声并携带真实伤害");
                        Check(SfxSources().Any(source => source.isPlaying && _audio.Bank.Find(SfxCue.Hurt).Clips.Contains(source.clip)),
                            "实际扣血使用当前配置的受击素材进入真实 AudioSource 播放");
                        _combat.ApplyDamage(null, _damageable, 10f, 0f);
                        Check(Count(SfxCue.Hurt) == 1, "受击无敌帧内伤害尝试不重复播声");
                        _snapshot = SnapshotSystem.Instance.Capture();
                        _combat.ApplyDamage(null, _damageable, -10f, 0f);
                        Check(Count(SfxCue.Hurt) == 1 && _status.Health == 100f, "治疗回血不播放受击声");
                        SnapshotSystem.Instance.Restore(_snapshot);
                        Check(_status.Health == 90f && Count(SfxCue.Hurt) == 1,
                            "真实快照恢复导致生命下降也不冒充受击");
                        Check(!SfxSources().Any(s => !s.ignoreListenerPause && s.isPlaying),
                            "快照恢复清除未来时间线的世界短音效");
                        _due = Now + 0.7d; break;
                    case 11:
                        _damageable.SetInvulnerable(true);
                        _combat.ApplyDamage(null, _damageable, 10f, 0f);
                        Check(Count(SfxCue.Hurt) == 1, "显式无敌不播放受击声");
                        _damageable.SetInvulnerable(false);
                        _damageable.ApplyDamage(new DamageInfo { Amount = 0f, Knockback = 0f });
                        Check(Count(SfxCue.Hurt) == 1, "零伤害纯击退通知不播放受击声");
                        RuleSystem.Instance.Request(AttributeId.Damage, RelationId.Constant, RuleSource.Player);
                        _combat.Processor.Tick();
                        _combat.ApplyDamage(null, _damageable, 5f, 0f);
                        Check(_status.Health == 90f && Count(SfxCue.Hurt) == 1, "真实伤害挂账期间不扣血也不播放受击声");
                        RuleSystem.Instance.Cancel(AttributeId.Damage);
                        _combat.Processor.Tick();
                        Check(_status.Health == 85f && Count(SfxCue.Hurt) == 2, "挂账结清真实扣血时才播放一次受击声");
                        Keys(Key.LeftShift); _due = Now + 0.25d; break;
                    case 12:
                        Check(_wheel.IsOpen && Count(SfxCue.WheelOpen) == 1, "真实 Shift 输入打开轮盘并播放界面声");
                        Check(Time.timeScale < 0.01f, "轮盘沿用原有慢时间而非改动游戏暂停");
                        _wheel.SelectAttributeIndex((int)AttributeId.Gravity);
                        _due = Now + 0.1d; break;
                    case 13:
                        Check(Count(SfxCue.WheelPreview) == 1, "属性预览在慢时间下正常发声");
                        _wheel.SelectAttributeIndex((int)AttributeId.Gravity);
                        Check(Count(SfxCue.WheelPreview) == 1, "悬停相同属性不会重复播声");
                        _wheel.SelectRelationIndex((int)RelationId.Enhance);
                        HoverRelation((int)RelationId.Enhance);
                        _due = Now + 0.1d; break;
                    case 14:
                        Check(Count(SfxCue.WheelPreview) == 2, "切换至关系选项播放预览声");
                        _wheel.SelectRelationIndex((int)RelationId.Enhance);
                        Check(Count(SfxCue.WheelPreview) == 2, "同一关系每帧预览不会连响");
                        _status.TryConsumeEnergy(_status.Energy);
                        Check(!_wheel.ConfirmSelection() && Count(SfxCue.WheelError) == 1 && _wheel.IsOpen,
                            "确认能量不足只播失败声并保持轮盘打开");
                        _status.RestoreEnergy(_status.EnergyMax);
                        Check(_wheel.ConfirmSelection() && Count(SfxCue.WheelConfirm) == 1
                            && Count(SfxCue.WheelCancel) == 0, "真实施法成功只播确认声，不叠加关闭取消声");
                        Keys(); _due = Now + 0.2d; break;
                    case 15:
                        Keys(Key.LeftShift); _due = Now + 0.2d; break;
                    case 16:
                        _wheel.SelectAttributeIndex((int)AttributeId.Gravity);
                        Buttons(2); _due = Now + 0.2d; break;
                    case 17:
                        Check(_wheel.Stage == RuleSelectionStage.Attribute && Count(SfxCue.WheelBack) == 1,
                            "真实右键返回一级扇区播放返回声");
                        Buttons(0); Keys(); _due = Now + 0.2d; break;
                    case 18:
                        Check(!_wheel.IsOpen && Count(SfxCue.WheelCancel) == 1, "松开 Shift 播放一次取消声并归还输入");
                        _wheel.Close(); Check(Count(SfxCue.WheelCancel) == 1, "重复关闭不重复播取消声");
                        RuleSystem.Instance.Cancel(AttributeId.Gravity);
                        MakePauseProbe();
                        Check(_audio.Play(SfxCue.Dash), "真实固定声源池接受测试长音以检查暂停位置");
                        _due = Now + 0.13d; break;
                    case 19:
                        _pausedSource = _audio.GetComponentsInChildren<AudioSource>().Single(s => s.clip == _longClip);
                        TimeManager.Instance.SetPaused(true);
                        _sample = _pausedSource.timeSamples;
                        Check(!_audio.Play(SfxCue.Jump), "暂停期间拒绝新增世界动作声音");
                        Check(_audio.Play(SfxCue.WheelOpen), "暂停期间界面保留独立可用通道");
                        _due = Now + 0.2d; break;
                    case 20:
                        Check(Math.Abs(_pausedSource.timeSamples - _sample) < 64, "暂停冻结已有世界音效的真实播放采样位置");
                        TimeManager.Instance.SetPaused(false);
                        _due = Now + 0.2d; break;
                    case 21:
                        Check(_pausedSource.timeSamples > _sample + 1000, "恢复暂停后世界音效从原播放位置继续");
                        UnityEngine.Random.State random = UnityEngine.Random.state;
                        float expectedRandom = UnityEngine.Random.value;
                        UnityEngine.Random.state = random;
                        Check(_audio.Play(SfxCue.Land), "音效变体测试接受真实落地声请求");
                        Check(UnityEngine.Random.value == expectedRandom, "音效变体与音高不消耗玩法随机数状态");
                        UnityEngine.Random.state = random;
                        _audio.ClearWorld();
                        _audio.Play(SfxCue.WheelConfirm);
                        _audio.ClearWorld();
                        Check(_audio.GetComponentsInChildren<AudioSource>().Any(s => s.ignoreListenerPause && s.isPlaying),
                            "清理世界声音保留正在播放的界面确认音");
                        for (int i = 0; i < 30; i++) _audio.Play(SfxCue.Jump);
                        Check(Count(SfxCue.Jump) == 2, "同帧大量重复动作请求只接受一次，具有最短间隔限制");
                        _audio.enabled = false;
                        _due = Now + 0.1d; break;
                    case 22:
                        Check(_root.GetComponentsInChildren<AudioSource>().Count(s => s.transform.parent == _audio.transform) == 4,
                            "停用音效池后释放九个声源且保留原有四个音乐声源");
                        _audio.enabled = true;
                        _playerView.enabled = false;
                        _due = Now + 0.7d; break;
                    case 23:
                        int hurtBefore = Count(SfxCue.Hurt);
                        _combat.ApplyDamage(null, _damageable, 1f, 0f);
                        Check(Count(SfxCue.Hurt) == hurtBefore, "停用玩家表现后实际受击不再播放声音");
                        _playerView.enabled = true;
                        _due = Now + 0.6d; break;
                    case 24:
                        _combat.ApplyDamage(null, _damageable, 9999f, 0f);
                        Check(_damageable.IsDead && Count(SfxCue.Hurt) == 3 && _actualDamage == 84f,
                            "致命一击仍播放一次受击声，通知使用剩余生命而非过量伤害");
                        Check(_audio.GetComponentsInChildren<AudioSource>().Length == 13,
                            "多轮交互及重新启用后固定九个音效声源加四个音乐声源");
                        Finish(true); break;
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void HoverRelation(int index)
        {
            RuleFan fan = _wheel.RelationFan;
            Vector2 point = fan.LocalToWorld(fan.OptionPoint(index));
            IPanel panel = _wheel.GetComponent<UIDocument>().rootVisualElement.panel;
            Vector2 origin = RuntimePanelUtils.ScreenToPanel(panel, Vector2.zero);
            Vector2 xUnit = RuntimePanelUtils.ScreenToPanel(panel, Vector2.right) - origin;
            Vector2 yUnit = RuntimePanelUtils.ScreenToPanel(panel, Vector2.up) - origin;
            Vector2 screen = new Vector2((point.x - origin.x) / xUnit.x,
                _wheel.WorldCamera.pixelHeight - (point.y - origin.y) / yUnit.y);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = screen });
        }

        private static void MakePauseProbe()
        {
            _testBank = UnityEngine.Object.Instantiate(_audio.Bank);
            _longClip = AudioClip.Create("SfxValidation_PauseProbe", 88200, 1, 44100, false);
            var samples = new float[88200];
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(i * 2f * Mathf.PI * 220f / 44100f) * 0.02f;
            _longClip.SetData(samples, 0);
            _testBank.Find(SfxCue.Dash).Clips = new[] { _longClip };
            SetReference(_audio, "_bank", _testBank);
        }

        private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Results.Add("PASS " + description);
        }

        private static void Fail(Exception ex)
        {
            Results.Add("FAIL step=" + _step + " " + ex);
            Debug.LogException(ex); Finish(false);
        }

        private static void Finish(bool passed)
        {
            _running = false; EditorApplication.update -= Tick;
            if (_wheel != null) _wheel.Close();
            TimeManager.Instance?.SetPaused(false);
            TimeManager.Instance?.RemoveScale(TimeSource.UI);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            _oldKeyboard?.MakeCurrent(); _oldMouse?.MakeCurrent();
            if (_testBank != null) UnityEngine.Object.Destroy(_testBank);
            if (_longClip != null) UnityEngine.Object.Destroy(_longClip);
            string summary = "passed=" + Results.Count(r => r.StartsWith("PASS")) + " failed=" + (passed ? 0 : 1);
            Directory.CreateDirectory("Logs/PlayerWheelSfx");
            File.WriteAllLines("Logs/PlayerWheelSfx/Validation.txt", Results.Concat(new[] { summary }));
            Debug.Log("[PlayerWheelSfxValidation] " + summary);
            EditorApplication.isPlaying = false;
        }
    }
}
