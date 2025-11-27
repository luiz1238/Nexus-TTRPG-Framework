using UnityEngine;
using Mirror;
using System.Collections.Generic;

namespace Nexus.Networking
{
    /// <summary>
    /// Network synchronization for tokens
    /// Syncs position, rotation, scale, and state across all clients
    /// </summary>
    [RequireComponent(typeof(TokenSetup))]
    public class NetworkedToken : NetworkBehaviour
    {
        private TokenSetup tokenSetup;
        private Rigidbody cachedRb;
        private NetworkSmoother smoother;
        
        [Header("Sync Settings")]
        [SerializeField] private float positionThreshold = 0.01f;
        [SerializeField] private float rotationThreshold = 1f;
        
        // Synced variables
        private Vector3 syncPosition;
        private Quaternion syncRotation;
        
        [SyncVar(hook = nameof(OnScaleChanged))]
        private float syncScale;
        
        [SyncVar(hook = nameof(OnStateChanged))]
        private int syncState;

        [SyncVar(hook = nameof(OnDraggingChanged))]
        private bool syncDragging;

        [SyncVar(hook = nameof(OnLockedChanged))]
        private bool syncLocked;

        // The client who is currently dragging this token (0 when none)
        [SyncVar]
        private uint dragOwnerNetId;

        // Local-only flag so the initiating client can immediately take visual control
        private bool localDragOwner;

        // Client-side interpolation buffer
        private struct Snapshot
        {
            public double time;
            public Vector3 pos;
            public Quaternion rot;
        }

        public override void OnSerialize(Mirror.NetworkWriter writer, bool initialState)
        {
            // send current time, pos, rot using unreliable channel
            double now = Mirror.NetworkTime.time;
            Vector3 pos = transform.position;
            Quaternion rot = transform.rotation;
            writer.WriteDouble(now);
            writer.WriteVector3(pos);
            writer.WriteQuaternion(rot);
            lastSentPos = pos;
            lastSentRot = rot;
            // then serialize SyncVars
            base.OnSerialize(writer, initialState);
        }

        public override void OnDeserialize(Mirror.NetworkReader reader, bool initialState)
        {
            double t = reader.ReadDouble();
            Vector3 pos = reader.ReadVector3();
            Quaternion rot = reader.ReadQuaternion();
            syncPosition = pos;
            syncRotation = rot;
            // forward snapshot to shared smoother
            if (!NetworkServer.active && smoother != null)
            {
                smoother.AddSnapshot(t, pos, rot);
            }
            // then deserialize SyncVars
            base.OnDeserialize(reader, initialState);
        }
        private readonly List<Snapshot> snapshots = new List<Snapshot>(64);
        [SerializeField] private double interpolationBackTime = 0.06; // seconds to interpolate behind real time
        private Vector3 lastSentPos;
        private Quaternion lastSentRot;
        private const float sendPosEpsilon = 0.0005f;
        private const float sendRotEpsilon = 0.25f; // degrees

        public bool IsLocked => syncLocked;

        private void Awake()
        {
            tokenSetup = GetComponent<TokenSetup>();
            cachedRb = GetComponent<Rigidbody>();
            smoother = GetComponent<NetworkSmoother>();
            if (smoother == null) smoother = gameObject.AddComponent<NetworkSmoother>();
            this.syncInterval = 0.02f; // ~50 Hz
        }

        private void Start()
        {
            // Initialize sync vars with current values
            if (NetworkServer.active && tokenSetup != null)
            {
                syncPosition = transform.position;
                syncRotation = transform.rotation;
                syncScale = tokenSetup.GetCurrentScale();
                syncState = tokenSetup.GetCurrentState();
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
            // Server updates sync vars when values change
            if (NetworkServer.active && tokenSetup != null)
            {
                if (Vector3.Distance(transform.position, syncPosition) > positionThreshold)
                {
                    syncPosition = transform.position;
                }

                if (Quaternion.Angle(transform.rotation, syncRotation) > rotationThreshold)
                {
                    syncRotation = transform.rotation;
                }

                float currentScale = tokenSetup.GetCurrentScale();
                if (Mathf.Abs(currentScale - syncScale) > 0.01f)
                {
                    syncScale = currentScale;
                }

                int currentState = tokenSetup.GetCurrentState();
                if (currentState != syncState)
                {
                    syncState = currentState;
                }
                // rely on syncInterval for send frequency
            }
            // Clients interpolate to sync values
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

        // Hook methods called when sync vars change
        private void OnPositionChanged(Vector3 oldPos, Vector3 newPos)
        {
            if (!NetworkServer.active) { /* no-op: handled by OnDeserialize */ }
        }

        private void OnRotationChanged(Quaternion oldRot, Quaternion newRot)
        {
            if (!NetworkServer.active) { /* no-op: handled by OnDeserialize */ }
        }

        private void OnScaleChanged(float oldScale, float newScale)
        {
            if (!NetworkServer.active && tokenSetup != null)
            {
                tokenSetup.SetScale(newScale);
            }
        }

        private void OnStateChanged(int oldState, int newState)
        {
            if (!NetworkServer.active && tokenSetup != null)
            {
                tokenSetup.SetState(newState);
            }
        }

        [Command(requiresAuthority = false)]
        public void CmdUpdatePosition(Vector3 position)
        {
            if (cachedRb != null)
            {
                cachedRb.position = position;
            }
            else
            {
                transform.position = position;
            }
            syncPosition = position;
        }

        [Command(requiresAuthority = false)]
        public void CmdUpdateRotation(Quaternion rotation)
        {
            if (cachedRb != null)
            {
                cachedRb.rotation = rotation;
            }
            else
            {
                transform.rotation = rotation;
            }
            syncRotation = rotation;
        }

        [Command(requiresAuthority = false)]
        public void CmdUpdateScale(float scale)
        {
            if (tokenSetup != null)
            {
                tokenSetup.SetScale(scale);
                syncScale = scale;
            }
        }

        [Command(requiresAuthority = false)]
        public void CmdUpdateState(int state)
        {
            if (tokenSetup != null)
            {
                tokenSetup.SetState(state);
                syncState = state;
            }
        }

        /// <summary>
        /// Spawn a token at a specific position (called from server)
        /// </summary>
        public static void SpawnTokenAtPosition(GameObject prefab, Vector3 position)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("Only server can spawn tokens!");
                return;
            }

            // Spawn the token at the specified position
            GameObject token = Instantiate(prefab, position, Quaternion.identity);
            NetworkServer.Spawn(token);
        }

