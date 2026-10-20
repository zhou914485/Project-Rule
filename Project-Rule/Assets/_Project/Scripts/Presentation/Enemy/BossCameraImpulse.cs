using UnityEngine;
using UnityEngine.Rendering;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Presentation.Enemy
{
    /// <summary>仅在相机渲染期间偏移视图矩阵，结束后复原，不与跟随或规则特写抢 Transform。</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class BossCameraImpulse : MonoBehaviour
    {
        private static event System.Action<float, float> Requested;
        private static event System.Action<Vector2, float, float> KickRequested;
        private static event System.Action KickCancelled;
        private Camera _camera;
        private float _strength, _duration, _age;
        private Vector2 _kickDirection;
        private float _kickStrength, _kickDuration, _kickAge;
        private bool _applied;
        public static void Pulse(float strength, float duration) => Requested?.Invoke(strength, duration);
        /// <summary>受击方向上的一记短推，随后快速回正；与 Boss 震动独立合成。</summary>
        public static void Kick(Vector2 direction, float strength, float duration)
            => KickRequested?.Invoke(direction, strength, duration);
        public static void CancelKick() => KickCancelled?.Invoke();
        private void Awake() => _camera = GetComponent<Camera>();
        private void OnEnable()
        {
            Requested += Begin;
            KickRequested += BeginKick;
            KickCancelled += ClearKick;
            RenderPipelineManager.beginCameraRendering += BeforeRender;
            RenderPipelineManager.endCameraRendering += AfterRender;
        }
        private void OnDisable()
        {
            Requested -= Begin;
            KickRequested -= BeginKick;
            KickCancelled -= ClearKick;
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            RenderPipelineManager.endCameraRendering -= AfterRender;
            Restore(); _strength = _kickStrength = 0f;
        }
        private void Begin(float strength, float duration)
        {
            _strength = Mathf.Max(_strength, strength); _duration = Mathf.Max(.01f, duration); _age = 0f;
        }
        private void BeginKick(Vector2 direction, float strength, float duration)
        {
            _kickDirection = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
            _kickStrength = Mathf.Max(0f, strength);
            _kickDuration = Mathf.Max(.01f, duration);
            _kickAge = 0f;
        }
        private void ClearKick() { _kickStrength = 0f; Restore(); }
        private void Update()
            => Advance(Time.deltaTime);

        private void Advance(float deltaTime)
        {
            if (TimeManager.Instance?.IsPaused == true) return;
            _age += deltaTime;
            if (_age >= _duration) _strength = 0f;
            _kickAge += deltaTime;
            if (_kickAge >= _kickDuration) _kickStrength = 0f;
        }
        private void BeforeRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || (_strength <= 0f && _kickStrength <= 0f) || TimeManager.Instance?.IsPaused == true) return;
            Matrix4x4 view = camera.worldToCameraMatrix;
            float fade = 1f - Mathf.Clamp01(_age / Mathf.Max(.01f, _duration));
            Vector3 offset = new Vector3(Mathf.Sin(_age * 173f), Mathf.Cos(_age * 139f) * .6f, 0f) * (_strength * fade * fade);
            float kickFade = 1f - Mathf.Clamp01(_kickAge / Mathf.Max(.01f, _kickDuration));
            Vector2 tangent = new Vector2(-_kickDirection.y, _kickDirection.x);
            Vector2 kick = _kickDirection * (Mathf.Exp(-_kickAge * 35f) - .13f * Mathf.Sin(_kickAge * 32f))
                + tangent * (Mathf.Sin(_kickAge * 97f) * .17f * kickFade);
            offset += (Vector3)kick * (_kickStrength * kickFade);
            camera.worldToCameraMatrix = Matrix4x4.Translate(offset) * view; _applied = true;
        }
        private void AfterRender(ScriptableRenderContext context, Camera camera) { if (camera == _camera) Restore(); }
        private void Restore()
        {
            // 写回旧矩阵仍处于手动模式，Transform 后续移动不会更新画面；必须归还自动视图计算。
            if (_applied && _camera != null) _camera.ResetWorldToCameraMatrix();
            _applied = false;
        }
    }
}
