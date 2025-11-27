using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Nexus.Networking
{
    [DisallowMultipleComponent]
    public class NetworkSmoother : MonoBehaviour
    {
        private struct Snapshot
        {
            public double time;
            public Vector3 pos;
            public Quaternion rot;
        }

        [Header("Interpolation")]
        [SerializeField] private double interpolationBackTime = 0.06; // seconds behind real time
        [SerializeField] private double historySeconds = 1.0; // how long to keep history
        [SerializeField] private int maxSnapshots = 64;

        private readonly List<Snapshot> snapshots = new List<Snapshot>(64);

        public bool PauseSmoothing { get; set; }

        public void Clear()
        {
            snapshots.Clear();
        }

        public void SnapTo(Vector3 position, Quaternion rotation)
        {
            transform.SetPositionAndRotation(position, rotation);
            snapshots.Clear();
            Snapshot s;
            s.time = NetworkTime.time;
            s.pos = position;
            s.rot = rotation;
            snapshots.Add(s);
        }

        public void AddSnapshot(double time, Vector3 position, Quaternion rotation)
        {
            Snapshot s;
            s.time = time;
            s.pos = position;
            s.rot = rotation;
            snapshots.Add(s);
            // trim old
            double cutoff = NetworkTime.time - historySeconds;
            int start = 0;
            for (; start < snapshots.Count; start++)
            {
                if (snapshots[start].time >= cutoff) break;
            }
            if (start > 0) snapshots.RemoveRange(0, start);
            if (snapshots.Count > maxSnapshots) snapshots.RemoveRange(0, snapshots.Count - maxSnapshots);
        }

        void LateUpdate()
        {
            if (PauseSmoothing) return;
            if (snapshots.Count == 0) return;
            double renderTime = NetworkTime.time - interpolationBackTime;
            int last = snapshots.Count - 1;
            if (renderTime >= snapshots[last].time)
            {
                transform.position = snapshots[last].pos;
                transform.rotation = snapshots[last].rot;
                return;
            }
            for (int i = snapshots.Count - 2; i >= 0; --i)
            {
                if (renderTime >= snapshots[i].time)
                {
                    Snapshot lhs = snapshots[i];
                    Snapshot rhs = snapshots[i + 1];
                    double span = rhs.time - lhs.time;
                    float t = span > 0.0001 ? (float)((renderTime - lhs.time) / span) : 0f;
                    transform.position = Vector3.Lerp(lhs.pos, rhs.pos, t);
                    transform.rotation = Quaternion.Slerp(lhs.rot, rhs.rot, t);
                    return;
                }
            }
            transform.position = snapshots[0].pos;
            transform.rotation = snapshots[0].rot;
        }
    }
}