        /// <summary>
        /// Find local player camera (similar to TokenSetup method)
        /// </summary>
        private static Camera FindLocalPlayerCamera()
        {
            Camera[] cameras = Object.FindObjectsOfType<Camera>();
            
            foreach (Camera cam in cameras)
            {
                if (cam.enabled && cam.gameObject.activeInHierarchy)
                {
                    // Check if this camera belongs to a local player
                    var networkPlayer = cam.GetComponentInParent<Nexus.Networking.NetworkPlayer>();
                    if (networkPlayer != null && networkPlayer.isLocalPlayer)
                    {
                        return cam;
                    }
                    // If no NetworkPlayer found, check if it's tagged as MainCamera
                    else if (cam.CompareTag("MainCamera"))
                    {
                        return cam;
                    }
                }
            }
            
            return Camera.main;
        }

        /// <summary>
        /// Command to move a token to a specific position
        /// </summary>
        [Command(requiresAuthority = false)]
        public void CmdMoveTokenToPosition(Vector3 targetPosition)
        {
            // Apply the position on server
            transform.position = targetPosition;
            syncPosition = targetPosition;
            
            // Force sync to all clients immediately
            RpcUpdatePosition(targetPosition);
        }
        
        [ClientRpc]
        private void RpcUpdatePosition(Vector3 newPosition)
        {
            // Update position on all clients immediately
            transform.position = newPosition;
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

        // Snapshot buffer helpers (client only)
        private void EnqueueSnapshot()
        {
            if (NetworkServer.active) return;
            Snapshot s;
            s.time = Mirror.NetworkTime.time;
            s.pos = syncPosition;
            s.rot = syncRotation;
            snapshots.Add(s);
            // Trim very old snapshots and cap size
            double cutoff = Mirror.NetworkTime.time - 1.0; // keep ~1s of history
            int start = 0;
            for (; start < snapshots.Count; start++)
            {
                if (snapshots[start].time >= cutoff) break;
            }
            if (start > 0) snapshots.RemoveRange(0, start);
            if (snapshots.Count > 64) snapshots.RemoveRange(0, snapshots.Count - 64);
        }

        private void ApplySnapshotInterpolation()
        {
            if (snapshots.Count == 0) return;
            double renderTime = Mirror.NetworkTime.time - interpolationBackTime;
            // If we're too new, just use latest
            int last = snapshots.Count - 1;
            if (renderTime >= snapshots[last].time)
            {
                transform.position = snapshots[last].pos;
                transform.rotation = snapshots[last].rot;
                return;
            }
            // Find the pair surrounding renderTime
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
            // Too old: stick to earliest snapshot
            transform.position = snapshots[0].pos;
            transform.rotation = snapshots[0].rot;
        }

        [Command(requiresAuthority = false)]
        public void CmdSetLocked(bool locked)
        {
            if (!NetworkServer.active) return;
            syncLocked = locked;
        }

        private void OnLockedChanged(bool oldVal, bool newVal)
        {
            // Lock state changed - handled by game logic if needed
        }

        // Allow server to move tokens directly when requested via player Command
        [Server]
        public void ServerMoveTo(Vector3 targetPosition)
        {
            transform.position = targetPosition;
            syncPosition = targetPosition;
            RpcUpdatePosition(targetPosition);
        }

        /// <summary>
        /// Helper method to find a player's camera from their connection
        /// </summary>
        private static Camera GetPlayerCamera(NetworkConnectionToClient conn)
        {
            // Find the player object for this connection
            if (conn.identity != null)
            {
                // Look for camera in the player object
                Camera camera = conn.identity.GetComponentInChildren<Camera>();
                if (camera != null && camera.enabled)
                {
                    return camera;
                }
            }

            // Fallback: find any active camera (for single player or host)
            Camera[] cameras = Object.FindObjectsOfType<Camera>();
            foreach (Camera cam in cameras)
            {
                if (cam.enabled && cam.gameObject.activeInHierarchy)
                {
                    return cam;
                }
            }

            return Camera.main;
        }
    }
}
