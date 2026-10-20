using System;
using System.IO;
using System.Linq;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RuleGame.Editor.Enemy
{
    /// <summary>真实 Prefab 的受伤、生命周期和 URP 像素验收。仅在可丢弃的完整工程副本运行。</summary>
    public static class EnemyHitFeedbackValidation
    {
        private const string Pending = "RuleGame.EnemyHitFeedbackValidation";
        private static EnemyActor _actor;
        private static Transform _attacker;
        private static EnemyHitFeedback _view;
        private static EnemyFeedback _lastHit;
        private static int _hits, _passed, _step, _lastFrame;
        private static float _due, _progress;
        private static double _deadline;
        private static Vector3 _impactPosition;
        private static Material _originalMaterial;
        private static object _snapshot;
        private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/EnemyHitFeedback"));

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
                if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            };
        }

        private static void Begin()
        {
            try
            {
                Application.runInBackground = true;
                Directory.CreateDirectory(Folder);
                TimeManager.Initialize(); WorldRegistry.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                Physics2D.simulationMode = SimulationMode2D.Script;
                _attacker = new GameObject("Attacker").transform;
                _attacker.position = new Vector3(-4, 2);
                // 每个生产 Prefab 都从已有视图接入，包含召唤物与九种关卡小怪。
                foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
                {
                    EnemyActor actor = MakeActor(kind, Vector3.zero);
                    Check(actor.GetComponent<EnemyHitFeedback>() != null, kind + " 自动接入受伤反馈");
                    Check(actor.GetComponents<EnemyHitFeedback>().Length == 1, kind + " 只有一个反馈组件");
                    UnityEngine.Object.DestroyImmediate(actor.gameObject);
                }
                _actor = MakeActor(EnemyKind.PlushSeal, new Vector3(0, 2));
                _view = _actor.GetComponent<EnemyHitFeedback>();
                _actor.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.Hit) { _hits++; _lastHit = cue; } };
                var combat = new GameObject("Combat").AddComponent<CombatSystem>();
                float healthBefore = _actor.Status.Health;
                combat.ApplyDamage(_attacker, _actor, 1, 0);
                Check(_actor.Status.Health == healthBefore - 1 && _hits == 1,
                    "CombatSystem 与 DamageProcessor 的真实结算触发受伤反馈");
                _actor.RestoreLifeFlag(false); _hits = 0;
                _originalMaterial = Body.sharedMaterial;
                var properties = new MaterialPropertyBlock(); properties.SetFloat("_UnrelatedEffect", .73f);
                Body.SetPropertyBlock(properties);
                _snapshot = _actor.CaptureSnapshotState();
                Hit(1);
                Check(_hits == 1 && _lastHit.Damage == 1 && !_lastHit.Staggered, "普通扣血只通知一次且携带实际伤害");
                Check(_lastHit.Position.x < _actor.transform.position.x && _lastHit.Direction.x > .9f,
                    "命中位于身体迎击面且方向由攻击者指向敌人");
                Check(Visible("HitFlash").Length == 1 && Visible("HitImpact").Length == 1, "闪白与独立冲击同时出现");
                Check(Body.sharedMaterial == _originalMaterial, "原有材质未被替换或实例化");
                Body.GetPropertyBlock(properties);
                Check(Mathf.Approximately(properties.GetFloat("_UnrelatedEffect"), .73f), "原 Renderer 属性块保留");
                Hit(1); Check(_hits == 1, "无敌间隔不误播二次命中");
                _actor.RestoreSnapshotState(_snapshot);
                Check(!Visible("HitFlash").Any() && !Visible("HitImpact").Any(), "快照恢复立即清除旧时间线反馈");
                Hit(-3); Hit(0);
                Check(_hits == 1, "治疗与挂账纯击退不播放扣血特效");
                StepActor(.14f); Hit(_actor.Definition.MinionStaggerThreshold);
                Check(_lastHit.Staggered && _actor.Brain.StateName == "Hurt", "破韧信息来自实际伤害后的状态");
                _impactPosition = Visible("HitImpact")[0].transform.position;
                _actor.transform.position += Vector3.right * 2;
                _actor.Status.SetPaused(true);
                _due = Time.time + .06f;
                _deadline = EditorApplication.timeSinceStartup + 45;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static EnemyActor MakeActor(EnemyKind kind, Vector3 position)
        {
            string subfolder = (int)kind >= (int)EnemyKind.StoneGuard ? "FieldMinions/" : "";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + subfolder + kind + ".prefab");
            if (prefab == null) throw new InvalidOperationException("缺少真实 Prefab: " + kind);
            var actor = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<EnemyActor>();
            actor.enabled = false;
            foreach (var vfx in actor.GetComponents<EnemyVfxPresenter>()) vfx.enabled = false;
            actor.Definition = UnityEngine.Object.Instantiate(actor.Definition);
            actor.Definition.Health = 500;
            actor.Target = _attacker;
            actor.SendMessage("Start");
            return actor;
        }

        private static SpriteRenderer Body => _actor.TryGetComponent<FieldMinionView>(out var view)
            ? view.Body : _actor.GetComponentInChildren<EnemySpriteAnimator>().GetComponent<SpriteRenderer>();
        private static SpriteRenderer[] Visible(string name) => _actor.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(renderer => renderer.name == name && renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
        private static SpriteRenderer[] All(string name) => _actor.GetComponentsInChildren<SpriteRenderer>(true)
            .Where(renderer => renderer.name == name).ToArray();
        private static void Hit(float amount) => _actor.ApplyDamage(new DamageInfo { Amount = amount, Knockback = 3, Attacker = _attacker });
        private static void StepActor(float seconds)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds / Time.fixedDeltaTime); i++) _actor.SendMessage("FixedUpdate");
        }
        private static float Progress(SpriteRenderer renderer)
        {
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            return properties.GetFloat("_Progress");
        }
        private static void PresentView() => typeof(EnemyHitFeedback).GetMethod("LateUpdate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_view, null);

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("受伤反馈验证超时，步骤 " + _step);
                if (Time.time < _due) return;
                switch (_step++)
                {
                    case 0:
                        Check(Vector3.Distance(Visible("HitImpact")[0].transform.position, _impactPosition) < .001f,
                            "敌人移动后，冲击仍停留在原来的世界命中点");
                        Behaviour field = _actor.GetComponent<FieldMinionView>();
                        if (field == null) field = _actor.GetComponentInChildren<EnemySpriteAnimator>();
                        field.enabled = false;
                        Body.flipX = true; Body.color = new Color(.2f, .3f, .4f, .3f);
                        PresentView();
                        SpriteRenderer flash = Visible("HitFlash")[0];
                        Check(flash.flipX && Mathf.Approximately(flash.color.a, .3f), "闪白同步朝向并保留源透明度");
                        Body.enabled = false; PresentView();
                        Check(!Visible("HitFlash").Any(), "隐藏的身体不会被闪白层重新显示");
                        Body.enabled = true; Body.flipX = false; field.enabled = true;
                        PresentView();
                        EnemyHitFeedback.Attach(_actor, Body);
                        Check(All("HitFlash").Length == 1, "重复接入视图不创建重复的闪白层");
                        _progress = Progress(Visible("HitImpact")[0]);
                        _due = Time.time + .12f;
                        break;
                    case 1:
                        Check(Mathf.Approximately(Progress(Visible("HitImpact")[0]), _progress), "实体暂停冻结受伤特效时钟");
                        _actor.Status.SetPaused(false); TimeManager.Instance.SetPaused(true);
                        _due = Time.time + .12f;
                        break;
                    case 2:
                        Check(Mathf.Approximately(Progress(Visible("HitImpact")[0]), _progress), "全局暂停冻结受伤特效时钟");
                        TimeManager.Instance.SetPaused(false);
                        _due = Time.time + .08f;
                        break;
                    case 3:
                        Check(Progress(Visible("HitImpact")[0]) > _progress, "恢复后特效继续推进");
                        _actor.gameObject.SetActive(false);
                        Check(All("HitFlash").All(r => !r.enabled) && All("HitImpact").All(r => !r.enabled), "对象停用立即清理");
                        _actor.gameObject.SetActive(true);
                        Check(!Visible("HitFlash").Any() && !Visible("HitImpact").Any(), "复用对象不残留旧闪光");
                        _actor.RestoreSnapshotState(_snapshot);
                        for (int i = 0; i < 30; i++) { StepActor(.14f); Hit(1); }
                        Check(All("HitImpact").Length == 4, "连续三十次命中保持四个复用槽的上限");
                        Check(All("HitFlash").Length == 1, "连续命中不重复创建覆盖层");
                        _actor.Status.ApplyHealthDelta(1 - _actor.Status.Health);
                        StepActor(.14f); Hit(100);
                        Check(_lastHit.Lethal && _lastHit.Damage == 1, "致命反馈携带剩余实际血量，非过量伤害");
                        Check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("EnemyHitFeedback/Flash").shader), "生产 shader 导入无编译错误");
                        _due = Time.time + .6f;
                        break;
                    case 4:
                        Check(!Visible("HitFlash").Any() && !Visible("HitImpact").Any(), "闪白和冲击按寿命收尾");
                        ValidateWeakPoint();
                        CaptureGallery();
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private static void ValidateWeakPoint()
        {
            EnemyActor clock = MakeActor(EnemyKind.ClockBoss, new Vector3(20, 2));
            float bodyHealth = clock.Status.Health;
            EnemyFeedback received = default;
            int count = 0;
            clock.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.Hit) { received = cue; count++; } };
            clock.WeakPoint.Open(15);
            clock.WeakPoint.ApplyDamage(new DamageInfo { Amount = 5, Attacker = _attacker });
            Check(count == 1 && received.Subject == clock.WeakPoint && received.Damage == 5, "真实弱点通知命中位置和实际伤害");
            Check(clock.Status.Health == bodyHealth, "弱点命中不额外扣身体血量");
            clock.WeakPoint.ApplyDamage(new DamageInfo { Amount = -2, Attacker = _attacker });
            Check(count == 1, "弱点治疗不播放伤害");
            clock.WeakPoint.ApplyDamage(new DamageInfo { Amount = 100, Attacker = _attacker });
            Check(count == 2 && received.Staggered && clock.WeakPoint.Broken, "弱点击破强化反馈");
            clock.WeakPoint.Close();
            clock.WeakPoint.ApplyDamage(new DamageInfo { Amount = 1 });
            Check(count == 2, "关闭的弱点不误播");
            float phaseFloor = clock.Definition.Health * 2f / 3f;
            clock.Status.ApplyHealthDelta(phaseFloor - clock.Status.Health);
            clock.ApplyDamage(new DamageInfo { Amount = 100, Attacker = _attacker });
            Check(count == 2, "Boss 阶段锁血而实际扣血为零时不误播命中");
            UnityEngine.Object.DestroyImmediate(clock.gameObject);
        }

        private static void CaptureGallery()
        {
            Check(GraphicsSettings.currentRenderPipeline != null, "视觉验收使用项目真实 URP 管线");
            _actor.gameObject.SetActive(false);
            var cameraObject = new GameObject("HitFeedbackCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.backgroundColor = new Color(.035f, .05f, .08f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            EnemyActor[] actors =
            {
                MakeActor(EnemyKind.BrickBoss, new Vector3(-5.3f, 2)),
                MakeActor(EnemyKind.PlushSeal, new Vector3(-2.4f, 2)),
                MakeActor(EnemyKind.BatBoss, new Vector3(1.3f, 2)),
                MakeActor(EnemyKind.RabbitBoss, new Vector3(5.8f, 2))
            };
            foreach (EnemyActor actor in actors)
            {
                actor.Status.SetPaused(true);
                actor.Cue("Idle", 1, true);
                foreach (var animator in actor.GetComponentsInChildren<EnemySpriteAnimator>()) animator.SendMessage("LateUpdate");
                var field = actor.GetComponent<FieldMinionView>();
                if (field != null) field.SendMessage("LateUpdate");
            }
            // 预览镜头按真实精灵外形取景，避免宽幅角色/翅膀被固定缩放裁掉。
            var framing = new Bounds(actors[0].transform.position, Vector3.zero);
            foreach (EnemyActor actor in actors)
                foreach (SpriteRenderer renderer in actor.GetComponentsInChildren<SpriteRenderer>())
                    if (renderer.enabled) framing.Encapsulate(renderer.bounds);
            camera.transform.position = new Vector3(framing.center.x, framing.center.y, -10);
            camera.orthographicSize = Mathf.Max(framing.extents.y + .5f, (framing.extents.x + .5f) / (1440f / 800f));
            var target = new RenderTexture(1440, 800, 24);
            var image = new Texture2D(1440, 800, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = target; RenderTexture.active = target;
            camera.Render(); image.ReadPixels(new Rect(0, 0, 1440, 800), 0, 0); image.Apply();
            Color[] baseline = image.GetPixels();
            File.WriteAllBytes(Path.Combine(Folder, "Before.png"), image.EncodeToPNG());
            for (int i = 0; i < actors.Length; i++)
                actors[i].EmitHitFeedback(10, _attacker, i == 2, i == 3);
            float[] times = { 0f, .03f, .06f, .08f, .10f, .12f, .15f, .18f, .21f,
                .22f, .24f, .27f, .30f, .33f, .36f, .39f, .42f };
            int initialWhitePixels = 0;
            foreach (float seconds in times)
            {
                for (int actorIndex = 0; actorIndex < actors.Length; actorIndex++)
                {
                    EnemyActor actor = actors[actorIndex];
                    var feedback = actor.GetComponent<EnemyHitFeedback>();
                    foreach (SpriteRenderer renderer in actor.GetComponentsInChildren<SpriteRenderer>())
                        if (renderer.name == "HitFlash" || renderer.name == "HitImpact")
                        {
                            float duration = renderer.name == "HitFlash"
                                ? feedback.FlashDuration * (actorIndex >= 2 ? 1.3f : 1f)
                                : feedback.ImpactDuration * (actorIndex == 3 ? 1.25f : 1f);
                            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                            properties.SetFloat("_Progress", seconds / duration); renderer.SetPropertyBlock(properties);
                        }
                }
                camera.Render(); image.ReadPixels(new Rect(0, 0, 1440, 800), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Folder, "Hit_" + Mathf.RoundToInt(seconds * 1000).ToString("D3") + "ms.png"), image.EncodeToPNG());
                Color[] pixels = image.GetPixels();
                int changed = pixels.Where((c, i) => Mathf.Abs(c.r - baseline[i].r) + Mathf.Abs(c.g - baseline[i].g)
                    + Mathf.Abs(c.b - baseline[i].b) > .04f).Count();
                int whitePixels = pixels.Count(c => c.r > .85f && c.g > .85f && c.b > .85f);
                if (seconds == 0)
                {
                    Check(changed > 500, "真实 URP 像素确认闪白和冲击可见");
                    initialWhitePixels = whitePixels;
                }
                if (Mathf.Approximately(seconds, .15f))
                    Check(whitePixels > initialWhitePixels * .8f, "受伤 0.15 秒后仍明确保持白色，不提前淡成轮廓");
                if (seconds > .4f) Check(changed == 0, "效果结束像素完全回到原画，无矩形或材质残留");
            }
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Material>("EnemyHitFeedback/Flash").shader), "实际渲染后 shader 无编译错误");
            camera.targetTexture = null; RenderTexture.active = previous; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            foreach (EnemyActor actor in actors) UnityEngine.Object.DestroyImmediate(actor.gameObject);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            _passed++; Debug.Log("PASS: " + name);
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(Pending, false);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "Results.txt"), "Passed: " + _passed + "\n" + (error ?? "Failed: 0"));
            if (error != null) Debug.LogError(error);
            Debug.Log("EnemyHitFeedbackValidation: " + _passed + " passed; " + (error == null ? "0 failed" : error));
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
