using System;
using System.Reflection;
using UnityEngine;

namespace Nexus
{
    // Minimal binder: assigns CaptureTap's RenderTexture to a native Klak.Ndi.NdiSender (Texture method) and sets stream name.
    public class NdiTextureBinder : MonoBehaviour
    {
        public CaptureTap captureTap;
        public Component ndiSender; // Assign Klak.Ndi.NdiSender in the Inspector
        public string streamName = "Nexus Capture";

        Texture _last;

        void OnEnable()
        {
            ApplyStaticSettings();
        }

        void Update()
        {
            if (ndiSender == null || captureTap == null) return;
            var tex = captureTap.GetTexture();
            if (tex == null || ReferenceEquals(tex, _last)) return;
            SetEnumByName(ndiSender, new[] { "captureMethod", "sourceType", "source", "method", "mode" }, "Texture");
            SetObjectMember(ndiSender, new[] { "sourceTexture", "inputTexture", "texture", "targetTexture" }, tex);
            _last = tex;
        }

        void ApplyStaticSettings()
        {
            if (ndiSender == null) return;
            SetStringMember(ndiSender, new[] { "ndiName", "senderName", "sourceName", "name" }, streamName);
            SetEnumByName(ndiSender, new[] { "captureMethod", "sourceType", "source", "method", "mode" }, "Texture");
        }

        static bool SetStringMember(object obj, string[] names, string value) => SetMember(obj, names, value);
        static bool SetObjectMember(object obj, string[] names, object value) => SetMember(obj, names, value);

        static bool SetEnumByName(object obj, string[] names, string enumName)
        {
            var t = obj.GetType();
            foreach (var n in names)
            {
                var p = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    var pt = p.PropertyType;
                    if (pt.IsEnum)
                    {
                        foreach (var en in Enum.GetNames(pt))
                        {
                            if (string.Equals(en, enumName, StringComparison.OrdinalIgnoreCase))
                            {
                                try { p.SetValue(obj, Enum.Parse(pt, en)); return true; } catch { }
                            }
                        }
                    }
                }
                var f = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    var ft = f.FieldType;
                    if (ft.IsEnum)
                    {
                        foreach (var en in Enum.GetNames(ft))
                        {
                            if (string.Equals(en, enumName, StringComparison.OrdinalIgnoreCase))
                            {
                                try { f.SetValue(obj, Enum.Parse(ft, en)); return true; } catch { }
                            }
                        }
                    }
                }
            }
            return false;
        }

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
