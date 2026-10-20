using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Time;
using RuleGame.Presentation.Enemy;

namespace RuleGame.Editor.Enemy
{
    /// <summary>只在工程副本中验证四 Boss 的真实 Prefab、攻击通知和表现几何。</summary>
    public static class BossVfxAlignmentValidation
    {
        private const string Pending = "RuleGame.BossVfxAlignment";
        private static readonly List<string> Results = new List<string>();
        private static int _passed, _failed;
        private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/BossVfxAlignment"));
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在可丢弃的工程副本运行。");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }
        public static void RunCalibratedBatch()
        {
            EnemyVfxSetup.CalibrateClipBounds();
            RunBatch();
        }
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!Application.isBatchMode || !SessionState.GetBool(Pending, false)) return;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Run();
            };
        }
        private static EnemyActor Make(EnemyKind kind)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/" + kind + ".prefab");
            var actor = UnityEngine.Object.Instantiate(prefab).GetComponent<EnemyActor>();
            actor.enabled = false; actor.transform.position = new Vector3(2, 3);
            actor.Target = new GameObject("Target").transform; actor.Target.position = new Vector3(20, 3);
            actor.SendMessage("Start");
            return actor;
        }
        private static void Case(string name, Action test)
        {
            try { test(); _passed++; Results.Add("PASS: " + name); }
            catch (Exception e) { _failed++; Results.Add("FAIL: " + name + " — " + e.Message); }
            finally
            {
                foreach(var shot in UnityEngine.Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None)) shot.gameObject.SetActive(false);
                foreach(var beam in UnityEngine.Object.FindObjectsByType<EnemyBeam>(FindObjectsSortMode.None)) beam.gameObject.SetActive(false);
                foreach(var actor in UnityEngine.Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None)) UnityEngine.Object.DestroyImmediate(actor.gameObject);
            }
        }
        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        private static void Near(Vector2 a, Vector2 b, string message)
        { Check(Vector2.Distance(a, b) < .025f, message + " actual=" + a + " expected=" + b); }
        private static void Late(Component component)
            => component.GetType().GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
        private static void Field(object obj, string name, object value)
            => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
        private static Bounds ClipBounds(EnemyVfxPresenter presenter, EnemyVfxClip clip)
        {
            var slots = (Array)typeof(EnemyVfxPresenter).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(presenter);
            foreach (object slot in slots)
            {
                if (slot == null || slot.GetType().GetField("Clip").GetValue(slot) != clip) continue;
                var renderer = (SpriteRenderer)slot.GetType().GetField("Renderer").GetValue(slot);
                Rect rect = Content(clip);
                if(renderer.flipX) rect.x = 1f - rect.xMax;
                // The authored clip's union of visible pixels, in its centered one-unit tile.
                Vector3 min = renderer.transform.TransformPoint(new Vector3(rect.xMin - .5f, rect.yMin - .5f));
                Vector3 max = renderer.transform.TransformPoint(new Vector3(rect.xMax - .5f, rect.yMax - .5f));
                return new Bounds((min + max) * .5f, new Vector3(Mathf.Abs(max.x-min.x), Mathf.Abs(max.y-min.y)));
            }
            throw new InvalidOperationException("未播放预期片段 " + clip.name);
        }
        private static Rect Content(EnemyVfxClip clip)
        {
            var image = new Texture2D(2, 2); image.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(clip.Atlas)));
            int w = image.width / clip.Grid.x, h = image.height / clip.Grid.y;
            Color32[] pixels = image.GetPixels32(); int x0=w, y0=h, x1=0, y1=0;
            for (int i=clip.FirstFrame;i<clip.FrameCount;i++)
                for (int y=0;y<h;y++) for (int x=0;x<w;x++)
                    if (pixels[((clip.Grid.y-1-i/clip.Grid.x)*h+y)*image.width+i%clip.Grid.x*w+x].a >= 40)
                    { x0=Mathf.Min(x0,x); y0=Mathf.Min(y0,y); x1=Mathf.Max(x1,x+1); y1=Mathf.Max(y1,y+1); }
            UnityEngine.Object.DestroyImmediate(image);
            return Rect.MinMaxRect(x0/(float)w,y0/(float)h,x1/(float)w,y1/(float)h);
        }
        private static void Capture(EnemyActor actor, Vector2 center, Vector2 size, string name, bool showDamageBounds = true)
        {
            var presenter=actor.GetComponent<EnemyVfxPresenter>();
            var slots=(Array)typeof(EnemyVfxPresenter).GetField("_slots",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(presenter);
            foreach(object slot in slots)
            {
                if(slot==null) continue;
                var clip=(EnemyVfxClip)slot.GetType().GetField("Clip").GetValue(slot);
                if(clip==null) continue;
                var renderer=(SpriteRenderer)slot.GetType().GetField("Renderer").GetValue(slot);
                renderer.sprite=clip.Frame(name.StartsWith("BrickNormalHammer")
                    ? .18f*(float)slot.GetType().GetField("Speed").GetValue(slot)
                    : clip==presenter.Profile.Cast?.25f:.12f);
            }
            var outline=new GameObject("DamageBounds"); var line=outline.AddComponent<LineRenderer>();
            line.sharedMaterial=new Material(Shader.Find("Sprites/Default")); line.useWorldSpace=true;
            line.positionCount=4; line.loop=true; line.startWidth=line.endWidth=.025f; line.sortingOrder=50;
            line.startColor=line.endColor=new Color(.1f,1f,.7f); line.enabled=showDamageBounds;
            line.SetPositions(new[] { (Vector3)(center-size*.5f),new Vector3(center.x+size.x*.5f,center.y-size.y*.5f),
                (Vector3)(center+size*.5f),new Vector3(center.x-size.x*.5f,center.y+size.y*.5f) });
            var go=new GameObject("ValidationCamera"); var camera=go.AddComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=4.5f; camera.backgroundColor=new Color(.035f,.05f,.08f);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.transform.position=new Vector3(center.x,center.y,-10);
            var target=new RenderTexture(1280,720,24); var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active;
            try
            {
                camera.targetTexture=target; RenderTexture.active=target; camera.Render();
                image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null; RenderTexture.active=previous; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(line.sharedMaterial);
                UnityEngine.Object.DestroyImmediate(outline);
            }
        }
        private static SpriteRenderer ClipRenderer(EnemyVfxPresenter presenter, EnemyVfxClip clip)
        {
            var slots=(Array)typeof(EnemyVfxPresenter).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presenter);
            foreach(object slot in slots)
                if(slot!=null && slot.GetType().GetField("Clip").GetValue(slot)==clip)
                    return (SpriteRenderer)slot.GetType().GetField("Renderer").GetValue(slot);
            throw new InvalidOperationException("找不到片段 "+clip.name);
        }
        private static int ClipCount(EnemyVfxPresenter presenter, EnemyVfxClip clip)
        {
            int count=0;
            var slots=(Array)typeof(EnemyVfxPresenter).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(presenter);
            foreach(object slot in slots)
                if(slot!=null && slot.GetType().GetField("Clip").GetValue(slot)==clip) count++;
            return count;
        }
        private sealed class ValidationFloor : IDisposable
        {
            private readonly GameObject _root;
            private readonly Sprite _sprite;
            private readonly Material _material;
            public ValidationFloor(float y)
            {
                _root=new GameObject("ValidationFloor"); _root.layer=8; _root.transform.position=new Vector3(0,y-.5f);
                var collider=_root.AddComponent<BoxCollider2D>(); collider.size=new Vector2(40,1);
                var art=new GameObject("FloorArt"); art.transform.SetParent(_root.transform,false); art.transform.localScale=new Vector3(40,1,1);
                var renderer=art.AddComponent<SpriteRenderer>();
                _sprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,1,1),Vector2.one*.5f,1);
                _material=new Material(Shader.Find("Sprites/Default"));
                renderer.sprite=_sprite; renderer.sharedMaterial=_material; renderer.color=new Color(.16f,.19f,.22f);
                Physics2D.SyncTransforms();
            }
            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(_root); UnityEngine.Object.DestroyImmediate(_sprite); UnityEngine.Object.DestroyImmediate(_material);
            }
        }
        private static Bounds MeshBounds(Sprite sprite)
        {
            Vector2[] vertices=sprite.vertices;
            var bounds=new Bounds(vertices[0],Vector3.zero);
            foreach(Vector2 vertex in vertices) bounds.Encapsulate(vertex);
            return bounds;
        }
        private static void CaptureProjectile(EnemyProjectile shot, SpriteRenderer body, Bounds visible, float authoredScale, string name)
        {
            var objects=new List<GameObject>();
            for(int column=0;column<3;column++)
            {
                var go=new GameObject("ProjectileComparison"); objects.Add(go); go.layer=31;
                var sprite=go.AddComponent<SpriteRenderer>(); sprite.sprite=body.sprite;
                sprite.sharedMaterial=body.sharedMaterial; sprite.color=body.color;
                float scale=column==1?1f/Mathf.Max(body.sprite.bounds.size.x,body.sprite.bounds.size.y):authoredScale;
                Vector3 offset=column==0?Vector3.zero:-(column==1?body.sprite.bounds.center:visible.center)*scale;
                Vector3 center=new Vector3((column-1)*1.2f,0);
                go.transform.localScale=Vector3.one*shot.Radius*2f*scale;
                go.transform.position=center+offset*shot.Radius*2f;
                var circle=new GameObject("UnchangedHitRadius"); circle.layer=31; objects.Add(circle);
                var line=circle.AddComponent<LineRenderer>(); line.sharedMaterial=new Material(Shader.Find("Sprites/Default"));
                line.positionCount=64; line.loop=true; line.useWorldSpace=true;
                line.startWidth=line.endWidth=.005f; line.startColor=line.endColor=new Color(.1f,.8f,.6f);
                for(int i=0;i<64;i++) line.SetPosition(i,center+new Vector3(Mathf.Cos(i*Mathf.PI/32),Mathf.Sin(i*Mathf.PI/32))*shot.Radius);
            }
            var cameraObject=new GameObject("ProjectileComparisonCamera"); var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=.65f; camera.transform.position=new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.05f,.08f); camera.cullingMask=1<<31;
            var target=new RenderTexture(960,320,24); var image=new Texture2D(960,320,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=target; RenderTexture.active=target; camera.Render();
                image.ReadPixels(new Rect(0,0,960,320),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(Folder,name+"Projectile_Original_Normalized_Restored.png"),image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null; RenderTexture.active=previous; target.Release();
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(cameraObject);
                foreach(var go in objects)
                {
                    var line=go.GetComponent<LineRenderer>(); if(line!=null) UnityEngine.Object.DestroyImmediate(line.sharedMaterial);
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }
        private static void Run()
        {
            Directory.CreateDirectory(Folder);
            try
            {
                TimeManager.Initialize(); WorldRegistry.Initialize();
                RuleSystem.Initialize(ScriptableObject.CreateInstance<RuleCatalog>());
                foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.BatBoss })
                {
                    foreach (float facing in new[] { -1f, 1f }) Case(kind + " melee " + facing, () =>
                    {
                        var actor=Make(kind); actor.FaceDirection(Vector2.right*facing); actor.Cue("Idle",1,true);
                        actor.Attack(actor.Definition.AttackRange,true);
                        var presenter=actor.GetComponent<EnemyVfxPresenter>();
                        Bounds b=ClipBounds(presenter,kind==EnemyKind.BatBoss?presenter.Profile.Slash:presenter.Profile.Impact);
                        if(facing>0) Capture(actor,(Vector2)actor.transform.position+Vector2.right*actor.Definition.AttackRange*.55f,
                            new Vector2(actor.Definition.AttackRange,actor.Definition.BodySize.y*.8f),kind+"Melee");
                        Near(b.center,(Vector2)actor.transform.position+Vector2.right*facing*actor.Definition.AttackRange*.55f,"近战中心");
                        Near(b.size,new Vector2(actor.Definition.AttackRange,actor.Definition.BodySize.y*.8f),"近战可见范围");
                        UnityEngine.Object.DestroyImmediate(actor.gameObject);
                    });
                }
                Case("Bat sustained dash follows the active query", () =>
                {
                    var actor=Make(EnemyKind.BatBoss); var presenter=actor.GetComponent<EnemyVfxPresenter>();
                    actor.Cue("Dash",1,true); actor.Attack(5,true,true);
                    actor.transform.position+=new Vector3(4,1); actor.FaceDirection(Vector2.right);
                    actor.Attack(5,sustained:true); Late(presenter);
                    Bounds b=ClipBounds(presenter,presenter.Profile.Slash);
                    Near(b.center,actor.MeleeCenter,"冲刺中攻击特效跟随实际判定中心");
                    actor.Status.SetPaused(true); Late(presenter);
                    Near(ClipBounds(presenter,presenter.Profile.Slash).center,b.center,"单体暂停位置稳定");
                    actor.Status.SetPaused(false); actor.Cue("Recover",1); Late(presenter);
                    Check(Array.TrueForAll(actor.GetComponentsInChildren<SpriteRenderer>(),r=>r.name!="EnemyVfx"||!r.enabled||r.sprite.texture!=presenter.Profile.Slash.Atlas),"攻击结束清除持续范围");
                });
                Case("Brick large ground windup retains authored layout", () =>
                {
                    var actor=Make(EnemyKind.BrickBoss); var presenter=actor.GetComponent<EnemyVfxPresenter>();
                    actor.transform.localScale=new Vector3(1.5f,.8f,1);
                    actor.Cue("HammerGround",1);
                    Bounds b=ClipBounds(presenter,presenter.Profile.Cast);
                    Near(b.center,actor.GroundSlamBounds.center,"大砸地恢复此前烘焙环的布局");
                    Near(b.size,actor.GroundSlamBounds.size,"大砸地保留攻击范围");
                    actor.RestoreLifeFlag(false);
                    Check(Array.TrueForAll(actor.GetComponentsInChildren<SpriteRenderer>(),r=>r.name!="EnemyVfx"||!r.enabled),"回溯清除旧范围");
                });
                Case("Brick large ground strike retains authored clips", () =>
                {
                    var actor=Make(EnemyKind.BrickBoss); var presenter=actor.GetComponent<EnemyVfxPresenter>();
                    Bounds area=actor.GroundSlamBounds; actor.AttackArea(area.center,area.size);
                    Bounds b=ClipBounds(presenter,presenter.Profile.Cast);
                    Near(b.center,area.center,"大砸地原效果位置"); Near(b.size,area.size,"大砸地原效果范围");
                    Check(actor.GetComponentsInChildren<ParticleSystem>().Length==0,"撤回新增方块粒子");
                    Check(Array.TrueForAll(actor.GetComponentsInChildren<LineRenderer>(),r=>r.name!="SlamRangeEdge"),"撤回新增直线边界");
                });
                foreach(float facing in new[] {-1f,1f}) Case("Brick normal hammer grounded "+facing, () =>
                {
                    var actor=Make(EnemyKind.BrickBoss); var presenter=actor.GetComponent<EnemyVfxPresenter>();
                    float groundY=actor.transform.position.y-actor.Definition.BodySize.y*.5f;
                    using(var floor=new ValidationFloor(groundY))
                    {
                        actor.FaceDirection(Vector2.right*facing); actor.Cue("HammerPlayer",1);
                        // 同时兼容动画时钟更新前后的工程基线，仅用于固定实际交付锤击姿态。
                        typeof(EnemyBrain).GetField("Elapsed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(actor.Brain,.6f);
                        typeof(EnemyActor).GetProperty("AnimationElapsed")?.SetValue(actor,.6f);
                        foreach(var animator in actor.GetComponentsInChildren<EnemySpriteAnimator>()) Late(animator);
                        actor.Attack(actor.Definition.AttackRange,true,true);
                        var circle=ClipRenderer(presenter,presenter.Profile.Impact);
                        Vector2 landing=new Vector2(actor.MeleeCenter.x,groundY);
                        Capture(actor,landing+Vector2.up, new Vector2(actor.Definition.AttackRange,2),
                            "BrickNormalHammer_"+(facing>0?"Right":"Left"),false);
                        Near(circle.transform.position,landing,"普通近身锤击小圈的原素材发射轴心贴地");
                        Near(circle.transform.localScale,Vector2.one*presenter.Profile.ImpactSize,"小圈恢复原作者素材比例");
                        Check(ClipCount(presenter,presenter.Profile.Death)==5,"五组原作者冲击序列补足范围");
                        Late(presenter);
                        Near(circle.transform.position,landing,"持续攻击下一帧小圈不回到身体中心");
                        Check(actor.GetComponentsInChildren<ParticleSystem>().Length==0,"普通锤击沿用原素材，不新增方块粒子");
                        Check(actor.GetComponentsInChildren<LineRenderer>().Length==0,"普通锤击不新增边框");
                        actor.Status.SetPaused(true); Late(presenter);
                        Near(circle.transform.position,landing,"暂停保持小圈地面落点"); actor.Status.SetPaused(false);
                        actor.RestoreLifeFlag(false);
                        Check(ClipCount(presenter,presenter.Profile.Impact)==0 && ClipCount(presenter,presenter.Profile.Death)==0,"回溯清空原素材的冲击环与粒子");
                    }
                });
                foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss,EnemyKind.RabbitBoss,EnemyKind.BatBoss,EnemyKind.ClockBoss })
                    Case(kind + " projectiles", () =>
                    {
                        var actor=Make(kind); EnemyProjectile shot=null;
                        actor.Feedback+=cue=> { if(cue.Kind==EnemyFeedbackKind.ProjectileSpawn) shot=(EnemyProjectile)cue.Subject; };
                        for(int index=0;index<3;index++)
                        {
                            actor.Shoot(Vector2.right,1,.4f,.22f,index); var view=shot.GetComponent<EnemyProjectileView>(); Late(view);
                            var body=shot.transform.Find("ProjectileVisual").GetComponent<SpriteRenderer>();
                            var profile=actor.GetComponent<EnemyVfxPresenter>().Profile;
                            EnemySpriteSequence sequence=profile.ProjectileAnimations.Length>0?profile.ProjectileAnimations[index%profile.ProjectileAnimations.Length]:null;
                            int frames=sequence!=null?sequence.Frames.Length:1;
                            for(int frame=0;frame<frames;frame++)
                            {
                                Field(shot,"_age",sequence!=null?(frame+.1f)/sequence.FrameRate:0f); Late(view);
                                Bounds visible=MeshBounds(body.sprite);
                                Near(body.transform.TransformPoint(visible.center),shot.transform.position,"弹芯可见中心");
                                // 回归基准是调整前的素材 PPU 与比例，不能再以整张 Sprite.bounds 当作图形。
                                float authoredScale=sequence!=null?1f:1.7f;
                                Vector2 expected=(Vector2)visible.size*(shot.Radius*2f*authoredScale);
                                Vector3 min=body.transform.TransformPoint(visible.min), max=body.transform.TransformPoint(visible.max);
                                Near(new Vector2(Mathf.Abs(max.x-min.x),Mathf.Abs(max.y-min.y)),expected,"保留原始可见尺寸及动画伸缩");
                                if(index==0 && frame==0) CaptureProjectile(shot,body,visible,authoredScale,kind.ToString());
                            }
                            shot.gameObject.SetActive(false);
                        }
                        UnityEngine.Object.DestroyImmediate(actor.gameObject);
                    });
                for(int hand=0;hand<3;hand++)
                {
                    int currentHand=hand;
                    Case("Clock authored beam "+hand, () =>
                {
                    var actor=Make(EnemyKind.ClockBoss); actor.BeamOrigin=new GameObject("Origin").transform;
                    actor.BeamOrigin.position=new Vector3(3,4); var beam=EnemyBeam.SpawnClockHand(actor,currentHand,actor.Definition.ClockBeamWidth+currentHand*actor.Definition.ClockBeamWidthStep,.2f);
                    Field(beam,"_age",.3f); Late(beam); var view=beam.GetComponent<EnemyBeamView>(); Late(view);
                    var art=beam.transform.Find("DeliveredBeam").GetComponent<SpriteRenderer>();
                    Check(Mathf.Abs(art.transform.lossyScale.y*art.sprite.bounds.size.y-beam.Width)<.01f,"光束宽度与伤害盒一致");
                    Vector3 localStart=new Vector3(art.sprite.bounds.min.x,art.sprite.bounds.center.y);
                    Vector3 localEnd=new Vector3(art.sprite.bounds.max.x,art.sprite.bounds.center.y);
                    Near(art.transform.TransformPoint(localStart),beam.Origin,"激光束口沿用实际表盘原点");
                    Near(art.transform.TransformPoint(localEnd),beam.GetComponent<LineRenderer>().GetPosition(1),"激光末端沿用墙面截断点");
                    object snapshot=beam.CaptureSnapshotState(); beam.RestoreSnapshotState(snapshot); Late(beam); Late(view);
                    Near(art.transform.TransformPoint(localStart),beam.Origin,"回溯后的激光原点稳定");
                    UnityEngine.Object.DestroyImmediate(actor.gameObject); beam.gameObject.SetActive(false);
                    });
                }
                Case("Clock fallback beam", () =>
                {
                    var actor=Make(EnemyKind.ClockBoss);
                    var profile=UnityEngine.Object.Instantiate(actor.GetComponent<EnemyVfxPresenter>().Profile);
                    profile.BeamAnimations=Array.Empty<EnemySpriteSequence>();
                    actor.GetComponent<EnemyVfxPresenter>().Profile=profile;
                    EnemyBeam beam=null;
                    actor.Feedback+=cue=>{if(cue.Kind==EnemyFeedbackKind.BeamSpawn) beam=(EnemyBeam)cue.Subject;};
                    actor.Beam(25,.2f,.3f); Field(beam,"_age",.4f); Late(beam); Late(beam.GetComponent<EnemyBeamView>());
                    var core=beam.transform.Find("BeamCore").GetComponent<LineRenderer>();
                    Check(Mathf.Abs(core.startWidth-beam.Width)<.001f,"回退实线核心使用真实伤害宽度");
                    Check(beam.GetComponent<LineRenderer>().startWidth<=beam.Width*1.3f,"回退外焰不再放大到十余倍");
                    UnityEngine.Object.DestroyImmediate(profile);
                });
            }
            catch(Exception e) { _failed++; Results.Add(e.ToString()); }
            File.WriteAllText(Path.Combine(Folder,"Results.txt"),$"Passed: {_passed}\nFailed: {_failed}\n"+string.Join("\n",Results));
            SessionState.SetBool(Pending,false); EditorApplication.Exit(_failed==0?0:1);
        }
    }
}
