using System;
using System.IO;
using RuleGame.Infrastructure;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RuleGame.Editor.Animations
{
    /// <summary>副本中使用生产 Hero 控制器、精灵和真实伤害入口检查受伤动画不会被机动动作吞掉。</summary>
    public static class PlayerHurtValidation
    {
        private const string Pending = "RuleGame.Player.HurtValidation";
        private static int _passed;

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Validate();
            };
        }
        private static EntityStatus _status;
        private static EntityDamageable _receiver;
        private static Animator _animator;
        private static SpriteRenderer _renderer;
        private static PlayerAnimationDriver _driver;
        private static int _step, _frame = -1;
        private static float _due, _pausedAge;
        private static double _deadline;
        private static void Validate()
        {
            try
            {
                var go = new GameObject("Player"); go.SetActive(false);
                go.AddComponent<Rigidbody2D>(); go.AddComponent<CapsuleCollider2D>();
                _status = go.AddComponent<EntityStatus>();
                var player = go.AddComponent<PlayerController>(); player.enabled = false;
                var playerData = new SerializedObject(player);
                playerData.FindProperty("_inputActions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Settings/InputSystem_Actions.inputactions");
                playerData.ApplyModifiedPropertiesWithoutUndo();
                _receiver = go.AddComponent<EntityDamageable>();
                var receiverData = new SerializedObject(_receiver);
                receiverData.FindProperty("_combat").objectReferenceValue = new GameObject("Combat").AddComponent<CombatSystem>();
                receiverData.ApplyModifiedPropertiesWithoutUndo();
                var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false);
                _renderer = visual.AddComponent<SpriteRenderer>();
                _animator = visual.AddComponent<Animator>();
                _animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Art/Animations/Hero/Hero.controller");
                _driver = go.AddComponent<PlayerAnimationDriver>();
                go.SetActive(true);
                _due = Time.time + .1f; _deadline = EditorApplication.timeSinceStartup + 20;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("受伤动画验证超时");
                if (Time.time < _due) return;
                switch (_step)
                {
                    case 0:
                        _receiver.ApplyDamage(new DamageInfo { Amount = 10f });
                        _due = Time.time + .07f; _step++; break;
                    case 1:
                        Check(_status.Health == 90f && InState(_animator, "Hurt"), "实际掉血触发受伤状态");
                        var hurt = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Animations/Hero/Hero_Hurt.anim");
                        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(hurt);
                        var frames = AnimationUtility.GetObjectReferenceCurve(hurt, bindings[0]);
                        Check(_renderer.sprite != null && Array.Exists(frames, frame => frame.value == _renderer.sprite), "受伤剪辑实际输出已有精灵帧");
                        _animator.SetTrigger(PlayerAnimationDriver.ParamLand);
                        _animator.SetTrigger(PlayerAnimationDriver.ParamAttack);
                        _animator.SetTrigger(PlayerAnimationDriver.ParamHeavy);
                        _due = Time.time + .05f; _step++; break;
                    case 2:
                        Check(InState(_animator, "Hurt"), "受伤动画不被落地或攻击立即覆盖");
                        _animator.ResetTrigger(PlayerAnimationDriver.ParamLand);
                        _animator.ResetTrigger(PlayerAnimationDriver.ParamAttack);
                        _animator.ResetTrigger(PlayerAnimationDriver.ParamHeavy);
                        _status.SetPaused(true); _driver.SendMessage("Update");
                        _pausedAge = _animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                        _due = Time.time + .2f; _step++; break;
                    case 3:
                        Check(Mathf.Approximately(_pausedAge, _animator.GetCurrentAnimatorStateInfo(0).normalizedTime), "暂停冻结受伤演出");
                        _status.SetPaused(false); _due = Time.time + .9f; _step++; break;
                    case 4:
                        Check(!InState(_animator, "Hurt"), "受伤播完后正常退出");
                        _status.ApplyHealthDelta(5f); _due = Time.time + .1f; _step++; break;
                    case 5:
                        Check(!InState(_animator, "Hurt"), "回血不误触发受伤");
                        _receiver.ApplyDamage(new DamageInfo { Amount = 5f });
                        _due = Time.time + .18f; _step++; break;
                    case 6:
                        Check(InState(_animator, "Hurt"), "恢复后下一次掉血仍可播放受伤");
                        _status.ApplyHealthDelta(-100f); _due = Time.time + .18f; _step++; break;
                    case 7:
                        Check(InState(_animator, "Die"), "致命伤优先进入死亡动画");
                        _animator.SetTrigger(PlayerAnimationDriver.ParamAttack);
                        _due = Time.time + .15f; _step++; break;
                    case 8:
                        Check(InState(_animator, "Die"), "死亡动画不被攻击覆盖");
                        Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            if (error != null) Debug.LogError(error);
            SessionState.SetBool(Pending, false);
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/PlayerHurtValidation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\n{error}\n");
            EditorApplication.Exit(error == null ? 0 : 1);
        }
        private static bool InState(Animator animator, string name)
            => animator.GetCurrentAnimatorStateInfo(0).IsName(name)
                || animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(name);
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            _passed++; Debug.Log("PASS: " + name);
        }
    }
}
