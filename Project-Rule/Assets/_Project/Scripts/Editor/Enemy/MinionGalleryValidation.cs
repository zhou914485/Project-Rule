using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Infrastructure;
using RuleGame.Development.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using RuleGame.Presentation.DemonCastle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Enemy
{
    /// <summary>检查真实预制体美术接线，不创建或替换小怪行为。</summary>
    public static class MinionGalleryValidation
    {
        private const string Pending = "RuleGame.MinionGalleryValidation";
        private static MinionGalleryControls _controls;
        private static int _stage, _passed;
        private static float _due, _health, _fuse, _age;
        private static EnemyProjectile _ice, _birdShot;
        private static Sprite _savedSprite;
        private static object _bearState, _iceState;
        private static Snapshot _initial, _fireworkSnapshot;
        private static double _deadline;
        private static readonly List<string> Errors = new List<string>();
        private static RenderTexture _preview, _previousPanelTarget;
        private static PanelSettings _panelSettings;
        private static string Folder => Path.GetFullPath("Logs/MinionGallery");
        private static SpriteRenderer Body(EnemyActor actor) => actor.GetComponentInChildren<EnemySpriteAnimator>().GetComponent<SpriteRenderer>();
        private static EnemySpriteSequence Sequence(EnemyActor actor, string action)
            => Array.Find(actor.GetComponentInChildren<EnemySpriteAnimator>().AnimationSet.Sequences, sequence => sequence.Action == action);
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在工程副本运行。");
            MinionGallerySceneSetup.Create();
            EditorSceneManager.OpenScene(MinionGallerySceneSetup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                Directory.CreateDirectory(Folder); _deadline = EditorApplication.timeSinceStartup + 80;
                _due = Time.time + 1f;
                Application.logMessageReceived += Log; EditorApplication.update += Tick;
            };
        }
        private static void Log(string condition, string stack, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search")) Errors.Add(condition + "\n" + stack);
        }
        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("小怪资源场景验证超时");
                if (Time.time < _due) return;
                switch (_stage++)
                {
                    case 0:
                        _controls = UnityEngine.Object.FindAnyObjectByType<MinionGalleryControls>();
                        Check(_controls != null && _controls.Stations.Length == 8 && _controls.Enemies.Length == 7, "七种已有小怪及烟花机关已接线");
                        foreach (var actor in _controls.Enemies)
                        {
                            Check(actor.Brain != null && actor.Target == _controls.Player.transform && actor.Combat != null, actor.Definition.Kind + " 保持原有行为和场景引用");
                            Check(Body(actor).sprite != null && actor.GetComponentInChildren<Hurtbox>() != null, actor.Definition.Kind + " 正式美术及受击箱完整");
                        }
                        Check(_controls.GetComponent<UIDocument>().rootVisualElement.Q<DropdownField>("stationChooser").choices.Count == 8, "测试面板可选择八个区域");
                        for (int i = 0; i < 8; i++)
                        {
                            _controls.SelectStation(i);
                            Check(Mathf.Abs(_controls.Player.transform.position.x - _controls.Stations[i].x) < .01f, "区域跳转 " + (i + 1));
                        }
                        ValidateDeliveredFrames();
                        Check(_controls.Firework.GetComponentsInChildren<SpriteRenderer>().Count(renderer => renderer.enabled) == 1, "烟花待机没有叠加旧外观");
                        _initial = SnapshotSystem.Instance.Capture();
                        _controls.SetInvincible(false); _controls.SelectStation(5);
                        _controls.Player.TeleportTo(_controls.Enemies[5].transform.position + Vector3.left * 1.05f);
                        _due = Time.time + 1.4f;
                        break;
                    case 1:
                        var bear = _controls.Enemies[5];
                        Check(bear.Brain.StateName == "Hug" && _controls.Player.MovementHeld, $"普通受伤模式下保持拥抱：{bear.Brain.StateName}, held={_controls.Player.MovementHeld}, distance={Vector2.Distance(bear.transform.position, _controls.Player.transform.position):0.00}, health={_controls.Player.GetComponent<EntityStatus>().Health}");
                        Check(_controls.Player.GetComponent<EntityStatus>().Health < 100f, "真实拥抱伤害生效且不把玩家推离拥抱");
                        Check(Sequence(bear, "Hug").Frames.Length == 4 && Sequence(bear, "Dead").Frames.Length == 9, "拥抱与死亡消散正确拆分");
                        _bearState = bear.CaptureSnapshotState(); _due = Time.time + .8f;
                        break;
                    case 2:
                        var heldBear = _controls.Enemies[5];
                        var embrace = Sequence(heldBear, "Hug");
                        Check(Body(heldBear).sprite == embrace.Frames[embrace.Frames.Length - 1], "持续拥抱停在抱住帧，不提前消散");
                        CaptureScene("BearHug");
                        heldBear.ApplyDamage(new DamageInfo { Amount = 1, Attacker = _controls.Player.transform });
                        Check(!_controls.Player.MovementHeld, "攻击仍可挣脱拥抱");
                        heldBear.RestoreSnapshotState(_bearState);
                        Check(_controls.Player.MovementHeld, "回溯到拥抱恢复原有移动限制");
                        heldBear.gameObject.SetActive(false);
                        Check(!_controls.Player.MovementHeld, "停用抱抱熊不残留移动限制");
                        SnapshotSystem.Instance.Restore(_initial);
                        _controls.SelectStation(6);
                        _controls.Player.TeleportTo(_controls.Enemies[6].transform.position + Vector3.left * 3f);
                        _due = Time.time + .3f;
                        break;
                    case 3:
                        var seal = _controls.Enemies[6];
                        Check(seal.HasTarget, "等待海豹原有索敌后再检查投射物表现");
                        seal.Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) _ice = (EnemyProjectile)cue.Subject; };
                        seal.Shoot(-seal.Motor.Up, 3f, 1f, .3f, origin: (Vector2)seal.transform.position + seal.Motor.Up * 3f);
                        _due = Time.time + .15f;
                        break;
                    case 4:
                        Check(_ice != null && _ice.gameObject.activeInHierarchy && _ice.GetComponent<EnemyProjectileView>() != null, "海豹沿用真实弹池发射冰块");
                        var iceBody = _ice.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>();
                        var profile = _controls.Enemies[6].GetComponent<EnemyVfxPresenter>().Profile;
                        Check(profile.ProjectileAnimations[0].Frames.Contains(iceBody.sprite) && iceBody.color == Color.white, "冰块使用交付双帧和原色");
                        Check(Mathf.Approximately(_ice.Radius, .3f) && Mathf.Approximately(_ice.transform.localScale.x, .6f), "美术不改变冰块碰撞半径或根节点比例");
                        _iceState = _ice.CaptureSnapshotState(); _age = _ice.Age; _savedSprite = iceBody.sprite;
                        _controls.SetPaused(true); _due = Time.time + .25f;
                        break;
                    case 5:
                        Check(_ice.Age == _age && _ice.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>().sprite == _savedSprite, "全局暂停冻结冰块运动与帧进度");
                        _controls.SetPaused(false); _ice.RestoreSnapshotState(_iceState); _ice.GetComponent<EnemyProjectileView>().SendMessage("LateUpdate");
                        Check(_ice.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>().sprite == _savedSprite, "冰块快照恢复动画帧");
                        _ice.gameObject.SetActive(false);
                        _controls.SetInvincible(false); _controls.SelectStation(7);
                        _fireworkSnapshot = SnapshotSystem.Instance.Capture();
                        var button = _controls.Firework.transform.parent.GetComponentInChildren<CitySwitch>();
                        Check(button.FollowMotor == _controls.Firework, "保留魔王城烟花按钮接线");
                        _controls.Player.TeleportTo(new Vector2(button.transform.position.x, button.GetComponent<Collider2D>().bounds.max.y + .5f));
                        _due = Time.time + .25f;
                        break;
                    case 6:
                        Check(_controls.Firework.Armed, "玩家仍可踩按钮点火");
                        var fuseView = _controls.Firework.GetComponentInChildren<DemonCastleSpriteView>();
                        fuseView.SendMessage("LateUpdate");
                        Check(fuseView.Target.enabled && fuseView.Frames.Contains(fuseView.Target.sprite), "点火阶段显示交付烟花原画");
                        Check(_controls.Firework.GetComponentsInChildren<SpriteRenderer>().Count(renderer => renderer.enabled) == 1, "点火只显示一套烟花外观");
                        CaptureScene("FireworkArmed");
                        _fuse = _controls.Firework.Remaining; _savedSprite = fuseView.Target.sprite;
                        _controls.SetPaused(true); _due = Time.time + .25f;
                        break;
                    case 7:
                        Check(_controls.Firework.Remaining == _fuse && _controls.Firework.GetComponentInChildren<DemonCastleSpriteView>().Target.sprite == _savedSprite, "暂停冻结烟花引信及原画");
                        _controls.SetPaused(false); _controls.Player.enabled = false;
                        _controls.Player.TeleportTo(_controls.Firework.transform.position + Vector3.right);
                        Physics2D.SyncTransforms(); _health = _controls.Player.GetComponent<EntityStatus>().Health;
                        _due = Time.time + 1.4f;
                        break;
                    case 8:
                        Check(_controls.Firework.Spent && _controls.Player.GetComponent<EntityStatus>().Health < _health, "保持既有烟花爆炸伤害");
                        SnapshotSystem.Instance.Restore(_fireworkSnapshot); _controls.Player.enabled = true;
                        var restored = _controls.Firework.GetComponentInChildren<DemonCastleSpriteView>(); restored.SendMessage("LateUpdate");
                        Check(!_controls.Firework.Armed && !_controls.Firework.Spent && restored.Target.enabled, "回溯恢复烟花待点火状态");
                        Check(_controls.Firework.GetComponentsInChildren<SpriteRenderer>().Count(renderer => renderer.enabled) == 1, "待机只显示一套完整烟花，不叠旧箱体");
                        _controls.SelectStation(6); _due = Time.time + .5f;
                        break;
                    case 9:
                        _controls.SelectStation(3);
                        _controls.Player.TeleportTo(_controls.Enemies[3].transform.position + Vector3.left * 3f);
                        _controls.Enemies[3].Feedback += cue => { if (cue.Kind == EnemyFeedbackKind.ProjectileSpawn) _birdShot = (EnemyProjectile)cue.Subject; };
                        _due = Time.time + .3f;
                        break;
                    case 10:
                        var bird = _controls.Enemies[3];
                        Check(bird.HasTarget, "毛绒鸟索敌正常");
                        bird.Shoot(Vector2.right, 3f, origin: (Vector2)bird.transform.position + Vector2.up);
                        _due = Time.time + .2f;
                        break;
                    case 11:
                        var firingBird = _controls.Enemies[3];
                        Check(firingBird.Brain.StateName == "Attack" && Body(firingBird).enabled, "小鸟真实攻击期间身体持续显示");
                        Check(_birdShot != null && _birdShot.gameObject.activeInHierarchy, "小鸟沿用现有弹池发射");
                        var light = _birdShot.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>();
                        Check(Sequence(firingBird, "ProjectileLight").Frames.Contains(light.sprite), "交付光球帧单独用于飞弹");
                        Check(light.color == Color.white && Mathf.Approximately(_birdShot.Radius, .22f), "飞弹保持原色和原碰撞半径");
                        var ballTexture = new Texture2D(2, 2);
                        ballTexture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(light.sprite)));
                        bool visibleBall = ballTexture.GetPixels32().Count(pixel => pixel.a > 128) > ballTexture.width * ballTexture.height * .1f;
                        UnityEngine.Object.DestroyImmediate(ballTexture);
                        Check(visibleBall, "飞弹原图有效区域足够大，不因整张角色画布缩成光点");
                        CaptureScene("BirdAttack");
                        _controls.SelectStation(4); _due = Time.time + .3f;
                        break;
                    case 12:
                        CaptureScene("BatFlight");
                        _controls.SelectStation(6); _due = Time.time + .5f;
                        break;
                    case 13:
                        _preview = new RenderTexture(1600, 900, 24); Camera.main.targetTexture = _preview;
                        _panelSettings = _controls.GetComponent<UIDocument>().panelSettings;
                        _previousPanelTarget = _panelSettings.targetTexture; _panelSettings.targetTexture = _preview;
                        Camera.main.Render(); _due = Time.time + .5f;
                        break;
                    default:
                        var previous = RenderTexture.active; RenderTexture.active = _preview;
                        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
                        File.WriteAllBytes(Path.Combine(Folder, "Gallery.png"), image.EncodeToPNG());
                        RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image);
                        Check(Errors.Count == 0, "场景运行无新增错误：" + string.Join("\n", Errors));
                        Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error); }
        }
        private static void ValidateDeliveredFrames()
        {
            foreach (var sprite in Sequence(_controls.Enemies[3], "Attack").Frames)
            {
                var texture = new Texture2D(2, 2);
                texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                var pivot = importer.spritePivot;
                bool opaque = texture.GetPixel(Mathf.RoundToInt(pivot.x * (texture.width - 1)), Mathf.RoundToInt(pivot.y * (texture.height - 1))).a > .5f;
                UnityEngine.Object.DestroyImmediate(texture);
                Check(opaque, "攻击序列的每帧都保留鸟身：" + sprite.name);
            }
            var heights = new List<float>();
            foreach (var sprite in Sequence(_controls.Enemies[4], "Walk").Frames)
            {
                var texture = new Texture2D(2, 2);
                texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
                var pixels = texture.GetPixels32(); int bottom = texture.height, top = -1;
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                        if (pixels[y * texture.width + x].a > 128) { bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                heights.Add((top - bottom + 1) / importer.spritePixelsPerUnit);
                UnityEngine.Object.DestroyImmediate(texture);
            }
            Check(heights.Max() - heights.Min() > .1f, "蝙蝠日常飞行动作有实际拍翼幅度，不循环收翼俯冲末帧");
            Check(_controls.Enemies[4].Brain.StateName == "Walk" || _controls.Enemies[4].Brain.StateName == "Idle", "蝙蝠按站位移动或悬停，没有新增俯冲行为");
        }
        private static void CaptureScene(string name)
        {
            var camera = Camera.main;
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var target = new RenderTexture(1600, 900, 24);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        }
        private static void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
            _passed++; Debug.Log("[MinionGalleryValidation] PASS " + message);
        }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            if (_preview != null)
            {
                if (Camera.main != null) Camera.main.targetTexture = null;
                if (_panelSettings != null) _panelSettings.targetTexture = _previousPanelTarget;
                UnityEngine.Object.DestroyImmediate(_preview);
            }
            SessionState.SetBool(Pending, false); Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, "Results.txt"), $"Passed: {_passed}\nFailed: {(error == null ? 0 : 1)}\n{error}");
            if (error != null) Debug.LogException(error);
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
