using UnityEngine;
using Mirror;

namespace Nexus.Networking
{
    /// <summary>
    /// Helper class to spawn networked objects
    /// Only the server/host can spawn objects
    /// </summary>
    public class NetworkSpawner : MonoBehaviour
    {
        private static NetworkSpawner _instance;
        public static NetworkSpawner Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<NetworkSpawner>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Spawn a networked object at a position
        /// </summary>
        public void SpawnObject(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("Only the host can spawn objects!");
                return;
            }

            SpawnObjectInternal(prefab.name, position, rotation);
        }

        private void SpawnObjectInternal(string prefabName, Vector3 position, Quaternion rotation)
        {
            // Find the prefab in the registered spawnable prefabs
            GameObject prefab = FindPrefabByName(prefabName);
            
            if (prefab == null)
            {
                Debug.LogError($"Prefab {prefabName} not found in spawnable prefabs!");
                return;
            }

            GameObject obj = Instantiate(prefab, position, rotation);
            // Server-side ground placement: align collider bottom to the nearest upward-facing floor below the spawn point.
            Collider[] cols = obj.GetComponentsInChildren<Collider>();
            if (cols != null && cols.Length > 0)
            {
                Bounds b = cols[0].bounds;
                for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
                float halfHeight = Mathf.Max(0.01f, b.extents.y);
                Vector3 origin = obj.transform.position + Vector3.up * (halfHeight + 2f);
                float distance = halfHeight + 50f;

                // Footprint boxcast first to avoid sinking on uneven ground
                Vector3 halfExtents = new Vector3(
                    Mathf.Max(0.001f, b.extents.x - 0.02f),
                    0.01f,
                    Mathf.Max(0.001f, b.extents.z - 0.02f)
                );
                RaycastHit[] hits = new RaycastHit[8];
                int count = Physics.BoxCastNonAlloc(origin, halfExtents, Vector3.down, hits, obj.transform.rotation, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float bestDist = float.PositiveInfinity;
                bool found = false;
                RaycastHit bestHit = new RaycastHit();
                for (int i = 0; i < count; i++)
                {
                    var h = hits[i];
                    if (h.collider == null) continue;
                    if (h.transform == obj.transform || h.transform.IsChildOf(obj.transform)) continue;
                    if (h.normal.y < 0.4f) continue;
                    if (h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        bestHit = h;
                        found = true;
                    }
                }
                // Fallback to a single center ray
                if (!found && Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.normal.y >= 0.4f) { bestHit = hit; found = true; }
                }
                if (found)
                {
                    float currentBottomY = b.min.y;
                    float targetBottomY = bestHit.point.y;
                    float delta = targetBottomY - currentBottomY;
                    if (Mathf.Abs(delta) > 0.0001f)
                    {
                        obj.transform.position += Vector3.up * delta;
                    }
                }
            }

            // Enforce simple non-physics defaults on server (snapping-based)
            var rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.drag = 0f;
                rb.angularDrag = 0.05f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                rb.detectCollisions = true;
                rb.WakeUp();
            }

            // Ensure simple drag component exists for network-spawned tokens (new logic)
            if (obj.GetComponent<TokenSetup>() != null)
            {
                var dragger = obj.GetComponent<TokenDraggable>();
                if (dragger == null)
                {
                    dragger = obj.AddComponent<TokenDraggable>();
                }
            }

            // Ensure colliders are solid (not triggers)
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    cols[i].isTrigger = false;
                }
            }
            // Ensure tokens have NetworkedToken for proper movement/sync
            if (obj.GetComponent<Nexus.Networking.NetworkedToken>() == null && obj.GetComponent<TokenSetup>() != null)
            {
                obj.AddComponent<Nexus.Networking.NetworkedToken>();
            }
            // Ensure non-token Movable objects get NetworkedMovable for sync
            if (obj.GetComponent<TokenSetup>() == null && obj.GetComponent<Nexus.Networking.NetworkedMovable>() == null)
            {
                if (obj.CompareTag("Movable"))
                {
                    obj.AddComponent<Nexus.Networking.NetworkedMovable>();
                }
            }
            NetworkServer.Spawn(obj);
        }

        /// <summary>
        /// Destroy a networked object
        /// </summary>
        public void DestroyObject(GameObject obj)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("Only the host can destroy objects!");
                return;
            }

            NetworkIdentity netId = obj.GetComponent<NetworkIdentity>();
            if (netId != null)
            {
                DestroyObjectInternal(netId.netId);
            }
        }

        private void DestroyObjectInternal(uint netId)
        {
            if (NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity))
            {
                NetworkServer.Destroy(identity.gameObject);
            }
        }

        /// <summary>
        /// Spawn a token at a specific position (prefab name must be registered in spawn prefabs)
        /// </summary>
        public void SpawnTokenAtPosition(string prefabName, Vector3 position)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("Only the host can spawn objects!");
                return;
            }
            SpawnObjectInternal(prefabName, position, Quaternion.identity);
        }

        /// <summary>
        /// Public API for spawning by name, usable by server-side Commands
        /// </summary>
        public void SpawnObjectByName(string prefabName, Vector3 position, Quaternion rotation)
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("Only the host can spawn objects!");
                return;
            }
            SpawnObjectInternal(prefabName, position, rotation);
        }

        private GameObject FindPrefabByName(string prefabName)
        {
            // This is a simple implementation
            // In production, you'd want a more robust prefab registry
            foreach (var prefab in GameNetworkManager.Instance.spawnPrefabs)
            {
                if (prefab.name == prefabName)
                {
                    return prefab;
                }
            }
            return null;
        }

        
    }
}
