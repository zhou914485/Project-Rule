using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 虚实平台的**点按式**按钮：切换发生后把接在目标上的 <see cref="CitySwitch"/> 复位成「未按下」。
    /// </summary>
    /// <remarks>
    /// <para>按钮用 <c>CitySwitch</c> 的**单次模式**（<c>Toggle = false</c>）：按一次就把 <c>IsOn</c>
    /// 置真并锁住 —— 这正是「点按」要的手感，但它不会自己弹回来。平台 / 组在一次切换之后调
    /// <see cref="ReleaseAll"/> 把按钮拉回「未按下」，于是下一次按仍然是完整的一次按下。</para>
    ///
    /// <para>⚠️ 只复位 <c>Toggle = false</c> 且没开 <c>PressByPlayerProximity</c> 的按钮：
    /// 摇杆要保留自己的开关状态；「踩上去就按」的按钮如果每帧复位会变成连发。</para>
    ///
    /// <para>识别方式是 <c>FollowMotor</c> 指向谁（与 <c>CitySwitch</c> 的解析一致：
    /// 直接指向目标，或者指向同一个物体上的任意组件）。不按名字 / 层级找。</para>
    /// </remarks>
    internal static class PhaseButtonRelease
    {
        /// <summary>把接在 <paramref name="target"/> 上的点按式按钮全部复位成「未按下」。</summary>
        /// <param name="cache">调用方持有的缓存；第一次调用时自动收集。</param>
        public static void ReleaseAll(MonoBehaviour target, ref CitySwitch[] cache)
        {
            if (target == null) return;
            if (cache == null) cache = Collect(target);

            for (int i = 0; i < cache.Length; i++)
            {
                CitySwitch trigger = cache[i];
                if (trigger == null || trigger.Toggle || trigger.PressByPlayerProximity) continue;

                // 只改字段：表现层每帧读 IsOn，会自己回到「未按下」那一帧。
                if (trigger.IsOn) trigger.IsOn = false;
            }
        }

        private static CitySwitch[] Collect(MonoBehaviour target)
        {
            CitySwitch[] all = Object.FindObjectsByType<CitySwitch>(FindObjectsInactive.Include);
            var wired = new List<CitySwitch>();

            for (int i = 0; i < all.Length; i++)
            {
                CitySwitch trigger = all[i];
                if (trigger == null || !Targets(trigger, target)) continue;
                wired.Add(trigger);
            }
            return wired.ToArray();
        }

        private static bool Targets(CitySwitch trigger, MonoBehaviour target)
        {
            MonoBehaviour follow = trigger.FollowMotor;
            if (follow == null) return false;
            if (follow == target) return true;

            // 拖 GameObject 时 Unity 只会赋第一个 MonoBehaviour —— 退一步到同一个物体上找目标类型。
            return follow.GetComponent(target.GetType()) == target;
        }
    }
}
