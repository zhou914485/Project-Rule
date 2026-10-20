using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Enemy;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene.DemonCastle;
using RuleGame.Presentation.DemonCastle;

namespace RuleGame.Editor.Enemy
{
    /// <summary>只接入本次交付的小怪表现，保留现有配置、碰撞、关卡和资源 GUID。</summary>
    public static class MinionArtSetup
    {
        public const string ManifestPath = "Assets/_Project/Art/Enemies/Minions/MinionArtManifest.json";
        public const string DataFolder = "Assets/_Project/Data/Enemies/Minions";
        public static readonly EnemyKind[] Kinds = { EnemyKind.StoneGuard, EnemyKind.ExplosiveBlock,
            EnemyKind.ClockMouse, EnemyKind.PlushBird, EnemyKind.BatMinion, EnemyKind.HugBear, EnemyKind.PlushSeal };

        [MenuItem("RuleGame/Enemies/接入已交付小怪美术（保留战斗参数）")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (text == null) throw new InvalidOperationException("请先运行 Tools/EnemyAssets/import_minions.py。");
            var manifest = JsonUtility.FromJson<EnemyArtManifest>(text.text);
            EnemyArtImporter.ImportFrames(manifest.frames);
            Directory.CreateDirectory(DataFolder);
            AssetDatabase.Refresh();
            var actors = new HashSet<string>();
            foreach (var entry in manifest.sequences) actors.Add(entry.actor);
            foreach (string actor in actors)
            {
                string path = DataFolder + "/" + actor + "_Animations.asset";
                var set = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>(path);
                if (set == null) { set = ScriptableObject.CreateInstance<EnemyAnimationSet>(); AssetDatabase.CreateAsset(set, path); }
                var sequences = new List<EnemySpriteSequence>();
                foreach (var entry in manifest.sequences)
                {
                    if (entry.actor != actor) continue;
                    var sprites = Array.ConvertAll(entry.frames, frame => AssetDatabase.LoadAssetAtPath<Sprite>(frame));
                    if (Array.Exists(sprites, sprite => sprite == null)) throw new InvalidOperationException("缺少 Sprite：" + actor + "/" + entry.action);
                    sequences.Add(new EnemySpriteSequence { Action = entry.action, Frames = sprites,
                        FrameRate = manifest.frameRate, NativeFrameRate = entry.nativeFrameRate, FacesRight = entry.facesRight });
                }
                set.Sequences = sequences.ToArray();
                EditorUtility.SetDirty(set);
                if (actor == "Firework") ConfigureFirework(set);
                if (Enum.TryParse(actor, out EnemyKind kind) && Array.IndexOf(Kinds, kind) >= 0) Configure(kind, set);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[MinionArt] 已接入六种已有小怪美术及烟花点火原画；石头人既有美术保留。");
        }

        public static string PrefabPath(EnemyKind kind) => kind == EnemyKind.BatMinion
            ? "Assets/_Project/Prefabs/Enemies/BatMinion.prefab"
            : FieldMinionSetup.PrefabFolder + "/" + kind + ".prefab";

        private static void Configure(EnemyKind kind, EnemyAnimationSet set)
        {
            string path = PrefabPath(kind);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                throw new InvalidOperationException("缺少已实现的小怪预制体：" + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var actor = root.GetComponent<EnemyActor>();
                if (kind == EnemyKind.HugBear || kind == EnemyKind.PlushSeal || kind == EnemyKind.ClockMouse)
                    EnsureDeathDuration(actor.Definition, set);
                if (kind == EnemyKind.PlushSeal) ConfigureProjectile(actor, set, "ProjectileIce");
                if (kind == EnemyKind.PlushBird) ConfigureProjectile(actor, set, "ProjectileLight");
                var placeholder = root.GetComponent<FieldMinionView>();
                var animator = root.GetComponentInChildren<EnemySpriteAnimator>();
                var body = placeholder != null ? placeholder.Body : animator.GetComponent<SpriteRenderer>();
                if (placeholder != null) UnityEngine.Object.DestroyImmediate(placeholder);
                body.gameObject.name = "Visual";
                body.transform.localScale = Vector3.one;
                body.transform.localPosition = new Vector3(0f, actor.Definition.Flying ? 0f : -actor.Definition.BodySize.y * .5f, 0f);
                body.transform.localRotation = Quaternion.identity;
                body.color = Color.white;
                body.flipX = false;
                body.enabled = true;
                var initial = Array.Find(set.Sequences, sequence => sequence.Action == (kind == EnemyKind.BatMinion ? "Walk" : "Idle"));
                body.sprite = initial.Frames[0];
                if (animator == null) animator = body.gameObject.AddComponent<EnemySpriteAnimator>();
                animator.Actor = actor; animator.AnimationSet = set;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void EnsureDeathDuration(EnemyDefinition definition, EnemyAnimationSet set)
        {
            var dead = Array.Find(set.Sequences, sequence => sequence.Action == "Dead");
            var timings = new List<EnemyActionTiming>(definition.Timings);
            int index = timings.FindIndex(timing => timing.Action == "Dead");
            float duration = Mathf.Max(definition.Duration("Dead"), dead.Duration);
            if (index >= 0 && Mathf.Approximately(timings[index].Duration, duration)) return;
            var timing = new EnemyActionTiming { Action = "Dead", Duration = duration };
            if (index >= 0) timings[index] = timing; else timings.Add(timing);
            definition.Timings = timings.ToArray(); EditorUtility.SetDirty(definition);
        }

        private static void ConfigureProjectile(EnemyActor actor, EnemyAnimationSet set, string action)
        {
            string path = DataFolder + "/" + actor.Definition.Kind + "_Vfx.asset";
            var profile = AssetDatabase.LoadAssetAtPath<EnemyVfxProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<EnemyVfxProfile>(); AssetDatabase.CreateAsset(profile, path); }
            var ice = Array.Find(set.Sequences, sequence => sequence.Action == action);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            profile.ProjectileAnimations = new[] { ice }; profile.ProjectileSprite = ice.Frames[0];
            profile.Material = profile.ProjectileMaterial = profile.BeamMaterial = material;
            profile.PhysicalProjectiles = true; profile.Accent = Color.white; profile.CameraImpulse = 0f;
            EditorUtility.SetDirty(profile);
            var presenter = actor.GetComponent<EnemyVfxPresenter>();
            if (presenter == null) presenter = actor.gameObject.AddComponent<EnemyVfxPresenter>();
            presenter.Profile = profile; actor.ProjectileSprite = ice.Frames[0];
        }

        private static void ConfigureFirework(EnemyAnimationSet set)
        {
            const string path = "Assets/_Project/Prefabs/DemonCastle/DemonFirework.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new InvalidOperationException("缺少既有魔王城烟花机关。");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var child = root.transform.Find("DeliveredFuse");
                if (child == null)
                {
                    child = new GameObject("DeliveredFuse").transform;
                    child.SetParent(root.transform, false);
                    child.gameObject.AddComponent<SpriteRenderer>();
                }
                var visual = child.GetComponent<SpriteRenderer>();
                visual.sprite = set.Sequences[0].Frames[0]; visual.color = Color.white;
                visual.sharedMaterial = root.transform.Find("Visual").GetComponent<SpriteRenderer>().sharedMaterial;
                visual.transform.localScale = Vector3.one;
                visual.transform.localPosition = Vector3.down * .5f;
                visual.sortingOrder = 5; visual.enabled = true;
                // The delivered frames include the entire box; replace the old composite visuals.
                foreach (var legacy in root.GetComponentsInChildren<SpriteRenderer>(true))
                    if (legacy != visual) legacy.gameObject.SetActive(false);
                var view = child.GetComponent<DemonCastleSpriteView>();
                if (view == null) view = child.gameObject.AddComponent<DemonCastleSpriteView>();
                view.Target = visual; view.Frames = set.Sequences[0].Frames;
                view.Firework = root.GetComponent<DemonFirework>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void ApplyBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只用于隔离工程。");
            Apply();
            EditorApplication.Exit(0);
        }
    }
}
