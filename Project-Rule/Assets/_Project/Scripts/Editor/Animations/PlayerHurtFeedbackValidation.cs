using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Player;
using RuleGame.Presentation.Audio;
using RuleGame.Presentation.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace RuleGame.Editor.Animations
{
    /// <summary>可丢弃副本中，用生产 Prefab、真实输入、伤害、Slide 与 URP 检查受击链路。</summary>
    public static class PlayerHurtFeedbackValidation
    {
        private const string Pending = "RuleGame.Player.HurtFeedbackValidation";
        private static PlayerController _player;
        private static EntityDamageable _receiver;
        private static EntityStatus _status;
        private static PlayerHurtFeedback _feedback;
        private static PlayerAnimationDriver _driver;
        private static Animator _animator;
        private static SpriteRenderer _body;
        private static Transform _attacker;
        private static Keyboard _keyboard;
        private static int _passed, _strikes, _damageEvents, _hurtSounds;
        private static Camera _camera;
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/PlayerHurtFeedback"));

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅在可丢弃工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".Preview", false);
            EditorApplication.isPlaying = true;
        }

        public static void RunPreviewBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅在可丢弃工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Pending + ".Preview", true);
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    if (SessionState.GetBool(Pending + ".Preview", false)) RecordPreview();
                    else Validate();
                }
            };
        }

        private static void Validate()
        {
            string failure = null;
            try
            {
                Directory.CreateDirectory(Folder);
                Application.runInBackground = true;
                TimeManager.Initialize();
                WorldRegistry.Initialize();
                SnapshotRoster.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
                Physics2D.simulationMode = SimulationMode2D.Script;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                _keyboard = InputSystem.AddDevice<Keyboard>();
                _keyboard.MakeCurrent();
                MakePlayer();
                _player.OnAttackStruck += cue => _strikes++;
                _player.GetComponent<SfxPlayer>().OnPlayed += cue => { if (cue == SfxCue.Hurt) _hurtSounds++; };
                _receiver.OnDamageTaken += damage => _damageEvents++;
                Check(_player.GetComponents<PlayerHurtFeedback>().Length == 1, "生产 Prefab 自动接入且只有一份受击反馈");
                Check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("PlayerHurtFeedback_Mat").shader), "受击 Shader 导入无编译错误");
                Check(GraphicsSettings.currentRenderPipeline != null, "真实项目 URP 渲染管线");

                Step(4);
                Material original = _body.sharedMaterial;
                Color originalColor = _body.color;
                var block = new MaterialPropertyBlock(); block.SetFloat("_Unrelated", .73f); _body.SetPropertyBlock(block);
                _camera.transform.position = _player.transform.position + new Vector3(0, .35f, -10);
                Color[] before = Capture("01_Before.png");
                Keys(Key.C);
                Step(1);
                Check(_player.IsDashing, "真实输入开始冲刺");
                Hit(10, 8);
                Check(_status.Health == 90 && _damageEvents == 1, "实际伤害只扣一次血并发出一次反馈");
                Check(_hurtSounds == 1 && _player.GetComponentsInChildren<AudioSource>().Any(source => source.isPlaying && source.clip != null && source.clip.name == "PlayerHurt"),
                    "生产音效库实际播放受击声，不再为空槽");
                Transform motion = _body.transform.parent;
                Check(motion.name == "HurtMotionRoot" && Quaternion.Angle(motion.localRotation, Quaternion.identity) > 5f,
                    "受击当帧身体后仰，动画节点与身体碰撞体保持分离");
                var impulse = _camera.GetComponent<BossCameraImpulse>();
                Matrix4x4 neutral = _camera.worldToCameraMatrix;
                typeof(BossCameraImpulse).GetMethod("BeforeRender", Private).Invoke(impulse,
                    new object[] { default(ScriptableRenderContext), _camera });
                Check(_camera.worldToCameraMatrix.m03 > neutral.m03 + .05f, "镜头沿真实受击方向顿挫");
                typeof(BossCameraImpulse).GetMethod("AfterRender", Private).Invoke(impulse,
                    new object[] { default(ScriptableRenderContext), _camera });
                Check(_camera.worldToCameraMatrix == neutral, "受击镜头渲染后归还自动视图矩阵");
                _camera.transform.position += Vector3.right * .5f;
                Check(Mathf.Abs(_camera.worldToCameraMatrix.m03 - neutral.m03 + .5f) < .001f,
                    "镜头顿挫后跟随相机继续移动，不锁死画面");
                _camera.transform.position -= Vector3.right * .5f;
                Check(!_player.IsDashing && (_player.AttackComboIndex < 0 && !_player.IsHeavyBusy) && _player.IsHurt, "受击立即打断冲刺和攻击");
                Check(_player.IsInHurtImpactPause && _player.Velocity == Vector2.zero, "命中定格收掉自主动量");
                Check(Visible("HurtFlash") == 1 && Visible("HurtBurst") == 1 && Visible("HurtMote") == 18,
                    "闪身、爆点、十八颗有界碎屑同时出现");
                Color[] flash = Capture("02_Impact.png");
                int brighter = flash.Where((c, i) => c.r + c.g + c.b > before[i].r + before[i].g + before[i].b + .5f).Count();
                Check(brighter > 100, "URP 实际像素出现清晰闪白与爆点");
                _body.GetPropertyBlock(block);
                Check(_body.sharedMaterial == original && _body.color == originalColor && Mathf.Approximately(block.GetFloat("_Unrelated"), .73f),
                    "原角色材质、颜色及其它属性块保留");
                Vector2 frozen = _player.GetComponent<Rigidbody2D>().position;
                Keys(Key.A, Key.J, Key.Space);
                Step(2);
                Check(Vector2.Distance(frozen, _player.GetComponent<Rigidbody2D>().position) < .001f, "短定格实际冻结物理位置");
                Check(_strikes == 0, "定格期间攻击输入不兑现");
                float scale = Time.timeScale;
                _status.SetPaused(true);
                float invincibility = _receiver.InvincibilityRemaining;
                Step(20);
                Check(_player.IsInHurtImpactPause && Mathf.Approximately(_receiver.InvincibilityRemaining, invincibility),
                    "暂停同时保留命中定格、击退及无敌窗口");
                Check(Time.timeScale == scale, "受击和暂停没有篡改全局时间倍率");
                _status.SetPaused(false);
                Keys();
                Step(5);
                Check(_player.GetComponent<Rigidbody2D>().position.x > frozen.x + .03f, "解冻后朝远离左侧攻击者的方向弹开");
                Check(_player.GetComponent<Rigidbody2D>().position.y > frozen.y + .01f, "向上的击退不被贴地吸附吞掉");
                _animator.Update(0f);
                Call(_driver, "Update");
                Check(InState("Hurt"), "真实 Animator 输出 Hurt 状态");
                Check(_animator.speed > 1f, "原七帧受伤压入短硬直而不修改原画");
                SetAge(.10f); Capture("03_Recoil.png");
                _camera.backgroundColor = new Color(.88f, .86f, .8f);
                Capture("05_LightBackdrop.png");
                _camera.backgroundColor = new Color(.08f, .10f, .15f);
                Step(24);
                Check(!_player.IsHurt && !InState("Hurt"), "短硬直结束，动画和操作均恢复");
                Check(_body.transform.parent.localPosition == Vector3.zero && _body.transform.parent.localScale == Vector3.one
                    && Quaternion.Angle(_body.transform.parent.localRotation, Quaternion.identity) < .01f,
                    "受击后的身体偏移、倾斜和拉伸全部归零");
                Check(_strikes == 0, "硬直结束没有补播受击前或受击中的攻击");
                Keys(Key.J); Step(1);
                Check((_player.AttackComboIndex >= 0 || _player.IsHeavyBusy), "硬直结束后的新按键正常起刀");
                ResetTarget();

                // 未生效的轻击、重击和跨帧跳跃缓冲均须被打断。
                Keys(Key.J); Step(1); Hit(1, 0); Keys(); Step(30);
                Check(_strikes == 0, "轻击起手受击后不产生幽灵命中");
                ResetTarget(); Step(3);
                Keys(Key.S, Key.J); Step(1);
                Check(_player.IsHeavyBusy, "真实输入开始重击起手");
                Hit(1, 0); Keys(); Step(35);
                Check(_strikes == 0 && !_player.IsHeavyBusy, "重击受击打断后不再落下伤害");

                ResetTarget();
                var wall = new GameObject("RecoilWall") { layer = 8 };
                wall.transform.position = new Vector3(1, 2);
                wall.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 6);
                Physics2D.SyncTransforms(); Hit(1, 8); Step(30);
                Check(_player.GetComponent<Collider2D>().bounds.max.x < .92f, "击退由真实 Slide 碰撞挡住，不穿墙");
                UnityEngine.Object.DestroyImmediate(wall);

                ResetTarget();
                int events = _damageEvents;
                Hit(-5, 0); Hit(0, 2);
                Check(_damageEvents == events && !_player.IsHurt && Visible("HurtBurst") == 0,
                    "治疗与挂账纯击退不误触发受击硬直或特效");
                _receiver.SetInvulnerable(true); Hit(10, 8); _receiver.SetInvulnerable(false);
                Check(_damageEvents == events && !_player.IsHurt, "免伤不会触发假反馈");
                Hit(1, 4); Hit(1, 4);
                Check(_damageEvents == events + 1, "无敌期重复命中不会重开反馈");
                Step(20); SetAge(.35f); Capture("04_Protection.png");
                float blinkAlpha = _body.color.a;
                SetAge(.40f);
                Check(Mathf.Abs(blinkAlpha - _body.color.a) > .5f, "无敌期真实身体明暗交替，不再整段泛白");
                SetAge(.35f);
                Check(Visible("HurtFlash") == 1, "短硬直结束后，剩余无敌期仍有可见闪烁提示");
                int rendererCount = _player.GetComponentsInChildren<SpriteRenderer>(true).Length;
                for (int i = 0; i < 12; i++) { SetInvincibility(0); Hit(1, 1); }
                Check(rendererCount == _player.GetComponentsInChildren<SpriteRenderer>(true).Length, "反复受击复用固定数量的 Renderer");

                var snapshot = SnapshotSystem.Instance.Capture();
                SnapshotSystem.Instance.Restore(snapshot);
                Check(!_player.IsHurt && Visible("HurtFlash") == 0 && Visible("HurtBurst") == 0 && Visible("HurtMote") == 0,
                    "真实快照恢复清除未来时间线硬直与视觉");
                Check(_receiver.InvincibilityRemaining == 0, "回溯不遗留未来无敌窗口");
                Check((float)typeof(BossCameraImpulse).GetField("_kickStrength", Private).GetValue(impulse) == 0,
                    "回溯只清理受击镜头冲击，不残留未来方向偏移");
                ResetTarget();
                Physics2D.gravity = new Vector2(0, 9.81f);
                _player.TeleportTo(new Vector2(0, 4));
                _attacker.position = new Vector3(3, 4);
                Hit(1, 8); Step(6);
                Check(_player.GetComponent<Rigidbody2D>().position.x < -.03f, "右侧来袭时向左击退");
                Check(_player.IsBodyFlipped, "受击仍遵循重力反转的身体朝向");
                Check(EntityDamageable.ResolveKnockbackDirection(_player.transform.position, _attacker, .4f).y < 0,
                    "反重力时击退抬升朝新的上方");
                Physics2D.gravity = new Vector2(0, -9.81f);
                ResetTarget();
                Hit(200, 5); Step(12);
                Check(_status.Health == 0 && _receiver.IsDead && InState("Die"), "致命伤优先进入死亡演出");
                Check(_animator.speed == 1f, "死亡动画保留原速度");
                _receiver.Revive(); Step(2);
                Check(!_receiver.IsDead && !InState("Die"), "复活后脱离死亡状态");
                SetInvincibility(0); Hit(1, 3);
                _player.enabled = false; Call(_feedback, "LateUpdate");
                Check(!_player.IsHurt && Visible("HurtFlash") == 0 && Visible("HurtBurst") == 0,
                    "停用玩家清理定格和所有受击层");
                Check(_body.color == originalColor && _body.transform.parent.localScale == Vector3.one,
                    "停用时恢复原身体颜色、透明度和姿态");
                Check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("PlayerHurtFeedback_Mat").shader), "实际渲染后 Shader 仍无编译错误");
            }
            catch (Exception error) { failure = error.ToString(); Debug.LogError(failure); }
            finally
            {
                SessionState.SetBool(Pending, false);
                File.WriteAllText(Path.Combine(Folder, "Results.txt"), $"Passed: {_passed}\nFailed: {(failure == null ? 0 : 1)}\n{failure}\n");
                EditorApplication.Exit(failure == null ? 0 : 1);
            }
        }

        private static void MakePlayer()
        {
            var combat = new GameObject("Combat").AddComponent<CombatSystem>();
            var staging = new GameObject("InactiveSetup"); staging.SetActive(false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab");
            GameObject go = UnityEngine.Object.Instantiate(prefab, staging.transform);
            _player = go.GetComponent<PlayerController>();
            _receiver = go.GetComponent<EntityDamageable>();
            _status = go.GetComponent<EntityStatus>();
            var inputs = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = inputs.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value).AddCompositeBinding("2DVector")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d").With("Down", "<Keyboard>/s");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Attack", InputActionType.Button, "<Keyboard>/j");
            map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            var data = new SerializedObject(_player);
            data.FindProperty("_inputActions").objectReferenceValue = inputs;
            data.FindProperty("_combat").objectReferenceValue = combat;
            data.FindProperty("_respawnOnDeath").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo();
            data = new SerializedObject(_receiver); data.FindProperty("_combat").objectReferenceValue = combat; data.ApplyModifiedPropertiesWithoutUndo();
            go.transform.SetParent(null); go.transform.position = new Vector3(0, .52f);
            _feedback = go.GetComponent<PlayerHurtFeedback>();
            _driver = go.GetComponent<PlayerAnimationDriver>();
            _animator = go.GetComponentInChildren<Animator>();
            _body = go.GetComponentsInChildren<SpriteRenderer>().First(r => r.gameObject.name == "Visual");
            var floor = new GameObject("Floor") { layer = 8 }; floor.transform.position = new Vector3(0, -.5f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(40, 1);
            var ceiling = new GameObject("Ceiling") { layer = 8 }; ceiling.transform.position = new Vector3(0, 7);
            ceiling.AddComponent<BoxCollider2D>().size = new Vector2(40, 1);
            _attacker = new GameObject("Attacker").transform; _attacker.position = new Vector3(-3, 1);
            _camera = new GameObject("Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            _camera.gameObject.AddComponent<AudioListener>();
            _camera.orthographic = true; _camera.orthographicSize = 2.3f;
            _camera.backgroundColor = new Color(.08f, .10f, .15f); _camera.clearFlags = CameraClearFlags.SolidColor;
            Physics2D.SyncTransforms();
        }

        private static void RecordPreview()
        {
            string failure = null;
            try
            {
                Directory.CreateDirectory(Folder);
                Directory.CreateDirectory(Path.Combine(Folder, "Frames"));
                Application.runInBackground = true;
                TimeManager.Initialize(); WorldRegistry.Initialize(); SnapshotRoster.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
                Physics2D.simulationMode = SimulationMode2D.Script;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                _keyboard = InputSystem.AddDevice<Keyboard>(); _keyboard.MakeCurrent();
                MakePlayer();
                _player.TeleportTo(new Vector2(.8f, .52f));
                _camera.transform.position = new Vector3(.1f, 1.1f, -10);
                _camera.orthographicSize = 2.15f;
                Sprite stageSprite = Sprite.Create(Texture2D.whiteTexture,
                    new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                    Vector2.one * .5f, Texture2D.whiteTexture.width);
                Material stageMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/P4Whitebox.mat");
                MakeStagePiece("Ground", new Vector3(0, -.5f, 0), new Vector2(40, 1), new Color(.16f, .19f, .24f), -10, stageSprite, stageMaterial);
                MakeStagePiece("GroundRim", new Vector3(0, -.025f, 0), new Vector2(40, .05f), new Color(.36f, .4f, .45f), -9, stageSprite, stageMaterial);
                for (int i = -6; i <= 6; i++)
                    MakeStagePiece("Pillar", new Vector3(i * 1.8f, 2, 0), new Vector2(.14f, 4), new Color(.095f, .12f, .17f), -20, stageSprite, stageMaterial);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/FieldMinions/StoneGuard.prefab");
                EnemyActor enemy = UnityEngine.Object.Instantiate(prefab, new Vector3(-1.25f, 1, 0), Quaternion.identity).GetComponent<EnemyActor>();
                enemy.enabled = false;
                foreach (EnemyVfxPresenter vfx in enemy.GetComponents<EnemyVfxPresenter>()) vfx.enabled = false;
                enemy.Target = _player.transform;
                Call(enemy, "Start");
                Physics2D.SyncTransforms();
                enemy.transform.position += Vector3.up * (.015f - enemy.GetComponent<Collider2D>().bounds.min.y);
                _attacker = enemy.transform;
                Physics2D.SyncTransforms();
                var combat = UnityEngine.Object.FindFirstObjectByType<CombatSystem>();
                for (int frame = 0; frame < 108; frame++)
                {
                    if (frame == 0) Keys(Key.A);
                    if (frame == 18)
                    {
                        Keys();
                        combat.ApplyDamage(enemy, _receiver, 10f, 5f);
                    }
                    if (frame == 46) Keys(Key.D);
                    if (frame == 55) Keys();
                    Step(1);
                    foreach (EnemySpriteAnimator view in enemy.GetComponentsInChildren<EnemySpriteAnimator>()) Call(view, "LateUpdate");
                    foreach (FieldMinionView view in enemy.GetComponentsInChildren<FieldMinionView>()) Call(view, "LateUpdate");
                    BossCameraImpulse impulse = _camera.GetComponent<BossCameraImpulse>();
                    MethodInfo advance = typeof(BossCameraImpulse).GetMethod("Advance", Private);
                    if (impulse != null && advance != null) advance.Invoke(impulse, new object[] { Time.fixedDeltaTime });
                    else if (impulse != null)
                    {
                        // 首版相机只有 Update；受控步进同样推进它的衰减，不能让旧震动停在首帧。
                        FieldInfo ageField = typeof(BossCameraImpulse).GetField("_age", Private);
                        float age = (float)ageField.GetValue(impulse) + Time.fixedDeltaTime;
                        ageField.SetValue(impulse, age);
                        if (age >= (float)typeof(BossCameraImpulse).GetField("_duration", Private).GetValue(impulse))
                            typeof(BossCameraImpulse).GetField("_strength", Private).SetValue(impulse, 0f);
                    }
                    Capture("Frames/Frame_" + frame.ToString("000") + ".png");
                }
                File.WriteAllText(Path.Combine(Folder, "Preview.txt"), "108 real Unity URP frames; 60 Hz manually stepped physics.\nDamage at frame 18 via CombatSystem: StoneGuard to EnemyLabPlayer, 10 damage, 5 knockback.\nRight input frames 46-54 demonstrates recovered control.\n");
            }
            catch (Exception error) { failure = error.ToString(); Debug.LogError(failure); }
            finally
            {
                SessionState.SetBool(Pending, false); SessionState.SetBool(Pending + ".Preview", false);
                EditorApplication.Exit(failure == null ? 0 : 1);
            }
        }

        private static void MakeStagePiece(string name, Vector3 position, Vector2 size, Color color, int order, Sprite sprite, Material material)
        {
            var renderer = new GameObject(name).AddComponent<SpriteRenderer>();
            renderer.transform.position = position; renderer.transform.localScale = new Vector3(size.x, size.y, 1);
            renderer.sprite = sprite; renderer.sharedMaterial = material; renderer.color = color; renderer.sortingOrder = order;
        }

        private static void Step(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Call(_player, "Update");
                Call(_player, "FixedUpdate");
                Physics2D.Simulate(Time.fixedDeltaTime);
                Call(_driver, "Update");
                _animator.Update(Time.fixedDeltaTime);
                Call(_driver, "LateUpdate");
                _feedback.SendMessage("Advance", Time.fixedDeltaTime);
                _receiver.SendMessage("AdvanceInvincibility", Time.fixedDeltaTime);
                InputSystem.Update();
            }
        }

        private static void Call(object target, string method)
            => target.GetType().GetMethod(method, Private).Invoke(target, null);
        private static void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys)); InputSystem.Update();
        }
        private static void Hit(float amount, float knockback)
            => _receiver.ApplyDamage(new DamageInfo { Attacker = _attacker, Amount = amount, Knockback = knockback });
        private static void SetInvincibility(float value)
            => typeof(EntityDamageable).GetField("_invincibleLeft", Private).SetValue(_receiver, value);
        private static void SetAge(float age)
        {
            typeof(PlayerHurtFeedback).GetField("_age", Private).SetValue(_feedback, age);
            typeof(PlayerHurtFeedback).GetMethod("Present", Private).Invoke(_feedback, null);
        }
        private static void ResetTarget()
        {
            Keys();
            _player.TeleportTo(new Vector2(0, .52f));
            _receiver.Revive(); SetInvincibility(0);
            _feedback.enabled = false; _feedback.enabled = true;
            _strikes = 0;
            _animator.Play("Idle", 0, 0); _animator.Update(0);
            Physics2D.SyncTransforms();
        }
        private static int Visible(string name)
            => _player.GetComponentsInChildren<SpriteRenderer>().Count(r => r.name == name && r.enabled);
        private static bool InState(string name)
            => _animator.GetCurrentAnimatorStateInfo(0).IsName(name)
                || _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).IsName(name);
        private static Color[] Capture(string name)
        {
            var target = new RenderTexture(960, 540, 24);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name), image.EncodeToPNG());
            Color[] pixels = image.GetPixels();
            _camera.targetTexture = null; RenderTexture.active = previous;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            return pixels;
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            _passed++; Debug.Log("PASS: " + message);
        }
    }
}
