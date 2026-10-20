using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>能量补给石像：<b>用一次就碎掉消失</b>，回多少能量由 Inspector 配。</summary>
    /// <remarks>
    /// <para><b>两条触发路径</b>（与 <see cref="CitySwitch"/> 的按钮 / <see cref="CityRewardSource"/>
    /// 的攻击宝箱同一条路）：按 E（<c>CityInteractor</c> → <see cref="Interact"/>），
    /// 或被<b>攻击盒 / 纸飞机</b>打中（<see cref="IContactSwitch.ImpactByContact"/>）。</para>
    /// <para>与 <see cref="CityBubble"/> 同一套「消耗 → 关碰撞 / 关显示 → 回溯复原」：
    /// 只把「有没有用过」存进快照，碰撞体与可见性由 <see cref="CityMechanism"/> 统一记。
    /// 回溯之后石像会重新长回来、还能再用一次。</para>
    /// <para>碎裂是表现层：<see cref="CityShatterBurst"/> 运行时按原图切碎片生成，
    /// 没有碰撞体、不进快照，也不改世界状态。</para>
    /// </remarks>
    public sealed class CityRefill : CityInteractable, IContactSwitch
    {
        [Header("补给")]
        [Tooltip("一次回复多少能量。实际回复量会被能量上限夹住。")]
        [SerializeField, Min(0f)] private float _amount = 30f;

        [Header("表现")]
        [Tooltip("用掉时塌落的碎片数量；0 = 直接消失不塌。")]
        [SerializeField, Min(0)] private int _shards = 42;

        [Tooltip("补给成功时触发（音效 / 特效挂这里）。")]
        public UnityEvent OnRefilled = new UnityEvent();

        private bool _used;
        private PlayerController _player;

        /// <summary>一次回复的能量。</summary>
        public float Amount => _amount;

        /// <summary>是否已经被用掉。</summary>
        public bool Used => _used;

        public override bool Available => !_used;

        public override bool Interact(PlayerController player)
        {
            if (!Available || Paused || player == null || _amount <= 0f) return false;

            var status = player.GetComponent<EntityStatus>();
            if (status == null || status.Energy >= status.EnergyMax) return false;

            status.RestoreEnergy(_amount);
            _used = true;
            Shatter();
            OnRefilled.Invoke();
            return true;
        }

        // ------------------------------------------------------------------
        //  IContactSwitch：被攻击盒 / 投射物打中
        // ------------------------------------------------------------------

        // 回蓝石像不是压板：石头 / 铁箱压过来不算「用掉」。
        public void PressByContact() { }
        public void ReleaseByContact() { }

        /// <summary>
        /// 被挥砍或被纸飞机打中：与按 E 走同一个 <see cref="Interact"/>。
        /// </summary>
        /// <remarks>
        /// <para>接口不带攻击者引用（多档拉杆只用它选方向），而回蓝必须有「谁的蓝」——
        /// 这里按全场景唯一的玩家取，与 <c>PlayerSpawnPoint</c> 同一条做法。</para>
        /// <para>能量满 / 已经用掉时什么都不做：不会白打一尊石像就把它打没。</para>
        /// </remarks>
        public void ImpactByContact(Vector2 sourcePoint)
        {
            if (!Available || Paused) return;

            PlayerController player = ResolvePlayer();
            if (player != null) Interact(player);
        }

        private PlayerController ResolvePlayer()
        {
            if (_player != null) return _player;

            PlayerProgress progress = PlayerProgress.Find();
            if (progress != null) _player = progress.GetComponentInParent<PlayerController>();
            if (_player == null) _player = FindAnyObjectByType<PlayerController>();
            return _player;
        }

        private void Shatter()
        {
            SpriteRenderer source = SourceVisual();
            if (source != null && _shards > 0) CityShatterBurst.Spawn(source, _shards);

            SetVisible(false);
            if (Shape != null) Shape.enabled = false;
        }

        /// <summary>拿有图的那一个 <see cref="SpriteRenderer"/>（机关可能有多个表现子物体）。</summary>
        private SpriteRenderer SourceVisual()
        {
            for (int i = 0; i < Visuals.Length; i++)
            {
                if (Visuals[i] != null && Visuals[i].sprite != null) return Visuals[i];
            }
            return null;
        }

        protected override object CaptureMechanismState() => _used;
        protected override void RestoreMechanismState(object state) => _used = state is bool used && used;
    }
}
