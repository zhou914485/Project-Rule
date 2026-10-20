using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>龙珠：按 <see cref="CityRewardSource.Kind"/> 选贴图，并压掉场景里按颜色区分的旧 override。</summary>
    /// <remarks>只有「生命 / 能量」两种龙珠有专属贴图（战斗力那条已删）。
    /// 其它 Kind（能力 / 规则）不换贴图 —— 那些箱子本来就不该挂本组件。</remarks>
    public sealed class CityRewardSpriteView : MonoBehaviour
    {
        public CityRewardSource Source;
        public SpriteRenderer Target;
        public Sprite Health;
        public Sprite Energy;

        private void LateUpdate()
        {
            if (Source == null || Target == null) return;

            Sprite sprite = Source.Kind == CityRewardKind.Health ? Health
                : Source.Kind == CityRewardKind.Energy ? Energy : null;
            if (sprite != null && Target.sprite != sprite) Target.sprite = sprite;
            if (Target.color != Color.white) Target.color = Color.white;
        }
    }
}
