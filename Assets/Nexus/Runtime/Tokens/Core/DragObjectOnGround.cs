    using UnityEngine;
using Mirror;
using Nexus.Networking;
using System.Collections.Generic;

public class DragObjectOnGround : MonoBehaviour
{
    [Header("Camadas")]
    public LayerMask groundLayer = 0; // se não definido, usa DefaultRaycastLayers

    [Header("Altura e suavização")]
    public float pivotOffsetToFeet = 1f;
    public float rayStartHeight = 2f;
    public float smoothSpeed = 10f;
    public float minGroundNormalY = 0.4f;
    public float maxStepUp = 0.75f;
    public float maxStepDown = 2.5f;
    public float maxRayDistance = 1000f;

    [Header("Rotação com scroll")]
    public float rotationSpeed = 200f;

    [Header("Debug")]
    public bool enableDebug = false;

    [Header("Horizontal movement limits")]
    [Tooltip("Max horizontal speed (m/s) while dragging to avoid big jumps when cursor is near camera")]
    public float maxXZSpeed = 8f;

    [Header("Ground settings")]
    [Tooltip("Small visual lift above ground to avoid z-fighting and tiny intersections")]
    public float feetLift = 0.01f;

    private Vector3 offset;
    private Camera mainCamera;
    private Camera playerCamera;
    private float lastGroundY;
    private bool hasGroundY = false;
    private TokenSetup tokenSetup;
    private NetworkedToken netToken;
    private Plane dragPlane;
    private bool dragPlaneActive = false;
    private float dragHeightOffset = 0f;
    private float lastNetworkSendTime = 0f;
    public float netSendInterval = 0.03f;
    [Header("Vertical search window")]
    [Tooltip("Max ascent allowed while dragging without explicit ramps (meters)")]
    public float searchUp = 0.75f;
    [Tooltip("Max descent allowed while dragging (meters)")]
    public float searchDown = 2.5f;
    [Tooltip("Max upward change allowed when no floor below is detected (prevents snapping to ceilings)")]
    public float maxStepUpNoBelow = 0.2f;
    // Undo tracking
    private Vector3 undoStartPos;
    private Quaternion undoStartRot;
    private bool undoActive;
    private Collider[] cachedCols;
    private Vector3 boxHalfExtents;
    private float pivotToBottomLocal;
    private float tokenHalfHeightLocal;
    private BoxCollider primaryBox; // prefer a single BoxCollider for precise bottom
    private SpriteRenderer spriteRendererRef; // for visual bottom alignment
    // Floor stickiness
    private Collider lastGroundCollider;
    private int framesSinceSeenLastGround = 0;
    [Tooltip("Frames to wait before switching to a new floor when last floor isn't detected")]
    public int floorSwitchGraceFrames = 6;

    private int EffectiveGroundMask()
    {
        if (groundLayer.value != 0) return groundLayer.value;
        int m = LayerMask.GetMask("Ground");
        if (m == 0) return Physics.DefaultRaycastLayers;
        return m;
    }

    void Start()
    {
        tokenSetup = GetComponent<TokenSetup>();
        primaryBox = GetComponent<BoxCollider>();
        spriteRendererRef = GetComponentInChildren<SpriteRenderer>();
        if (Nexus.CameraManager.Instance != null)
            playerCamera = Nexus.CameraManager.Instance.MainCamera;
        if (playerCamera == null) playerCamera = Camera.main; // Fallback if CameraManager not available or camera not set
        netToken = GetComponent<NetworkedToken>();
    }

    private void Update()
    {
        if (Nexus.CameraManager.Instance != null)
            playerCamera = Nexus.CameraManager.Instance.MainCamera;

        if (playerCamera == null) playerCamera = Camera.main; // Fallback if CameraManager not available or camera not set
    }

