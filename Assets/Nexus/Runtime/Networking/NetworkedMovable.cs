using UnityEngine;
using Mirror;
using System.Collections.Generic;

namespace Nexus.Networking
{
    public class NetworkedMovable : NetworkBehaviour
    {
        private Rigidbody cachedRb;
        private NetworkSmoother smoother;
        [SerializeField] private float positionThreshold = 0.01f;
        [SerializeField] private float rotationThreshold = 1f;

        private Vector3 syncPosition;
        private Quaternion syncRotation;
        [SyncVar(hook = nameof(OnDraggingChanged))]
        private bool syncDragging;

        // The client who is currently dragging this object (0 when none)
        [SyncVar]
        private uint dragOwnerNetId;

        // Local-only flag so the initiating client can immediately take visual control
        private bool localDragOwner;

        private void Awake()
        {
            cachedRb = GetComponent<Rigidbody>();
            smoother = GetComponent<NetworkSmoother>();
            if (smoother == null) smoother = gameObject.AddComponent<NetworkSmoother>();
            this.syncInterval = 0.02f; // ~50 Hz for smooth streaming
        }

        private void Start()
        {
            if (NetworkServer.active)
            {
                syncPosition = transform.position;
                syncRotation = transform.rotation;
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!NetworkServer.active && smoother != null)
            {
                smoother.Clear();
            }
        }

        private void Update()
        {
            if (NetworkServer.active)
            {
                if (Vector3.Distance(transform.position, syncPosition) > positionThreshold)
                {
                    syncPosition = transform.position;
                }
                if (Quaternion.Angle(transform.rotation, syncRotation) > rotationThreshold)
                {
                    syncRotation = transform.rotation;
                }
                // rely on default sync interval for send frequency
            }
            else
            {
                // If this local client is the drag owner, don't override their local visual control
                uint localId = Mirror.NetworkClient.localPlayer != null ? Mirror.NetworkClient.localPlayer.netId : 0u;
                bool iAmDragOwner = syncDragging && dragOwnerNetId != 0u && localId == dragOwnerNetId;
                if (smoother != null)
                {
                    smoother.PauseSmoothing = (localDragOwner || iAmDragOwner);
                }
            }
        }

        private void OnPositionChanged(Vector3 oldPos, Vector3 newPos)
        {
            if (!NetworkServer.active) { }
        }

        private void OnRotationChanged(Quaternion oldRot, Quaternion newRot)
        {
            if (!NetworkServer.active) { }
        }

        [Command(requiresAuthority = false)]
        public void CmdBeginDrag()
        {
            if (!NetworkServer.active) return;
            syncDragging = true;
            dragOwnerNetId = (connectionToClient != null && connectionToClient.identity != null) ? connectionToClient.identity.netId : 0u;
            RpcClearSnapshots();
        }

        [Command(requiresAuthority = false)]
        public void CmdEndDragFinal(Vector3 finalPosition, Quaternion finalRotation)
        {
            if (!NetworkServer.active) return;
            
            syncDragging = false;
            dragOwnerNetId = 0u;
            
            if (cachedRb != null)
            {
                cachedRb.position = finalPosition;
                cachedRb.rotation = finalRotation;
            }
            else
            {
                transform.SetPositionAndRotation(finalPosition, finalRotation);
            }
            syncPosition = finalPosition;
            syncRotation = finalRotation;
            
            RpcSnapTo(finalPosition, finalRotation);
        }

        [ClientRpc]
        private void RpcSnapTo(Vector3 position, Quaternion rotation)
        {
            // Clear local drag owner NOW so we accept this snap
            localDragOwner = false;
            
            if (cachedRb != null)
            {
                cachedRb.position = position;
                cachedRb.rotation = rotation;
            }
            transform.SetPositionAndRotation(position, rotation);
            if (smoother != null)
            {
                smoother.SnapTo(position, rotation);
            }
        }
        
        [ClientRpc]
        private void RpcClearSnapshots()
        {
            if (smoother != null)
            {
                smoother.Clear();
            }
        }

        private void OnDraggingChanged(bool oldVal, bool newVal)
        {
            // Don't clear localDragOwner here - wait for RpcSnapTo
        }

        public void SetLocalDragOwner(bool value)
        {
            localDragOwner = value;
        }

        [Server]
        public void ServerMoveTo(Vector3 targetPosition)
        {
            transform.position = targetPosition;
            syncPosition = targetPosition;
        }

        // Snapshot buffer and serialization like NetworkedToken
        private struct Snapshot
        {
            public double time;
            public Vector3 pos;
            public Quaternion rot;
        }
        private readonly List<Snapshot> snapshots = new List<Snapshot>(64);
        [SerializeField] private double interpolationBackTime = 0.06; // seconds to interpolate behind real time
        private Vector3 lastSentPos;
        private Quaternion lastSentRot;
        private const float sendPosEpsilon = 0.0005f;
        private const float sendRotEpsilon = 0.25f; // degrees

        public override void OnSerialize(Mirror.NetworkWriter writer, bool initialState)
        {
            double now = Mirror.NetworkTime.time;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            writer.WriteDouble(now);
            writer.WriteVector3(pos);
            writer.WriteQuaternion(rot);
            lastSentPos = pos;
            lastSentRot = rot;
            base.OnSerialize(writer, initialState);
        }

        public override void OnDeserialize(Mirror.NetworkReader reader, bool initialState)
        {
            double t = reader.ReadDouble();
            Vector3 pos = reader.ReadVector3();
            Quaternion rot = reader.ReadQuaternion();
            syncPosition = pos;
            syncRotation = rot;
            if (!NetworkServer.active && smoother != null)
            {
                smoother.AddSnapshot(t, pos, rot);
            }
            base.OnDeserialize(reader, initialState);
        }

        private void ApplySnapshotInterpolation()
        {
            if (snapshots.Count == 0) return;
            double renderTime = Mirror.NetworkTime.time - interpolationBackTime;
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
