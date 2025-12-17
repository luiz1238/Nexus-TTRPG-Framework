using System;
using System.Reflection;
using UnityEngine;

namespace Nexus
{
    // Minimal binder: assigns CaptureTap's RenderTexture to a native Klak.Spout.SpoutSender and sets sender name.
    public class SpoutTextureBinder : MonoBehaviour
    {
        public CaptureTap captureTap;
        [Tooltip("GameObject that has the Klak.Spout.SpoutSender component")]
        public GameObject spoutSenderObject;
        public string senderName = "Nexus Capture";

        Component _spoutSender;
        Texture _last;

        void OnEnable()
        {
            EnsureSenderComponent();
            ApplyStaticSettings();
        }

        void Update()
        {
            EnsureSenderComponent();
            if (_spoutSender == null || captureTap == null) return;
            var tex = captureTap.GetTexture();
            if (tex == null || ReferenceEquals(tex, _last)) return;
            SetObjectMember(_spoutSender, new[] { "sourceTexture", "inputTexture", "texture", "targetTexture" }, tex);
            _last = tex;
        }

        void EnsureSenderComponent()
        {
            if (_spoutSender != null) return;
            if (spoutSenderObject == null) return;

            // Try to get the Klak.Spout.SpoutSender component from the assigned GameObject
            var comp = spoutSenderObject.GetComponent("Klak.Spout.SpoutSender");
            if (comp is Component c)
            {
                _spoutSender = c;
            }
        }

        void ApplyStaticSettings()
        {
            if (_spoutSender == null) return;
            SetStringMember(_spoutSender, new[] { "senderName", "spoutName", "sourceName", "name" }, senderName);
        }

        static bool SetStringMember(object obj, string[] names, string value) => SetMember(obj, names, value);
        static bool SetObjectMember(object obj, string[] names, object value) => SetMember(obj, names, value);

        static bool SetMember(object obj, string[] names, object value)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite)
                {
                    if (value == null || p.PropertyType.IsInstanceOfType(value))
                    {
                        try { p.SetValue(obj, value); return true; } catch { }
                    }
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    if (value == null || f.FieldType.IsInstanceOfType(value))
                    {
                        try { f.SetValue(obj, value); return true; } catch { }
                    }
                }
            }
            return false;
        }
    }
}
