using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace RuleGame.UI.RuleWheel
{
    /// <summary>朝角色前方展开的纸扇轮盘。绘制、标签与命中共用角度及镜像参数。</summary>
    [UxmlElement]
    public partial class RuleFan : VisualElement
    {
        [UxmlAttribute] public float OuterRadius { get; set; } = 184f;
        [UxmlAttribute] public float InnerRadius { get; set; } = 102f;
        [UxmlAttribute] public float StartAngle { get; set; } = -84f;
        [UxmlAttribute] public float EndAngle { get; set; } = 84f;
        [UxmlAttribute] public float WordWidth { get; set; } = 88f;
        [UxmlAttribute] public float WordHeight { get; set; } = 36f;
        private string[] _words = Array.Empty<string>();
        private bool[] _available = Array.Empty<bool>();
        private bool[] _active = Array.Empty<bool>();
        private Label[] _labels = Array.Empty<Label>();
        private VisualElement[] _cards = Array.Empty<VisualElement>();
        private VisualElement[] _glows = Array.Empty<VisualElement>();
        private float[] _hover = Array.Empty<float>();
        public const float RevealDuration = .24f;
        public const float ExitDuration = .18f;
        private float _reveal = 1f, _exit, _clock;
        private bool _exiting, _released;
        private static readonly Vector2[] AttributeSizes = { new(262, 252), new(242, 246), new(218, 240), new(220, 240), new(246, 248), new(264, 254) };
        private static readonly Vector2[] RelationSizes = { new(262, 258), new(234, 242), new(204, 244), new(234, 252), new(252, 250) };
        private static readonly float[] AttributeAngles = { -60f, -37f, -14f, 12f, 35f, 59f };
        private static readonly float[] RelationAngles = { -50f, -27f, -2f, 21f, 46f };
        public int SelectedIndex { get; private set; } = -1;
        public Vector2 Center => contentRect.size * 0.5f;
        public bool Mirrored { get; private set; }

        public RuleFan()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => LayoutWords());
        }

        public void SetOptions(string[] words, bool[] available)
        {
            Clear();
            _words = words;
            _available = available;
            _active = new bool[words.Length];
            _labels = new Label[words.Length];
            _cards = new VisualElement[words.Length];
            _glows = new VisualElement[words.Length];
            _hover = new float[words.Length];
            SelectedIndex = -1;
            for (int i = 0; i < words.Length; i++)
            {
                string artwork = (words.Length == 6 ? "attribute-card-" : "relation-card-") + i;
                var glow = new VisualElement { pickingMode = PickingMode.Ignore };
                glow.AddToClassList("wheel-card"); glow.AddToClassList("wheel-glow"); glow.AddToClassList(artwork);
                Add(glow); _glows[i] = glow;
                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.AddToClassList("wheel-card"); card.AddToClassList("wheel-paper"); card.AddToClassList(artwork);
                Add(card); _cards[i] = card;
                var label = new Label(words[i]) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("wheel-word");
                label.EnableInClassList("long-word", words[i].Length > 4);
                label.EnableInClassList("unavailable", !available[i]);
                Add(label);
                _labels[i] = label;
            }
            foreach (Label label in _labels) label.BringToFront();
            LayoutWords();
            MarkDirtyRepaint();
        }

        public void SetAvailable(bool[] available)
        {
            _available = available;
            for (int i = 0; i < _labels.Length; i++) _labels[i].EnableInClassList("unavailable", !available[i]);
            MarkDirtyRepaint();
        }

        /// <summary>标记当前生效的规则格位；扇区填充与标签配色一起表现高亮。</summary>
        /// <remarks>可用性每 0.1 秒刷新一次，所以先比一遍，没变化就不重绘。</remarks>
        public void SetActiveMarks(bool[] active)
        {
            if (SameMarks(active)) return;
            // 必须复制：调用方复用同一块数组，存引用会让「上一次」跟着一起变，高亮永远不刷新。
            _active = active == null ? Array.Empty<bool>() : (bool[])active.Clone();
            for (int i = 0; i < _labels.Length; i++)
                _labels[i].EnableInClassList("active", i < _active.Length && _active[i]);
            MarkDirtyRepaint();
        }

        private bool SameMarks(bool[] active)
        {
            int count = active?.Length ?? 0;
            if (count != _active.Length) return false;
            for (int i = 0; i < count; i++) if (active[i] != _active[i]) return false;
            return true;
        }

        public void SetArc(float start, float end)
        {
            if (Mathf.Approximately(StartAngle, start) && Mathf.Approximately(EndAngle, end)) return;
            StartAngle = start; EndAngle = end;
            LayoutWords(); MarkDirtyRepaint();
        }

        public void SetMirrored(bool mirrored)
        {
            if (Mirrored == mirrored) return;
            Mirrored = mirrored;
            LayoutWords(); MarkDirtyRepaint();
        }

        private Vector2 Direction(float angle)
        {
            float x = Mathf.Cos(angle * Mathf.Deg2Rad);
            return new Vector2(Mirrored ? -x : x, Mathf.Sin(angle * Mathf.Deg2Rad));
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _words.Length) index = -1;
            if (index == SelectedIndex) return;
            SelectedIndex = index;
            for (int i = 0; i < _labels.Length; i++) _labels[i].EnableInClassList("selected", i == index);
            MarkDirtyRepaint();
        }

        public int IndexAt(Vector2 localPoint)
        {
            Vector2 delta = localPoint - Center;
            if (delta.magnitude < InnerRadius || delta.magnitude > OuterRadius + 28f || _words.Length == 0) return -1;
            // 先把左侧输入还原到同一套逻辑角度，鼠标和手柄保持相同的词条顺序。
            if (Mirrored) delta.x = -delta.x;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            if (angle < StartAngle || angle > EndAngle) return -1;
            return Mathf.Min(_words.Length - 1, Mathf.FloorToInt((angle - StartAngle) / (EndAngle - StartAngle) * _words.Length));
        }

        public Vector2 OptionPoint(int index)
        {
            float angle = Mathf.Lerp(StartAngle, EndAngle, (index + 0.5f) / Mathf.Max(1, _words.Length));
            float radius = (InnerRadius + OuterRadius) * 0.5f;
            return Center + Direction(angle) * radius;
        }

        public void PlayReveal()
        {
            _reveal = 0f; _exit = 0f; _exiting = false; _released = false;
            Array.Clear(_hover, 0, _hover.Length);
            TickAnimation(0f);
        }

        public void PlayExit(bool released)
        {
            _exiting = true; _released = released; _exit = 0f;
        }

        public void TickAnimation(float unscaledDelta)
        {
            _clock += unscaledDelta;
            _reveal = Mathf.Min(1f, _reveal + unscaledDelta / RevealDuration);
            if (_exiting) _exit = Mathf.Min(1f, _exit + unscaledDelta / ExitDuration);
            for (int i = 0; i < _hover.Length; i++)
                _hover[i] = Mathf.MoveTowards(_hover[i], i == SelectedIndex && _available[i] ? 1f : 0f, unscaledDelta * 10f);
            LayoutWords();
            MarkDirtyRepaint();
        }

        private void LayoutWords()
        {
            bool attributes = _words.Length == 6;
            Vector2[] sizes = attributes ? AttributeSizes : RelationSizes;
            float[] nativeAngles = attributes ? AttributeAngles : RelationAngles;
            float artScale = (OuterRadius - InnerRadius) / 245f;
            for (int i = 0; i < _labels.Length; i++)
            {
                float progress = Mathf.Clamp01((_reveal - i * .045f) / .775f);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                float hover = _hover[i];
                float angle = Mathf.Lerp(StartAngle, EndAngle, (i + .5f) / _labels.Length);
                Vector2 point = OptionPoint(i) + Direction(angle) * (-18f * (1f - eased) + hover * 5f + (_released ? _exit * 16f : -_exit * 8f));
                Vector2 size = sizes[Mathf.Min(i, sizes.Length - 1)] * artScale;
                float rotation = angle - nativeAngles[Mathf.Min(i, nativeAngles.Length - 1)];
                if (Mirrored) rotation = -rotation;
                float scale = (.82f + eased * .18f + hover * .065f) * (1f + (_released ? _exit * .12f : -_exit * .08f));
                bool active = i < _active.Length && _active[i];
                float pulse = .5f + .5f * Mathf.Sin(_clock * 4f);
                LayoutCard(_cards[i], point, size, rotation, scale);
                LayoutCard(_glows[i], point, size, rotation, scale + .1f + hover * .03f + (_released ? _exit * .25f : 0f));
                _cards[i].style.opacity = eased * (_available[i] ? 1f : .72f);
                _cards[i].style.unityBackgroundImageTintColor = active ? new Color(1f, .83f, .46f)
                    : Color.Lerp(Color.white, new Color(1f, .88f, .62f), hover);
                _glows[i].style.unityBackgroundImageTintColor = new Color(1f, .72f, .26f);
                _glows[i].style.opacity = eased * (active ? .25f : 0f) + hover * (.2f + pulse * .16f) + (_released && i == SelectedIndex ? (1f - _exit) * .5f : 0f);
                _labels[i].style.left = point.x - WordWidth * .5f;
                _labels[i].style.top = point.y - WordHeight * .5f;
                _labels[i].style.opacity = eased;
            }
        }

        private void LayoutCard(VisualElement card, Vector2 point, Vector2 size, float angle, float scale)
        {
            card.style.width = size.x; card.style.height = size.y;
            card.style.left = point.x - size.x * .5f; card.style.top = point.y - size.y * .5f;
            card.style.rotate = new Rotate(Angle.Degrees(angle));
            card.style.scale = new Scale(new Vector3(Mirrored ? -scale : scale, scale, 1f));
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_words.Length == 0 || contentRect.height < 1f || SelectedIndex < 0 || !_available[SelectedIndex]) return;
            var painter = context.painter2D;
            float slice = (EndAngle - StartAngle) / _words.Length;
            float angle = StartAngle + slice * (SelectedIndex + .5f);
            float begin = angle - slice * .32f, end = angle + slice * .32f;
            if (Mirrored) { begin = 180f - begin; end = 180f - end; }
            painter.strokeColor = new Color(1f, .8f, .42f, _hover[SelectedIndex] * .7f);
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(Center, OuterRadius + 7f + (_released ? _exit * 24f : 0f), Angle.Degrees(begin), Angle.Degrees(end),
                Mirrored ? ArcDirection.CounterClockwise : ArcDirection.Clockwise);
            painter.Stroke();
        }
    }
}
