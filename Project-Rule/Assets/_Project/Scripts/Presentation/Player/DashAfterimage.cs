using System.Collections.Generic;
using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure.Time;
using UnityEngine;

namespace RuleGame.Presentation.Player
{
    /// <summary>
    /// 冲刺残影：冲刺期间每隔一小段复制一次当前外观，留在原地淡出。
    /// </summary>
    /// <remarks>
    /// <para><b>速度感来自拖影，不来自把速度调高</b>。冲刺的实际位移与时长由
    /// <see cref="PlayerController"/> 决定，这里只读 <see cref="PlayerController.IsDashing"/>，
    /// 不参与移动、不改速度、不认识规则系统。</para>
    /// <para>残影是纯表现：没有碰撞体、不挡路、不进世界快照；暂停时跟着冻住。</para>
    /// </remarks>
    [DefaultExecutionOrder(50), DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    public sealed class DashAfterimage : MonoBehaviour
    {
        [Header("生成节奏")]
        [Tooltip("每隔多少秒留一个残影。0.035 ≈ 一次冲刺留 6~8 个。")]
        [SerializeField, Min(0.005f)] private float _interval = 0.035f;

        [Tooltip("单个残影从满到消失的秒数。")]
        [SerializeField, Min(0.02f)] private float _lifetime = 0.22f;

        [Tooltip("同时存在的上限。超过就回收最老的那个。")]
        [SerializeField, Range(1, 16)] private int _maxAlive = 8;

        [Header("外观")]
        [Tooltip("残影染色。默认偏暖红，和角色红围巾同色系。")]
        [SerializeField] private Color _tint = new Color(1f, 0.62f, 0.68f, 1f);

        [Tooltip("生成瞬间的不透明度。")]
        [SerializeField, Range(0f, 1f)] private float _startAlpha = 0.5f;

        [Tooltip("相对本体的排序层偏移。-1 = 压在角色下面。")]
        [SerializeField] private int _sortingOrderOffset = -1;

        private PlayerController _player;
        private EntityStatus     _status;
        private SpriteRenderer   _source;

        private readonly List<Ghost> _ghosts = new List<Ghost>();
        private float _timer;

        private sealed class Ghost
        {
            public GameObject     Go;
            public SpriteRenderer Renderer;
            public float          Age;
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _status = GetComponent<EntityStatus>();
            _source = GetComponentInChildren<SpriteRenderer>();
        }

        private void OnDisable() => Clear();

        private void Update()
        {
            if (_source == null) return;
            if (_status != null && _status.IsPaused) return;
            if (TimeManager.Instance != null && TimeManager.Instance.IsPaused) return;

            float dt = Time.deltaTime;

            Tick(dt);

            if (!_player.IsDashing)
            {
                _timer = 0f;
                return;
            }

            _timer -= dt;
            // while 而不是 if：掉帧时一次补足，残影间距才不会忽大忽小。
            int guard = 0;
            while (_timer <= 0f && guard++ < _maxAlive)
            {
                Spawn();
                _timer += _interval;
            }
        }

        private void Tick(float dt)
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                Ghost g = _ghosts[i];
                if (!g.Go.activeSelf) continue;

                g.Age += dt;
                float t = g.Age / _lifetime;
                if (t >= 1f)
                {
                    g.Go.SetActive(false);
                    continue;
                }

                Color c = _tint;
                c.a = _startAlpha * (1f - t);
                g.Renderer.color = c;
            }
        }

        private void Spawn()
        {
            if (_source.sprite == null) return;

            Ghost g = Rent();
            if (g == null) return;

            Transform src = _source.transform;
            Transform dst = g.Go.transform;
            dst.SetPositionAndRotation(src.position, src.rotation);
            dst.localScale = src.lossyScale;

            SpriteRenderer r = g.Renderer;
            r.sprite         = _source.sprite;
            r.flipX          = _source.flipX;
            r.flipY          = _source.flipY;
            r.sortingLayerID = _source.sortingLayerID;
            r.sortingOrder   = _source.sortingOrder + _sortingOrderOffset;

            Color c = _tint;
            c.a = _startAlpha;
            r.color = c;

            g.Age = 0f;
            g.Go.SetActive(true);
        }

        private Ghost Rent()
        {
            for (int i = 0; i < _ghosts.Count; i++)
                if (!_ghosts[i].Go.activeSelf) return _ghosts[i];

            if (_ghosts.Count >= _maxAlive)
            {
                // 池满：把最老的顶掉，保持上限。
                Ghost oldest = _ghosts[0];
                for (int i = 1; i < _ghosts.Count; i++)
                    if (_ghosts[i].Age > oldest.Age) oldest = _ghosts[i];
                return oldest;
            }

            var go = new GameObject("DashGhost");
            go.transform.SetParent(transform.parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = _source.sharedMaterial;

            var ghost = new Ghost { Go = go, Renderer = sr };
            _ghosts.Add(ghost);
            return ghost;
        }

        private void Clear()
        {
            for (int i = 0; i < _ghosts.Count; i++)
                if (_ghosts[i].Go != null) Destroy(_ghosts[i].Go);
            _ghosts.Clear();
            _timer = 0f;
        }
    }
}
