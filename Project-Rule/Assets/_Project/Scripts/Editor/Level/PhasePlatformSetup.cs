using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键整理当前场景里的虚实平台：给每块平台补一个**点按式**按钮（站到平台顶面正中、
    /// <c>Toggle = false</c>、<c>FollowMotor</c> 指向同一个 <see cref="PhasePlatformGroup"/>），
    /// 并把所有平台的切换时间统一成 0.5 秒。
    /// </summary>
    /// <remarks>
    /// <para>菜单 <c>Tools/规则游戏/地图组件/⑯a 虚实平台：补按钮 + 统一切换时间（当前场景）</c>。
    /// 用于「平台是之前摆的」或「按钮还是切换型」的情况 —— 新生成的一套本来就已经配好，不用点。</para>
    ///
    /// <para><b>幂等</b>：已经接了按钮的平台会跳过新建，但**已有的按钮也会被改回点按式**
    /// （<c>Toggle = false</c>、<c>IsOn = false</c>），并把平台收进组里（重复点不会重复加）。</para>
    ///
    /// <para><b>组从哪来</b>：优先用平台父链上已有的组；没有就用当前场景里的第一个组；
    /// 一个组都没有时，在平台群的中间新建一个 <c>PhasePlatforms</c> 根。</para>
    ///
    /// <para>全部走 Undo；改完记得保存场景。</para>
    /// </remarks>
    public static class PhasePlatformSetup
    {
        private const string ButtonPrefabPath = "Assets/_Project/Prefabs/BrickCity/CityButton.prefab";
        private const string ButtonName = "PhaseButton";
        private const string GroupRootName = "PhasePlatforms";

        /// <summary>CityButton 的碰撞体高 0.35 —— 中心离平台顶面 0.175 才正好贴着顶面。</summary>
        private const float ButtonHalfHeight = 0.175f;

        /// <summary>统一后的切换延迟（秒）。</summary>
        private const float SwitchDelaySeconds = 0.5f;

        [MenuItem("Tools/规则游戏/地图组件/⑯a 虚实平台：补按钮 + 统一切换时间（当前场景）", false, 69)]
        public static void AddButtonsToAll()
        {
            var buttonPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ButtonPrefabPath);
            if (buttonPrefab == null)
            {
                Debug.LogError("[地图组件] 找不到按钮预制体：" + ButtonPrefabPath);
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            List<PhasePlatform> platforms = Collect<PhasePlatform>(scene);
            if (platforms.Count == 0)
            {
                int elsewhere = CountOutside(scene, CollectAll<PhasePlatform>());
                Debug.LogWarning("[地图组件] 当前场景（" + scene.name + "）里没有 PhasePlatform" +
                                 (elsewhere > 0
                                     ? $"，但其它已加载场景里有 {elsewhere} 块 —— 切到那个场景再点一次。"
                                     : "。"));
                return;
            }

            PhasePlatformGroup group = ResolveGroup(scene, platforms);
            var all = new List<PhasePlatform>(platforms.Count);
            int created = 0;
            int rewired = 0;
            int skipped = 0;
            int delayUpdated = 0;

            foreach (PhasePlatform platform in platforms)
            {
                all.Add(platform);
                if (ApplySwitchDelay(platform)) delayUpdated++;

                CitySwitch existing = FindChildButton(platform);
                if (existing != null && IsWiredTo(existing, group))
                {
                    skipped++;          // 已经接好了（下面统一改成点按式）
                    continue;
                }

                if (existing != null)
                {
                    if (existing.FollowMotor != null)
                    {
                        Debug.LogWarning($"[地图组件] {platform.name} 上已经有一个按钮接在别的目标上，" +
                                         "没有动它，也没有再加一个 —— 请手动确认。", platform);
                        skipped++;
                        continue;
                    }

                    Wire(existing, group);   // 有按钮但还没接线：接上，不重复加
                    rewired++;
                    continue;
                }

                CreateButton(platform, group, buttonPrefab);
                created++;
            }

            int normalized = NormalizeButtons(scene, group);

            Undo.RecordObject(group, "收集虚实平台");
            group.SetPlatforms(all.ToArray());
            EditorUtility.SetDirty(group);

            Debug.Log($"[地图组件] 已处理 {platforms.Count} 块虚实平台：新建按钮 {created} 个，" +
                      $"接上已有按钮 {rewired} 个，已有按钮跳过 {skipped} 块；" +
                      $"把 {normalized} 个按钮改回点按式（Toggle 不勾、IsOn 复位、关掉踩踏触发），" +
                      $"{delayUpdated} 块平台的切换时间改为 {SwitchDelaySeconds:0.##} 秒；" +
                      $"全部接到「{group.name}」的 PhasePlatformGroup 上。\n" +
                      "· 每块平台上的按钮任何时候都能按；按一次整组一起翻\n" +
                      "· 记得保存场景");
        }

        /// <summary>把平台的切换时间统一成 <see cref="SwitchDelaySeconds"/>；返回是否真的改了。</summary>
        private static bool ApplySwitchDelay(PhasePlatform platform)
        {
            var so = new SerializedObject(platform);
            SerializedProperty delay = so.FindProperty("_switchDelay");
            if (delay == null || Mathf.Approximately(delay.floatValue, SwitchDelaySeconds)) return false;

            Undo.RecordObject(platform, "统一虚实平台切换时间");
            delay.floatValue = SwitchDelaySeconds;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(platform);
            return true;
        }

        /// <summary>把接在本组上的按钮全部改回点按式；返回改了几个。</summary>
        private static int NormalizeButtons(Scene scene, PhasePlatformGroup group)
        {
            List<CitySwitch> triggers = Collect<CitySwitch>(scene);
            int changed = 0;

            for (int i = 0; i < triggers.Count; i++)
            {
                CitySwitch trigger = triggers[i];
                if (!IsWiredTo(trigger, group)) continue;
                if (!trigger.Toggle && !trigger.IsOn && !trigger.PressByPlayerProximity) continue;

                Undo.RecordObject(trigger, "改回点按式按钮");
                trigger.Toggle = false;
                trigger.IsOn = false;
                // 踩上去就按的按钮不会被复位（会一直锁住）—— 虚实平台统一用 E / 攻击触发。
                trigger.PressByPlayerProximity = false;
                EditorUtility.SetDirty(trigger);
                changed++;
            }
            return changed;
        }

        // ==================================================================
        //  查找
        // ==================================================================

        /// <summary>所有已加载场景里（含未激活）的组件。</summary>
        private static List<T> CollectAll<T>() where T : Component
        {
            var result = new List<T>();

            T[] found = Object.FindObjectsByType<T>(FindObjectsInactive.Include);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null) result.Add(found[i]);
            }
            return result;
        }

        /// <summary>某个场景里（含未激活）的组件。预制体预览场景 / 其它已加载场景都不算。</summary>
        private static List<T> Collect<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            if (!scene.IsValid()) return result;

            List<T> all = CollectAll<T>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].gameObject.scene == scene) result.Add(all[i]);
            }
            return result;
        }

        private static int CountOutside(Scene scene, List<PhasePlatform> all)
        {
            int count = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].gameObject.scene != scene) count++;
            }
            return count;
        }

        /// <summary>平台该挂在哪个组上：父链上的 → 场景里的第一个 → 新建一个。</summary>
        private static PhasePlatformGroup ResolveGroup(Scene scene, List<PhasePlatform> platforms)
        {
            for (int i = 0; i < platforms.Count; i++)
            {
                PhasePlatformGroup ancestor = platforms[i].GetComponentInParent<PhasePlatformGroup>();
                if (ancestor != null) return ancestor;
            }

            List<PhasePlatformGroup> groups = Collect<PhasePlatformGroup>(scene);
            if (groups.Count > 0) return groups[0];

            Vector3 center = Vector3.zero;
            for (int i = 0; i < platforms.Count; i++) center += platforms[i].transform.position;
            center /= platforms.Count;

            var root = new GameObject(GroupRootName);
            root.transform.position = center;
            Undo.RegisterCreatedObjectUndo(root, "创建虚实平台组");

            Debug.Log($"[地图组件] 场景里没有 PhasePlatformGroup，已在 ({center.x:0.##}, {center.y:0.##}) " +
                      $"新建「{GroupRootName}」作为组。", root);

            return root.AddComponent<PhasePlatformGroup>();
        }

        // ==================================================================
        //  按钮
        // ==================================================================

        /// <summary>平台下面（含自身）第一个 CitySwitch —— 没有就返回 null。</summary>
        private static CitySwitch FindChildButton(PhasePlatform platform)
        {
            CitySwitch[] triggers = platform.GetComponentsInChildren<CitySwitch>(true);
            return triggers.Length > 0 ? triggers[0] : null;
        }

        /// <summary>把一个现成的按钮接到组上，并设成点按式。</summary>
        private static void Wire(CitySwitch trigger, PhasePlatformGroup group)
        {
            Undo.RecordObject(trigger, "接上虚实平台组");
            trigger.Toggle = false;                 // 点按式：按一次由平台翻转一次，按完平台复位
            trigger.IsOn = false;
            trigger.PressByPlayerProximity = false;
            trigger.FollowMotor = group;
            EditorUtility.SetDirty(trigger);
        }

        private static bool IsWiredTo(CitySwitch trigger, PhasePlatformGroup group)
        {
            if (trigger == null || trigger.FollowMotor == null) return false;
            if (trigger.FollowMotor == (MonoBehaviour)group) return true;

            // 拖 GameObject 时 Unity 只会赋第一个 MonoBehaviour —— 退一步到同一个物体上找组。
            return trigger.FollowMotor.GetComponent<PhasePlatformGroup>() == group;
        }

        private static void CreateButton(PhasePlatform platform, PhasePlatformGroup group, GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return;

            Undo.RegisterCreatedObjectUndo(instance, "创建虚实平台按钮");
            instance.name = ButtonName;
            instance.transform.SetParent(platform.transform, false);
            instance.transform.localPosition = new Vector3(0f, TopLocalY(platform), 0f);

            var trigger = instance.GetComponent<CitySwitch>();
            if (trigger == null)
            {
                Debug.LogError("[地图组件] " + ButtonPrefabPath + " 上没有 CitySwitch —— 按钮驱动不了平台。", instance);
                return;
            }

            Wire(trigger, group);
        }

        /// <summary>按钮中心在平台局部空间里的高度：平台顶面 + 按钮半高。</summary>
        /// <remarks>碰撞体的 <c>size</c> / <c>offset</c> 本来就是**局部**单位，与传进来的
        /// <c>localPosition</c> 同一个坐标系，不需要再乘缩放。</remarks>
        private static float TopLocalY(PhasePlatform platform)
        {
            var box = platform.GetComponent<BoxCollider2D>();
            if (box == null) return ButtonHalfHeight;

            return box.offset.y + box.size.y * 0.5f + ButtonHalfHeight;
        }
    }
}
