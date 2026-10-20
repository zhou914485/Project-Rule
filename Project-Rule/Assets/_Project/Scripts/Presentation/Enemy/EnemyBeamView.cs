using UnityEngine;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>保留原光束伤害盒：预告期间细线，释放时暖色外焰与白色核心同时扩张、消退。</summary>
    [DefaultExecutionOrder(40)]
    public sealed class EnemyBeamView : MonoBehaviour
    {
        private EnemyBeam _beam;
        private LineRenderer _outer, _core, _sweepEnd, _sweepArc;
        private SpriteRenderer _art;
        private EnemyVfxProfile _profile;
        private float _lastAge;
        private bool _fired;
        public void Configure(EnemyVfxProfile profile)
        {
            _profile = profile; _fired = false; _lastAge = 0f;
            if (_beam == null) _beam = GetComponent<EnemyBeam>();
            if (_outer == null) _outer = GetComponent<LineRenderer>();
            _outer.sharedMaterial = profile.BeamMaterial; _outer.sortingOrder = 14;
            if (_core == null)
            {
                var go = new GameObject("BeamCore"); go.transform.SetParent(transform, false);
                _core = go.AddComponent<LineRenderer>(); _core.positionCount = 2;
                _core.useWorldSpace = true; _core.sortingOrder = 15;
            }
            // 核心用无纹理实线，外层单独保留作者的柔和渐变，避免核心也被纹理压窄。
            _core.sharedMaterial = profile.BeamCoreMaterial;
            if (_sweepEnd == null)
            {
                _sweepEnd = CreateWarningLine("SweepEnd", 2);
                _sweepArc = CreateWarningLine("SweepArc", 17);
            }
            _sweepEnd.sharedMaterial = _sweepArc.sharedMaterial = profile.BeamCoreMaterial;
            _sweepEnd.enabled = _sweepArc.enabled = false;
            if (_art == null && profile.BeamAnimations.Length > 0)
            {
                var go = new GameObject("DeliveredBeam"); go.transform.SetParent(transform, false);
                _art = go.AddComponent<SpriteRenderer>(); _art.sortingOrder = 15;
            }
            if (_art != null)
            {
                _art.sharedMaterial = profile.BeamSpriteMaterial;
                _art.enabled = false;
            }
        }
        private void LateUpdate()
        {
            if (_profile == null || _beam.Source == null) return;
            if (_beam.Age < _lastAge) _fired = false;
            _lastAge = _beam.Age;
            // 原点跟随表盘，而不是身体包围盒中心。发射方向和末端沿用原伤害线。
            Vector3 start = _beam.Origin;
            Vector3 end = _outer.GetPosition(1); start.z = end.z;
            _outer.SetPosition(0, start); _core.SetPosition(0, start); _core.SetPosition(1, end);
            float progress = Mathf.Clamp01(_beam.Age / Mathf.Max(.01f, _beam.WarningDuration));
            bool firing = _beam.Age >= _beam.WarningDuration;
            DrawSweepWarning(start, progress, firing);
            float fade = firing ? Mathf.Clamp01(1f - (_beam.Age - _beam.WarningDuration) / _beam.FireDuration) : 1f;
            _outer.enabled = true;
            _outer.startWidth = _outer.endWidth = firing ? _beam.Width * 1.25f : .1f + progress * .06f;
            Color outer = _profile.Secondary; outer.a = firing ? fade * .85f : .25f + progress * .4f;
            _outer.startColor = _outer.endColor = outer;
            _core.enabled = firing;
            _core.startWidth = _core.endWidth = _beam.Width;
            _core.startColor = _core.endColor = new Color(1f, .92f, .65f, fade);
            bool authored = DrawAuthoredBeam(start, end, firing, progress, fade);
            if (firing && !_fired)
            {
                _fired = true;
                var presenter = _beam.Source.GetComponent<EnemyVfxPresenter>();
                if (!authored) presenter?.Play(_profile.Shot, start, 1.8f, Color.white, duration: .24f);
                presenter?.Play(_profile.Impact, end, authored ? .65f : 2f, Color.white, duration: .25f);
                BossCameraImpulse.Pulse(_profile.CameraImpulse * .3f, .09f);
            }
        }
        private LineRenderer CreateWarningLine(string name, int points)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = points; line.sortingOrder = 13;
            line.startWidth = line.endWidth = .045f;
            return line;
        }
        private void DrawSweepWarning(Vector3 start, float progress, bool firing)
        {
            bool visible = _beam.FollowsClockHand && !firing;
            _sweepEnd.enabled = _sweepArc.enabled = visible;
            if (!visible) return;
            Color color = new Color(1f, .78f, .35f, .18f + progress * .35f);
            _sweepEnd.startColor = _sweepEnd.endColor = color;
            _sweepArc.startColor = _sweepArc.endColor = color;
            Vector3 end = _beam.SweepEndPoint; end.z = start.z;
            _sweepEnd.SetPosition(0, start); _sweepEnd.SetPosition(1, end);
            // 近轴短弧标明旋转方向和覆盖角；整场只复用两条提示线，不生成粒子弹幕。
            float radius = Mathf.Max(1f, _beam.Source.Definition.BodySize.x * .75f);
            for (int i = 0; i < _sweepArc.positionCount; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, 0f, -_beam.SweepAngle * i / (_sweepArc.positionCount - 1))
                    * (Vector3)_beam.Direction;
                _sweepArc.SetPosition(i, start + direction * radius);
            }
        }
        private bool DrawAuthoredBeam(Vector3 start, Vector3 end, bool firing, float progress, float fade)
        {
            if (_art == null || _profile.BeamAnimations.Length == 0) return false;
            int hand = Mathf.Clamp(_beam.ClockHand, 0, _profile.BeamAnimations.Length - 1);
            EnemySpriteSequence sequence = _profile.BeamAnimations[hand];
            if (sequence == null || sequence.Frames.Length < 3) return false;
            // 原稿前两帧是束口蓄力，后五帧是伸展/持续激光；表现服从原有判定线。
            int index = firing ? Mathf.Min(sequence.Frames.Length - 1,
                2 + Mathf.FloorToInt((1f - fade) * (sequence.Frames.Length - 2)))
                : Mathf.Min(1, Mathf.FloorToInt(progress * 2f));
            Sprite sprite = sequence.Frames[index];
            if (sprite == null) return false;
            float length = firing ? Vector3.Distance(start, end) : Mathf.Max(.3f, _beam.Width * 2f);
            float width = firing ? _beam.Width : Mathf.Max(.3f, _beam.Width * 2f);
            _art.sprite = sprite; _art.color = Color.white; _art.enabled = true;
            Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg);
            _art.transform.localScale = new Vector3(length / Mathf.Max(.001f, sprite.bounds.size.x),
                width / Mathf.Max(.001f, sprite.bounds.size.y), 1f);
            Vector3 offset = Vector3.Scale(new Vector3(sprite.bounds.min.x, sprite.bounds.center.y), _art.transform.localScale);
            _art.transform.SetPositionAndRotation(start - rotation * offset, rotation);
            // 原画束宽与伤害宽度一致量级，避免用十几倍粗的外焰掩盖躲避空间。
            if (firing) { _outer.enabled = false; _core.enabled = false; }
            return true;
        }
        private void OnDisable()
        {
            if (_core != null) _core.enabled = false;
            if (_art != null) _art.enabled = false;
            if (_sweepEnd != null) _sweepEnd.enabled = false;
            if (_sweepArc != null) _sweepArc.enabled = false;
        }
    }
}
