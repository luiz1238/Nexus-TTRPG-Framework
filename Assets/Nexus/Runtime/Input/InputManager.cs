using UnityEngine;

namespace Nexus
{
    public enum InputAction
    {
        NextScene,
        PrevScene,
        ReloadScene,
        TokenState1,
        TokenState2,
        TokenState3,
        TokenState4,
        TokenState5,
        TokenState6,
        TokenScaleUp,
        TokenScaleDown
    }

    public class InputManager : MonoBehaviour
    {
        private static InputManager _instance;
        public static InputManager Instance
        {
            get
            {
                if (_instance == null) _instance = FindObjectOfType<InputManager>();
                if (_instance == null)
                {
                    var go = new GameObject("InputManager");
                    _instance = go.AddComponent<InputManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        [SerializeField] private KeyCode[] nextScene = new KeyCode[] { KeyCode.N };
        [SerializeField] private KeyCode[] prevScene = new KeyCode[] { KeyCode.P };
        [SerializeField] private KeyCode[] reloadScene = new KeyCode[] { KeyCode.R };

        [SerializeField] private KeyCode[] tokenState1 = new KeyCode[] { KeyCode.Alpha1, KeyCode.Keypad1 };
        [SerializeField] private KeyCode[] tokenState2 = new KeyCode[] { KeyCode.Alpha2, KeyCode.Keypad2 };
        [SerializeField] private KeyCode[] tokenState3 = new KeyCode[] { KeyCode.Alpha3, KeyCode.Keypad3 };
        [SerializeField] private KeyCode[] tokenState4 = new KeyCode[] { KeyCode.Alpha4, KeyCode.Keypad4 };
        [SerializeField] private KeyCode[] tokenState5 = new KeyCode[] { KeyCode.Alpha5, KeyCode.Keypad5 };
        [SerializeField] private KeyCode[] tokenState6 = new KeyCode[] { KeyCode.Alpha6, KeyCode.Keypad6 };

        [SerializeField] private KeyCode[] tokenScaleUp = new KeyCode[] { KeyCode.Equals, KeyCode.Plus, KeyCode.KeypadPlus };
        [SerializeField] private KeyCode[] tokenScaleDown = new KeyCode[] { KeyCode.Minus, KeyCode.KeypadMinus };

        void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        public bool GetDown(InputAction action)
        {
            switch (action)
            {
                case InputAction.NextScene: return AnyDown(nextScene);
                case InputAction.PrevScene: return AnyDown(prevScene);
                case InputAction.ReloadScene: return AnyDown(reloadScene);
                case InputAction.TokenState1: return AnyDown(tokenState1);
                case InputAction.TokenState2: return AnyDown(tokenState2);
                case InputAction.TokenState3: return AnyDown(tokenState3);
                case InputAction.TokenState4: return AnyDown(tokenState4);
                case InputAction.TokenState5: return AnyDown(tokenState5);
                case InputAction.TokenState6: return AnyDown(tokenState6);
                case InputAction.TokenScaleUp: return AnyDown(tokenScaleUp);
                case InputAction.TokenScaleDown: return AnyDown(tokenScaleDown);
            }
            return false;
        }

        private static bool AnyDown(KeyCode[] keys)
        {
            if (keys == null) return false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Input.GetKeyDown(keys[i])) return true;
            }
            return false;
        }
    }
}
