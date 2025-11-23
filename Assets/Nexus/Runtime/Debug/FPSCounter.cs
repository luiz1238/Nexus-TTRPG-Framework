using UnityEngine;

namespace Nexus.Debugging
{
    public class FPSCounter : MonoBehaviour
    {
        const string GO_NAME = "[FPSCounter]";

        [Header("Display")] 
        [SerializeField] bool show = true;
        [SerializeField] KeyCode toggleKey = KeyCode.F2;
        [SerializeField] int fontSize = 14;
        [SerializeField] Color textColor = Color.white;
        [SerializeField] Color backgroundColor = new Color(0, 0, 0, 0.4f);
        [SerializeField] Vector2 padding = new Vector2(6, 4);
        [SerializeField] Vector2 offset = new Vector2(10, 10);

        [Header("Smoothing")]
        [SerializeField] int sampleCount = 50;
        [SerializeField] float lowPass = 0.1f;

        float accumDt;
        int frames;
        float fps;
        float minFps = float.MaxValue;
        float maxFps = 0f;

        GUIStyle style;
        Texture2D bgTex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (FindObjectOfType<FPSCounter>() != null)
                return;
            var go = new GameObject(GO_NAME);
            DontDestroyOnLoad(go);
            go.AddComponent<FPSCounter>();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) show = !show;

            float dt = Time.unscaledDeltaTime;
            accumDt += dt;
            frames++;

            // Update FPS at a stable cadence using a simple low pass
            if (frames >= Mathf.Max(1, sampleCount))
            {
                float instant = frames / Mathf.Max(0.0001f, accumDt);
                fps = Mathf.Lerp(fps <= 0 ? instant : fps, instant, lowPass);
                minFps = Mathf.Min(minFps, fps);
                maxFps = Mathf.Max(maxFps, fps);
                frames = 0;
                accumDt = 0f;
            }
        }

        void EnsureGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.fontSize = Mathf.Max(10, fontSize);
                style.normal.textColor = textColor;
                style.alignment = TextAnchor.UpperLeft;
            }
            if (bgTex == null)
            {
                bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                bgTex.SetPixel(0, 0, backgroundColor);
                bgTex.Apply();
            }
        }

        void OnGUI()
        {
            if (!show) return;
            EnsureGUI();

            string txt = string.Format("FPS: {0:0.0}\nMin: {1:0.0}  Max: {2:0.0}", fps, minFps == float.MaxValue ? 0f : minFps, maxFps);
            Vector2 size = style.CalcSize(new GUIContent(txt));
            Rect rect = new Rect(offset.x, offset.y, size.x + padding.x * 2f, size.y + padding.y * 2f);

            var old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, bgTex);
            GUI.color = old;
            Rect ir = new Rect(rect.x + padding.x, rect.y + padding.y, rect.width - padding.x * 2f, rect.height - padding.y * 2f);
            GUI.Label(ir, txt, style);
        }

        void OnDestroy()
        {
            if (bgTex != null)
            {
                Destroy(bgTex);
                bgTex = null;
            }
        }
    }
}
