using UnityEngine;
using UnityEngine.Rendering;

namespace Nexus
{
    public class CaptureTap : MonoBehaviour
    {
        public int width = 1920;
        public int height = 1080;
        public Camera explicitCamera;

        public RenderTexture CaptureTexture { get; private set; }

        Camera _currentCamera;
        CommandBuffer _cb;
        int _lastW;
        int _lastH;

        void OnEnable()
        {
            TryAttach();
        }

        void OnDisable()
        {
            Detach();
            ReleaseRT();
        }

        void Update()
        {
            var cam = ResolveCamera();
            if (cam != _currentCamera)
            {
                Detach();
                TryAttach();
                return;
            }

            if (_currentCamera == null) return;

            var size = ComputeSize();
            if (size.x != _lastW || size.y != _lastH)
            {
                SetupRT(size.x, size.y);
            }
        }

        Camera ResolveCamera()
        {
            if (explicitCamera != null && explicitCamera.isActiveAndEnabled) return explicitCamera;
            if (Nexus.CameraManager.Instance != null && Nexus.CameraManager.Instance.MainCamera != null)
                return Nexus.CameraManager.Instance.MainCamera;
            if (Camera.main != null) return Camera.main;
            return null;
        }

        Vector2Int ComputeSize()
        {
            int w = Mathf.Max(1, width);
            int h = Mathf.Max(1, height);
            return new Vector2Int(w, h);
        }

        void TryAttach()
        {
            _currentCamera = ResolveCamera();
            if (_currentCamera == null)
            {
                return;
            }

            var size = ComputeSize();
            SetupRT(size.x, size.y);

            _cb = new CommandBuffer();
            _cb.name = "CaptureTap Blit";
            _cb.Blit(BuiltinRenderTextureType.CameraTarget, CaptureTexture);
            _currentCamera.AddCommandBuffer(CameraEvent.AfterImageEffects, _cb);
        }

        void Detach()
        {
            if (_currentCamera != null && _cb != null)
            {
                _currentCamera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, _cb);
            }
            if (_cb != null)
            {
                _cb.Release();
                _cb = null;
            }
            _currentCamera = null;
        }

        void SetupRT(int w, int h)
        {
            _lastW = w;
            _lastH = h;

            if (CaptureTexture != null)
            {
                if (CaptureTexture.width == w && CaptureTexture.height == h) return;
                ReleaseRT();
            }

            var desc = new RenderTextureDescriptor(w, h, RenderTextureFormat.ARGB32, 0);
            desc.msaaSamples = 1;
            desc.sRGB = (QualitySettings.activeColorSpace == ColorSpace.Linear);
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.depthBufferBits = 0;

            CaptureTexture = new RenderTexture(desc);
            CaptureTexture.name = "CaptureTap_RT";
            CaptureTexture.filterMode = FilterMode.Bilinear;
            CaptureTexture.Create();

            if (_cb != null)
            {
                _cb.Clear();
                _cb.Blit(BuiltinRenderTextureType.CameraTarget, CaptureTexture);
            }
        }

        void ReleaseRT()
        {
            if (CaptureTexture != null)
            {
                if (Application.isPlaying) Destroy(CaptureTexture); else DestroyImmediate(CaptureTexture);
                CaptureTexture = null;
            }
        }

        public RenderTexture GetTexture()
        {
            return CaptureTexture;
        }
    }
}
