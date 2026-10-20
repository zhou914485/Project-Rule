using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;
using Object = UnityEngine.Object;

namespace RuleGame.Editor.Enemy
{
    public static class DemonMinionValidation
    {
        private const string Pending = "RuleGame.DemonMinions";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<Object> Owned = new List<Object>();
        private static readonly List<string> Results = new List<string>();
        private static EnemyActor _actor;
        private static PlayerController _player;
        private static int _passed, _failed;
        public static void RunBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
        }
        [InitializeOnLoadMethod] private static void Resume()
        {
            if (Application.isBatchMode && SessionState.GetBool(Pending, false))
                EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredPlayMode) Run(); };
        }
        private static void Run()
        {
            TimeManager.Initialize(); WorldRegistry.Initialize(); SnapshotRoster.Initialize();
            RuleSystem.Initialize(AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Rules/RuleCatalog.asset"));
            SnapshotSystem.Initialize(RuleSystem.Instance, SnapshotRoster.Instance);
            Physics2D.simulationMode = SimulationMode2D.Script;
            for (int i = 0; i < 24; i++)
            {
                float angle = i * 15f * Mathf.Deg2Rad;
                Case("辐射全周 " + i * 15 + "°", () =>
                {
                    Spawn(EnemyKind.DiscoBall, new Vector2(0f, 5f));
                    _player.TeleportTo((Vector2)_actor.transform.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 4f);
                    float health = Health; Force("Radiate"); Tick(.02f);
                    Check(Health == health - _actor.Definition.Damage, "全周存在无伤缝隙");
                });
            }
            Case("墙、平台阻挡与移开恢复", () =>
            {
                Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f)); _player.TeleportTo(new Vector2(4f, 3f));
                var cover = Box("Cover", new Vector2(2f, 3f), new Vector2(.5f, 2f));
                Force("Radiate"); float health = Health; Tick(.02f);
                Check(Health == health, "隔墙受到辐射伤害");
                var brain = (DiscoBallBrain)_actor.Brain;
                Check(brain.Reach(Vector2.right) < 2f, "光区没有被墙截断");
                cover.transform.position = new Vector2(2f, 6f); Physics2D.SyncTransforms(); Tick(.55f);
                Check(Health == health - _actor.Definition.Damage, "掩体移走后阴影/伤害没有恢复");
                cover.transform.position = new Vector2(2f, 3f); Physics2D.SyncTransforms(); health = Health; Tick(.55f);
                Check(Health == health, "掩体移回后仍受到辐射");
            });
            Case("真实移动平台动态遮挡", MovingCover);
            Case("辐射周期、停火与暂停", () =>
            {
                Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f)); _player.TeleportTo(new Vector2(3f, 3f));
                float health = Health; Tick(_actor.Definition.RadiationWarning - .04f);
                Check(Health == health && _actor.Brain.StateName == "Warning", "预警提前伤害");
                Tick(.08f); Check(Health < health && ((DiscoBallBrain)_actor.Brain).Radiating, "预警后没有辐射");
                float age = _actor.Brain.MechanicsAge; _actor.Status.SetPaused(true); Tick(2f);
                Check(Mathf.Approximately(age, _actor.Brain.MechanicsAge), "暂停没有冻结周期");
                _actor.Status.SetPaused(false); Force("Rest"); health = Health;
                Tick(_actor.Definition.RadiationRest - .04f); Check(Health == health && _actor.Brain.StateName == "Rest", "停火期造成伤害");
                Tick(_actor.Definition.RadiationWarning + .12f); Check(Health < health, "停火后不再恢复辐射");
            });
            Case("辐射受击箱去重与关闭", () =>
            {
                Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f)); _player.TeleportTo(new Vector2(3f, 3f));
                var extra = new GameObject("ExtraHurtbox"); extra.transform.SetParent(_player.transform, false);
                extra.AddComponent<BoxCollider2D>().size = Vector2.one; extra.AddComponent<Hurtbox>();
                Force("Radiate"); float health = Health; Tick(.02f);
                Check(Health == health - _actor.Definition.Damage, "同一实体多个箱子重复伤害");
                health = Health; Tick(.1f); Check(Health == health, "辐射每帧扣血");
                foreach (var box in _player.GetComponentsInChildren<Hurtbox>()) box.SetHittable(false);
                Tick(.6f); Check(Health == health, "关闭受击箱仍受到伤害");
            });
            Case("辐射视觉与阴影网格", Visual);
            Case("陀螺真实碰墙反弹", () =>
            {
                Spawn(EnemyKind.SpinningTop, new Vector2(0f, .62f)); Ground();
                Box("Wall", new Vector2(2.5f, 2f), new Vector2(.4f, 4f));
                float peak = 0f; bool bounced = false;
                for (int i = 0; i < 110; i++) { Physical(); peak = Mathf.Max(peak, _actor.transform.position.x); if (peak > 1f && _actor.Motor.ContactVelocity.x < -1f) bounced = true; }
                Check(bounced && _actor.transform.position.x < peak - .4f, "撞墙后没有真正反弹，或只切了动画");
                Check(_actor.AnimationName == "Spin", "反弹时丢失旋转动作");
            });
            Case("陀螺真实铁箱双方弹开", () =>
            {
                Spawn(EnemyKind.SpinningTop, new Vector2(0f, .62f)); Ground();
                var crate = Box("DynamicCrate", new Vector2(2f, .8f), new Vector2(1f, 1.5f));
                crate.layer = 9; var body = crate.AddComponent<Rigidbody2D>(); body.mass = 2f;
                bool both = false;
                for (int i = 0; i < 90; i++) { Physical(); if (_actor.Motor.ContactVelocity.x < -.1f && body.linearVelocity.x > .1f) both = true; }
                Check(both && crate.transform.position.x > 2.1f, "铁箱或陀螺没有弹开");
            });
            Case("陀螺真实玩家反弹", () =>
            {
                Spawn(EnemyKind.SpinningTop, new Vector2(0f, .62f)); Ground(); _player.TeleportTo(new Vector2(2f, .55f));
                bool bounced = false;
                for (int i = 0; i < 90; i++)
                {
                    _player.SendMessage("FixedUpdate"); Physical();
                    if (_actor.Motor.ContactVelocity.x < -.1f) bounced = true;
                }
                Check(bounced && _player.transform.position.x > 2.15f, "玩家和陀螺没有双方弹开");
            });
            Case("两只陀螺互相弹开", () =>
            {
                Spawn(EnemyKind.SpinningTop, new Vector2(0f, .62f)); Ground();
                var left = _actor;
                var config = Object.Instantiate(left.Definition); Owned.Add(config); config.TopInitialDirection = -1f;
                var right = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DemonMinionSetup.PrefabPath(EnemyKind.SpinningTop)), new Vector3(3f, .62f), Quaternion.identity).GetComponent<EnemyActor>();
                Owned.Add(right.gameObject); right.enabled = false; right.Definition = config; right.SendMessage("Start");
                bool separated = false;
                for (int i = 0; i < 80; i++)
                {
                    left.SendMessage("FixedUpdate"); right.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime);
                    if (left.Motor.ContactVelocity.x < -.3f && right.Motor.ContactVelocity.x > .3f) separated = true;
                }
                Check(separated, "两只陀螺相撞后没有分开");
            });
            Case("陀螺碰撞质量与反向等冲量", () =>
            {
                Spawn(EnemyKind.SpinningTop, new Vector2(0f, 3f));
                var other = Box("HeavyCrate", new Vector2(2f, 3f), Vector2.one).AddComponent<Rigidbody2D>(); other.mass = 4f;
                float mass = _actor.GetComponent<Rigidbody2D>().mass;
                _actor.Brain.OnMotorCollision(other.GetComponent<Collider2D>(), Vector2.left, Vector2.right * 4f);
                float mine = _actor.Motor.CaptureImpulse().x * mass;
                Check(mine < 0f, "陀螺未获得反作用力");
                Physics2D.Simulate(Time.fixedDeltaTime);
                Check(Mathf.Abs(mine + other.mass * other.linearVelocity.x) < .05f, "双方冲量不等大反向");
                var saved = _actor.CaptureSnapshotState(); _actor.Brain.Tick(.5f); _actor.RestoreSnapshotState(saved);
                Check(Mathf.Approximately(_actor.Motor.CaptureImpulse().x * mass, mine), "快照没有恢复反弹冲量");
            });
            foreach (RelationId relation in Enum.GetValues(typeof(RelationId)))
            {
                var r = relation;
                Case("辐射实际伤害规则/" + r, () =>
                {
                    Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f)); _player.TeleportTo(new Vector2(3f, 3f));
                    _player.GetComponent<EntityStatus>().ApplyHealthDelta(-100f); float health = Health;
                    Check(RuleSystem.Instance.Request(AttributeId.Damage, r, RuleSource.Player).Succeeded, "规则请求失败");
                    _actor.Combat.Processor.Tick(); Force("Radiate"); Tick(.02f);
                    float change = r == RelationId.Enhance ? -20f : r == RelationId.Weaken ? -5f : r == RelationId.Revert ? 10f : r == RelationId.Nullify || r == RelationId.Constant ? 0f : -10f;
                    Check(Mathf.Abs(Health - health - change) < .01f, "辐射没有经过共享伤害规则");
                    if (r == RelationId.Constant)
                    {
                        RuleSystem.Instance.Cancel(AttributeId.Damage); _actor.Combat.Processor.Tick();
                        Check(Mathf.Abs(Health - health + 10f) < .01f, "辐射挂账未在规则结束后结算");
                    }
                });
            }
            Case("辐射世界回溯恢复位置与停火周期", () =>
            {
                Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f)); Force("Rest"); Tick(.5f);
                var snapshot = SnapshotSystem.Instance.Capture(); float clock = _actor.Brain.MechanicsAge;
                _actor.transform.position = new Vector2(10f, 3f); _actor.GetComponent<Rigidbody2D>().position = _actor.transform.position;
                Force("Radiate"); Tick(.3f); SnapshotSystem.Instance.Restore(snapshot);
                Check(_actor.Brain.StateName == "Rest" && Mathf.Approximately(clock, _actor.Brain.MechanicsAge)
                    && _actor.GetComponent<Rigidbody2D>().position.x < .001f, "世界回溯没有恢复停火和锚点");
            });
            foreach (var kind in new[] { EnemyKind.DiscoBall, EnemyKind.SpinningTop })
            {
                foreach (AttributeId attribute in Enum.GetValues(typeof(AttributeId)))
                    foreach (RelationId relation in Enum.GetValues(typeof(RelationId)))
                    {
                        var k = kind; var a = attribute; var r = relation;
                        Case(k + "/" + a + "/" + r, () => RuleCase(k, a, r));
                    }
                Case(kind + " 自动绑定、暂停、快照与死亡", () => Lifecycle(kind));
            }
            Directory.CreateDirectory("Logs/DemonMinions");
            File.WriteAllText("Logs/DemonMinions/Results.txt", $"Passed: {_passed}\nFailed: {_failed}\n" + string.Join("\n", Results));
            SessionState.SetBool(Pending, false); EditorApplication.Exit(_failed == 0 ? 0 : 1);
        }
        private static float Health => _player.GetComponent<EntityStatus>().Health;
        private static void Spawn(EnemyKind kind, Vector2 position)
        {
            var combat = new GameObject("Combat").AddComponent<CombatSystem>(); Owned.Add(combat.gameObject);
            _player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab"), new Vector3(12f, .55f), Quaternion.identity).GetComponent<PlayerController>();
            Owned.Add(_player.gameObject); _player.enabled = false; _player.GetComponent<EntityStatus>().InitializeVitals(1000f, 100f);
            var serialized = new SerializedObject(_player.GetComponent<EntityDamageable>());
            serialized.FindProperty("_invincibleSeconds").floatValue = 0f; serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DemonMinionSetup.PrefabPath(kind));
            var definition = Object.Instantiate(prefab.GetComponent<EnemyActor>().Definition); Owned.Add(definition);
            _actor = Object.Instantiate(prefab, position, Quaternion.identity).GetComponent<EnemyActor>(); Owned.Add(_actor.gameObject);
            _actor.enabled = false; _actor.Definition = definition; _actor.SendMessage("Start");
            Check(_actor.Target == _player.transform && _actor.Combat != null, "预制体不能自动绑定玩家/战斗服务");
            _actor.GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.None; Physics2D.SyncTransforms();
        }
        private static void MovingCover()
        {
            Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f)); _player.TeleportTo(new Vector2(4f, 3f));
            var cover = Box("MovingCover", new Vector2(2f, 3f), new Vector2(.5f, 2f));
            cover.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var platform = cover.AddComponent<MovingPlatform>();
            var serialized = new SerializedObject(platform);
            serialized.FindProperty("_start").vector2Value = new Vector2(2f, 3f);
            serialized.FindProperty("_end").vector2Value = new Vector2(2f, 7f);
            serialized.FindProperty("_speed").floatValue = 3f;
            serialized.ApplyModifiedPropertiesWithoutUndo(); platform.SendMessage("OnEnable"); platform.enabled = false;
            Force("Radiate"); float health = Health; Tick(.02f); Check(Health == health, "平台起点没有遮住辐射");
            for (int i = 0; i < 60; i++) { platform.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); _actor.SendMessage("FixedUpdate"); }
            Check(cover.transform.position.y > 5f && Health < health, "真实平台移动没有改变辐射遮挡");
        }
        private static void RuleCase(EnemyKind kind, AttributeId attribute, RelationId relation)
        {
            Spawn(kind, new Vector2(0f, 3f)); Ground();
            if (attribute == AttributeId.Time && relation == RelationId.Revert) SnapshotSystem.Instance.CaptureToBuffer();
            Check(RuleSystem.Instance.Request(attribute, relation, RuleSource.Player).Succeeded, "规则请求失败");
            Vector2 before = _actor.transform.position;
            for (int i = 0; i < 30; i++) Physical();
            Check(_actor.GetComponent<RuleAffectedBody>().IsRegistered, "没有注册规则实体");
            Check(!float.IsNaN(_actor.Motor.ContactVelocity.x) && _actor.Brain.StateName != null, "规则导致状态失效");
            if (kind == EnemyKind.DiscoBall) Check(Vector2.Distance(before, _actor.transform.position) < .001f, "固定悬挂球被重力/冲量拖走");
            if (attribute == AttributeId.Time && relation == RelationId.Nullify)
                Check(_actor.Brain.MechanicsAge < .001f && _actor.AnimationElapsed > .1f, "机制计时与动作没有分开");
            if (attribute == AttributeId.Mass)
            {
                float baseMass = _actor.GetComponent<RuleAffectedBody>().BaseMass;
                float expected = relation == RelationId.Enhance ? baseMass * 3f : relation == RelationId.Weaken ? baseMass * .3f
                    : relation == RelationId.Nullify ? baseMass * .05f : relation == RelationId.Revert ? 1f / baseMass : baseMass;
                Check(Mathf.Abs(_actor.GetComponent<Rigidbody2D>().mass - expected) < .001f, "质量规则没有生效");
            }
            var snapshot = _actor.CaptureSnapshotState(); float clock = _actor.Brain.MechanicsAge;
            Tick(.1f); _actor.RestoreSnapshotState(snapshot);
            Check(Mathf.Approximately(clock, _actor.Brain.MechanicsAge), "规则期间快照计时未恢复");
        }
        private static void Lifecycle(EnemyKind kind)
        {
            Spawn(kind, new Vector2(0f, 3f)); Tick(.3f); float clock = _actor.Brain.SimulationAge;
            _actor.Status.SetPaused(true); Physical(); Check(Mathf.Approximately(clock, _actor.Brain.SimulationAge), "暂停仍推进");
            _actor.Status.SetPaused(false); var snapshot = _actor.CaptureSnapshotState();
            Tick(.5f); _actor.RestoreSnapshotState(snapshot); Check(Mathf.Approximately(clock, _actor.Brain.SimulationAge), "快照没有恢复");
            _actor.ApplyDamage(new DamageInfo { Amount = 1000f }); Tick(.02f);
            Check(_actor.Defeated && !(_actor.Brain is DiscoBallBrain disco && disco.Radiating), "死亡后仍攻击");
            Tick(.6f); Check(!_actor.gameObject.activeSelf, "死亡未收尾");
        }
        private static void Visual()
        {
            Spawn(EnemyKind.DiscoBall, new Vector2(0f, 3f));
            Box("Cover", new Vector2(2f, 3f), new Vector2(.5f, 2f)); Force("Radiate"); Tick(.02f);
            var view = _actor.GetComponentInChildren<DiscoRadiationView>(); view.SendMessage("LateUpdate");
            var mesh = view.GetComponent<MeshFilter>().sharedMesh;
            Check(mesh.vertexCount >= 258 && mesh.triangles.Length >= 768, "辐射不是连续全周网格");
            bool clipped = false, full = false;
            foreach (var vertex in mesh.vertices) { if (vertex.x > 1.7f && vertex.x < 2f && Mathf.Abs(vertex.y) < .1f) clipped = true; if (vertex.x < -7.9f) full = true; }
            Check(clipped && full, "可见光区没有形成掩体阴影");
            Check(view.GetComponent<MeshRenderer>().sharedMaterial.shader.isSupported, "辐射 Shader 不支持当前管线");
            var cover = GameObject.Find("Cover").AddComponent<SpriteRenderer>();
            cover.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            cover.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            cover.drawMode = SpriteDrawMode.Tiled; cover.size = new Vector2(.5f, 2f); cover.color = Color.gray; cover.sortingOrder = 4;
            _actor.GetComponentInChildren<EnemySpriteAnimator>().SendMessage("LateUpdate");
            var camera = new GameObject("PreviewCamera").AddComponent<Camera>(); Owned.Add(camera.gameObject);
            camera.orthographic = true; camera.orthographicSize = 8.5f; camera.transform.position = new Vector3(0f, 3f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .09f, .13f);
            Directory.CreateDirectory("Logs/DemonMinions");
            var target = new RenderTexture(1200, 800, 24); var previous = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1200, 800, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); pixels.Apply();
            File.WriteAllBytes("Logs/DemonMinions/DiscoRadiation.png", pixels.EncodeToPNG());
            var colors = pixels.GetPixels32(); int red = 0, green = 0, blue = 0;
            foreach (var color in colors)
            {
                if (color.r > 60 && color.r > color.g * 1.3f && color.r > color.b * 1.3f) red++;
                if (color.g > 60 && color.g > color.r * 1.3f && color.g > color.b * 1.3f) green++;
                if (color.b > 60 && color.b > color.r * 1.3f && color.b > color.g * 1.3f) blue++;
            }
            Check(red > 1000 && green > 1000 && blue > 1000, $"辐射缺少实际彩色渲染：R={red}, G={green}, B={blue}");
            Vector3 shadowPixel = camera.WorldToScreenPoint(new Vector3(6f, 3f));
            Vector3 clearPixel = camera.WorldToScreenPoint(new Vector3(10f, 7f));
            Color shadow = pixels.GetPixel((int)shadowPixel.x, (int)shadowPixel.y);
            Color clear = pixels.GetPixel((int)clearPixel.x, (int)clearPixel.y);
            Check(Mathf.Abs(shadow.r - clear.r) + Mathf.Abs(shadow.g - clear.g) + Mathf.Abs(shadow.b - clear.b) < .02f,
                "彩色光效越过了掩体的安全阴影");
            // 重复 GPU 渲染允许一个 8-bit 色阶的量化误差，不容许图案或光束发生位移。
            byte[] first = pixels.GetRawTextureData<byte>().ToArray();
            _actor.Status.SetPaused(true); Tick(.5f); view.SendMessage("LateUpdate"); camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); pixels.Apply();
            byte[] paused = pixels.GetRawTextureData<byte>().ToArray(); bool identical = first.Length == paused.Length;
            for (int i = 0; identical && i < first.Length; i++) identical = Mathf.Abs(first[i] - paused[i]) <= 1;
            if (!identical) File.WriteAllBytes("Logs/DemonMinions/PausedComparison.png", pixels.EncodeToPNG());
            Check(identical, "暂停后 Shader 仍在自行滚动"); _actor.Status.SetPaused(false);
            Directory.CreateDirectory("Logs/DemonMinions/DiscoFrames");
            for (int i = 0; i < 32; i++)
            {
                if (i > 0) Tick(.1f);
                _actor.GetComponentInChildren<EnemySpriteAnimator>().SendMessage("LateUpdate");
                view.SendMessage("LateUpdate"); camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); pixels.Apply();
                File.WriteAllBytes($"Logs/DemonMinions/DiscoFrames/Frame{i:000}.png", pixels.EncodeToPNG());
            }
            byte[] animated = pixels.GetRawTextureData<byte>().ToArray(); int changed = 0;
            for (int i = 0; i < first.Length; i++) if (Mathf.Abs(first[i] - animated[i]) > 8) changed++;
            Check(changed > 10000, "恢复运行后彩色图案没有继续流动");
            camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
            Force("Rest"); view.SendMessage("LateUpdate");
            Check(!view.GetComponent<MeshRenderer>().enabled, "停火时光区没有消失");
        }
        private static GameObject Box(string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name) { layer = 8 }; Owned.Add(go); go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size; Physics2D.SyncTransforms(); return go;
        }
        private static void Ground() => Box("Ground", new Vector2(0f, -.5f), new Vector2(100f, 1f));
        private static void Force(string state) => typeof(EnemyBrain).GetMethod("Go", Hidden).Invoke(_actor.Brain, new object[] { state });
        private static void Tick(float seconds) { for (int i = 0; i < Mathf.CeilToInt(seconds / .02f); i++) _actor.Brain.Tick(.02f); }
        private static void Physical() { _actor.SendMessage("FixedUpdate"); Physics2D.Simulate(Time.fixedDeltaTime); Physics2D.SyncTransforms(); }
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private static void Case(string name, Action test)
        {
            try { test(); _passed++; Results.Add("PASS " + name); }
            catch (Exception error) { _failed++; Results.Add("FAIL " + name + ": " + error); }
            finally
            {
                RuleSystem.Instance.ClearAll();
                for (int i = Owned.Count - 1; i >= 0; i--) if (Owned[i] != null) Object.DestroyImmediate(Owned[i]);
                Owned.Clear(); Physics2D.SyncTransforms(); SnapshotSystem.Instance.ClearBuffer();
            }
        }
    }
}