    void OnMouseDown()
    {
        if (playerCamera == null) playerCamera = (Nexus.CameraManager.Instance != null ? Nexus.CameraManager.Instance.MainCamera : Camera.main);
        if (playerCamera == null) return;
        if (tokenSetup != null) tokenSetup.externalDragActive = true;
        var allCols = GetComponentsInChildren<Collider>();
        if (allCols != null && allCols.Length > 0)
        {
            // filter out triggers; only solid colliders define the footprint
            List<Collider> solids = new List<Collider>(allCols.Length);
            for (int i = 0; i < allCols.Length; i++)
            {
                var c = allCols[i];
                if (c != null && !c.isTrigger) solids.Add(c);
            }
            cachedCols = solids.Count > 0 ? solids.ToArray() : allCols; // fallback if all are triggers
            if (primaryBox != null)
            {
                // compute from box collider for accuracy
                Vector3 worldSize = Vector3.Scale(primaryBox.size, primaryBox.transform.lossyScale);
                tokenHalfHeightLocal = Mathf.Max(0.001f, worldSize.y * 0.5f);
                Vector3 worldBottom = primaryBox.transform.TransformPoint(primaryBox.center - new Vector3(0f, primaryBox.size.y * 0.5f, 0f));
                pivotToBottomLocal = worldBottom.y - transform.position.y;
                boxHalfExtents = new Vector3(
                    Mathf.Max(0.001f, worldSize.x * 0.5f - 0.02f),
                    0.01f,
                    Mathf.Max(0.001f, worldSize.z * 0.5f - 0.02f)
                );
            }
            else
            {
                Bounds b = cachedCols[0].bounds;
                for (int i = 1; i < cachedCols.Length; i++) b.Encapsulate(cachedCols[i].bounds);
                tokenHalfHeightLocal = b.extents.y;
                pivotToBottomLocal = b.min.y - transform.position.y;
                boxHalfExtents = new Vector3(Mathf.Max(0.001f, b.extents.x - 0.02f), 0.01f, Mathf.Max(0.001f, b.extents.z - 0.02f));
            }
        }
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        int mask = EffectiveGroundMask();
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        bool found = false;
        RaycastHit best = new RaycastHit();
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (h.collider == null) continue;
            var ht = h.collider.transform;
            if (ht == transform || ht.IsChildOf(transform)) continue; // ignora próprio collider
            if (h.normal.y < minGroundNormalY) continue; // ignora superfícies muito íngremes
            best = h; found = true; break;
        }
        Vector3 planePoint = found ? best.point : transform.position;
        Vector3 planeNormal = found && best.normal.y >= minGroundNormalY ? best.normal : Vector3.up;
        dragPlane = new Plane(planeNormal, planePoint);
        dragPlaneActive = true;
        // Initialize last ground height so step filters are respected on first drag frame
        if (found)
        {
            lastGroundY = best.point.y;
            hasGroundY = true;
            lastGroundCollider = best.collider;
            framesSinceSeenLastGround = 0;
        }
        else
        {
            hasGroundY = false;
            lastGroundCollider = null;
            framesSinceSeenLastGround = 0;
        }
        // offset estável relativo ao ponto do plano
        if (dragPlane.Raycast(ray, out float d))
        {
            offset = transform.position - ray.GetPoint(d);
        }
        // memoriza diferença de altura inicial
        dragHeightOffset = 0f;

        // Rede: inicia drag
        bool networkActive = NetworkServer.active || NetworkClient.isConnected;
        if (networkActive && netToken != null && netToken.netIdentity != null && netToken.netIdentity.netId != 0)
        {
            netToken.SetLocalDragOwner(true);
            netToken.CmdBeginDrag();
            lastNetworkSendTime = 0f;
        }
        // Begin undo
        undoStartPos = transform.position;
        undoStartRot = transform.rotation;
        undoActive = true;

