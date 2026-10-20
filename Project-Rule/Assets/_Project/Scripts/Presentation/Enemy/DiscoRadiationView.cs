using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Enemy;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>连续的可见区域网格；伤害不是有限条射线，光区不在射线之间留下安全缝。</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class DiscoRadiationView : MonoBehaviour
    {
        public EnemyActor Actor;
        private const int Rays = 256, MaxAngles = Rays + 32 * 12;
        private readonly List<float> _angles = new List<float>(MaxAngles);
        private readonly Collider2D[] _obstacles = new Collider2D[32];
        private readonly Vector3[] _vertices = new Vector3[MaxAngles + 2];
        private readonly Color[] _colors = new Color[MaxAngles + 2];
        private readonly int[] _triangles = new int[MaxAngles * 3];
        private MaterialPropertyBlock _properties;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private void Awake()
        {
            if (Actor == null) Actor = GetComponentInParent<EnemyActor>();
            _renderer = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "Disco visibility" }; _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            for (int i = 0; i < MaxAngles; i++)
            { _triangles[i * 3] = 0; _triangles[i * 3 + 1] = i + 1; _triangles[i * 3 + 2] = i + 2; }
        }
        private void LateUpdate()
        {
            var brain = Actor != null ? Actor.Brain as DiscoBallBrain : null;
            bool active = brain != null && !Actor.Defeated && Actor.gameObject.activeInHierarchy
                && (brain.Radiating || brain.StateName == "Warning");
            _renderer.enabled = active;
            if (!active) return;
            Vector2 center = Actor.transform.position;
            _angles.Clear();
            for (int i = 0; i < Rays; i++) _angles.Add(i * Mathf.PI * 2f / Rays);
            var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(Actor.Motor.SolidMask);
            int count = Physics2D.OverlapCircle(center, Actor.Definition.RadiationRadius, filter, _obstacles);
            for (int i = 0; i < count; i++)
            {
                // 额外沿平台/墙的实际角点采样，保留窄掩体背后的清晰阴影边缘。
                var box = _obstacles[i] as BoxCollider2D;
                Bounds bounds = _obstacles[i].bounds;
                for (int y = -1; y <= 1; y += 2)
                    for (int x = -1; x <= 1; x += 2)
                    {
                        Vector2 corner = box != null
                            ? (Vector2)box.transform.TransformPoint(box.offset + box.size * new Vector2(x, y) * .5f)
                            : (Vector2)bounds.center + new Vector2(bounds.extents.x * x, bounds.extents.y * y);
                        float angle = Mathf.Atan2(corner.y - center.y, corner.x - center.x);
                        for (int k = -1; k <= 1; k++) _angles.Add(Mathf.Repeat(angle + k * .0003f, Mathf.PI * 2f));
                    }
            }
            _angles.Sort();
            _vertices[0] = transform.InverseTransformPoint(center); _colors[0] = Color.white;
            for (int i = 0; i <= _angles.Count; i++)
            {
                float angle = _angles[i % _angles.Count];
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                _vertices[i + 1] = transform.InverseTransformPoint(center + direction * brain.Reach(direction));
                _colors[i + 1] = new Color(1f, 1f, 1f, .65f);
            }
            _mesh.Clear(); _mesh.SetVertices(_vertices, 0, _angles.Count + 2);
            _mesh.SetColors(_colors, 0, _angles.Count + 2);
            _mesh.SetTriangles(_triangles, 0, _angles.Count * 3, 0, false);
            _mesh.RecalculateBounds();
            _properties.SetFloat("_Phase", Actor.Brain.MechanicsAge);
            _properties.SetFloat("_Strength", brain.Radiating ? 1f : .2f);
            _properties.SetVector("_Origin", center);
            _renderer.SetPropertyBlock(_properties);
        }
        private void OnDisable() { if (_renderer != null) _renderer.enabled = false; }
        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }
}
