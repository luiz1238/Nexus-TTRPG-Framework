using System;
using UnityEngine;

namespace Nexus
{
    public class NdiCaptureBootstrap : MonoBehaviour
    {
        public string streamName = "Nexus Capture";
        public CaptureTap tap;

        void Awake()
        {
            if (!tap) tap = FindObjectOfType<CaptureTap>();
            if (!tap) tap = gameObject.AddComponent<CaptureTap>();
        }

        void Start()
        {
            var ndiType = FindNdiSenderType();
            if (ndiType == null)
            {
                Debug.LogWarning("NdiCaptureBootstrap: Klak.Ndi.NdiSender not found. Install Klak.Ndi.");
                return;
            }

            var go = new GameObject("NDI Sender");
            DontDestroyOnLoad(go);
            var sender = go.AddComponent(ndiType) as Component;

            var binder = go.AddComponent<NdiTextureBinder>();
            binder.captureTap = tap;
            binder.ndiSender = sender;
            binder.streamName = streamName;
        }

        static Type FindNdiSenderType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Klak.Ndi.NdiSender");
                if (t != null) return t;
            }
            return null;
        }
    }
}