        // Ensure TabletopManager selection targets this token for shortcuts (copy/paste/delete)
        var tabletop = Nexus.TabletopManager.GetActive();
        if (tabletop != null)
        {
            tabletop.Select(transform);
        }
    }

    void OnMouseDrag()
    {
        if (playerCamera == null) playerCamera = (Nexus.CameraManager.Instance != null ? Nexus.CameraManager.Instance.MainCamera : Camera.main);
        if (playerCamera == null) return;
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (!(dragPlaneActive && dragPlane.Raycast(ray, out float distance)))
        {
            // fallback simples
            Plane plane = new Plane(Vector3.up, transform.position);
            if (!plane.Raycast(ray, out distance)) return;
            dragPlane = plane;
            dragPlaneActive = true;
        }

        float maxD = Mathf.Min(maxRayDistance, 80f);
        if (distance < 0f) distance = 0f;
        float dClamped = Mathf.Min(distance, maxD);
        Vector3 targetPos = ray.GetPoint(dClamped) + offset;
        // Limit horizontal speed to avoid overshooting into roofs/ceilings when cursor moves fast or is near camera
        Vector2 curXZ = new Vector2(transform.position.x, transform.position.z);
        Vector2 tgtXZ = new Vector2(targetPos.x, targetPos.z);
        Vector2 newXZ = Vector2.MoveTowards(curXZ, tgtXZ, Mathf.Max(0.01f, maxXZSpeed) * Time.deltaTime);
        targetPos.x = newXZ.x;
        targetPos.z = newXZ.y;
        // Compute current bottom at predicted XZ, then cast straight down to find a support UNDER the token
        Vector3 savedPosXZ = transform.position;
        transform.position = new Vector3(targetPos.x, savedPosXZ.y, targetPos.z);
        // Ensure colliders are cached
        if (cachedCols == null || cachedCols.Length == 0)
        {
            var allColsNow = GetComponentsInChildren<Collider>();
            if (allColsNow != null && allColsNow.Length > 0)
            {
                List<Collider> solidsNow = new List<Collider>(allColsNow.Length);
                for (int i = 0; i < allColsNow.Length; i++)
                {
                    var c = allColsNow[i];
                    if (c != null && !c.isTrigger) solidsNow.Add(c);
                }
                cachedCols = solidsNow.Count > 0 ? solidsNow.ToArray() : allColsNow;
            }
        }
        Bounds cbNow;
        Vector3 halfExtNow;
        float currentBottomY;
        if (primaryBox != null)
        {
            cbNow = primaryBox.bounds;
            currentBottomY = cbNow.min.y;
            halfExtNow = new Vector3(
                Mathf.Max(0.001f, cbNow.extents.x - 0.02f),
                0.01f,
                Mathf.Max(0.001f, cbNow.extents.z - 0.02f)
            );
        }
        else if (cachedCols != null && cachedCols.Length > 0)
        {
            cbNow = cachedCols[0].bounds;
            for (int i = 1; i < cachedCols.Length; i++) cbNow.Encapsulate(cachedCols[i].bounds);
            currentBottomY = cbNow.min.y;
            halfExtNow = new Vector3(
                Mathf.Max(0.001f, cbNow.extents.x - 0.02f),
                0.01f,
                Mathf.Max(0.001f, cbNow.extents.z - 0.02f)
            );
        }
        else
        {
            cbNow = new Bounds(savedPosXZ, new Vector3(0.02f, 0.02f, 0.02f));
            currentBottomY = savedPosXZ.y - pivotOffsetToFeet;
            halfExtNow = new Vector3(0.01f, 0.01f, 0.01f);
        }
        if (spriteRendererRef != null)
        {
            float spriteBottom = spriteRendererRef.bounds.min.y;
            if (spriteBottom < currentBottomY) currentBottomY = spriteBottom;
        }
        transform.position = savedPosXZ;

        int mask = EffectiveGroundMask();
        float originY = cbNow.max.y + Mathf.Max(0.05f, rayStartHeight);
        float rayDownDist = Mathf.Max(0.25f, rayStartHeight + searchDown + cbNow.extents.y + 0.5f);
        Vector3 origin = new Vector3(targetPos.x, originY, targetPos.z);
        float allowedUp = Mathf.Max(0.01f, maxStepUpNoBelow);
        float newY = savedPosXZ.y; // default keep height
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayDownDist, mask, QueryTriggerInteraction.Ignore))
        {
            var ht = hit.collider != null ? hit.collider.transform : null;
            bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
            if (!selfHit && hit.normal.y >= minGroundNormalY)
            {
                float hpY = hit.point.y;
                if (hpY <= currentBottomY + allowedUp)
                {
                    float groundY = hpY + feetLift;
                    newY = savedPosXZ.y + (groundY - currentBottomY);
                }
            }
        }
        targetPos.y = newY;

        // Final safety: lift out of any residual intersections with ground colliders (vertical-only)
        if (cachedCols != null && cachedCols.Length > 0)
        {
            Vector3 savedPos = transform.position;
            Quaternion savedRot = transform.rotation;
            // Predict at target position to compute penetration
            transform.position = new Vector3(targetPos.x, targetPos.y, targetPos.z);
            // Compute combined bounds at predicted position
            Bounds cb = cachedCols[0].bounds;
            for (int i = 1; i < cachedCols.Length; i++) cb.Encapsulate(cachedCols[i].bounds);
            Vector3 half = cb.extents + new Vector3(0.002f, 0.002f, 0.002f);
            Collider[] envCols = Physics.OverlapBox(cb.center, half, transform.rotation, EffectiveGroundMask(), QueryTriggerInteraction.Ignore);
            float maxLift = 0f;
            for (int i = 0; i < envCols.Length; i++)
            {
                var ec = envCols[i];
                if (ec == null) continue;
                if (ec.transform == transform || ec.transform.IsChildOf(transform)) continue;
                if (ec.isTrigger) continue;
                for (int c = 0; c < cachedCols.Length; c++)
                {
                    var tc = cachedCols[c];
                    if (tc == null) continue;
                    if (Physics.ComputePenetration(tc, tc.transform.position, tc.transform.rotation,
                                                   ec, ec.transform.position, ec.transform.rotation,
                                                   out Vector3 dir, out float dist))
                    {
                        if (dist > 0f)
                        {
                            float lift = Vector3.Dot(dir.normalized * dist, Vector3.up);
                            if (lift > maxLift) maxLift = lift;
                        }
                    }
                }
            }
            // restore
            transform.position = savedPos;
            transform.rotation = savedRot;
            if (maxLift > 0f)
            {
                targetPos.y += maxLift;
            }
        }

        transform.position = targetPos;

        float scroll = Input.mouseScrollDelta.y;
        if (Input.GetKey(KeyCode.R) && Mathf.Abs(scroll) > 0.01f)
        {
            transform.Rotate(Vector3.up, scroll * rotationSpeed * Time.deltaTime, Space.World);
        }

        // Rede: envia atualização parcial durante o drag (throttle)
        bool networkActive = NetworkServer.active || NetworkClient.isConnected;
        if (networkActive && netToken != null && netToken.netIdentity != null && netToken.netIdentity.netId != 0)
        {
            if (Time.time - lastNetworkSendTime >= netSendInterval)
            {
                netToken.CmdUpdatePosition(transform.position);
                netToken.CmdUpdateRotation(transform.rotation);
                lastNetworkSendTime = Time.time;
            }
        }
    }

    void OnMouseUp()
    {
        if (tokenSetup != null) tokenSetup.externalDragActive = false;
        // Rede: finaliza drag e aplica pose
        bool networkActive = NetworkServer.active || NetworkClient.isConnected;
        if (networkActive && netToken != null && netToken.netIdentity != null && netToken.netIdentity.netId != 0)
        {
            netToken.CmdEndDragFinal(transform.position, transform.rotation);
            netToken.SetLocalDragOwner(false);
        }
        dragPlaneActive = false;
        
        // Push undo entry
        if (undoActive)
        {
            var tabletop = Nexus.TabletopManager.GetActive();
            if (tabletop != null)
            {
                tabletop.PushMoveUndo(gameObject, undoStartPos, undoStartRot, transform.position, transform.rotation);
            }
            undoActive = false;
        }

        // reset height offset
        dragHeightOffset = 0f;
        if (tokenSetup != null) tokenSetup.ForceSnapImmediate();
    }
}
