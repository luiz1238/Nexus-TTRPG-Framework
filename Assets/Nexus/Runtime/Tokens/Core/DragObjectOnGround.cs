    using UnityEngine;
using Mirror;
using Nexus.Networking;
using System.Collections.Generic;

public class DragObjectOnGround : MonoBehaviour
{
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

    private Vector3 lastProjectedPoint;
    private bool hasProjected = false;
    private Vector3 lastMousePos;

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
        if (tokenSetup != null)
        {
            int m = tokenSetup.GetGroundMask();
            if (m != 0) return m;
        }
        return LayerMask.GetMask("Ground");
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
            lastMousePos = Input.mousePosition;
            lastProjectedPoint = ray.GetPoint(d) + offset;
            hasProjected = true;
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
        Vector3 pCurrent = ray.GetPoint(dClamped) + offset;

        Vector3 newXZPos = pCurrent;
        if (hasProjected)
        {
            Ray rayPrev = playerCamera.ScreenPointToRay(lastMousePos);
            float prevDist;
            if (!(dragPlaneActive && dragPlane.Raycast(rayPrev, out prevDist)))
            {
                Plane planePrev = new Plane(Vector3.up, transform.position);
                planePrev.Raycast(rayPrev, out prevDist);
            }
            float prevClamped = Mathf.Min(prevDist, maxD);
            Vector3 pPrevSameCam = rayPrev.GetPoint(prevClamped) + offset;
            Vector3 mouseOnlyDelta = pCurrent - pPrevSameCam;
            newXZPos = lastProjectedPoint + mouseOnlyDelta;
        }

        Vector3 targetPos = newXZPos;
        lastProjectedPoint = newXZPos;
        lastMousePos = Input.mousePosition;
        hasProjected = true;
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
        transform.position = savedPosXZ;

        int mask = EffectiveGroundMask();
        float originY = cbNow.max.y + Mathf.Max(0.05f, rayStartHeight);
        float rayDownDist = Mathf.Max(0.25f, rayStartHeight + searchDown + cbNow.extents.y + 0.5f);
        Vector3 origin = new Vector3(targetPos.x, originY, targetPos.z);
        float allowedUp = Mathf.Max(0.01f, maxStepUpNoBelow);
        float allowedMaxY = currentBottomY + allowedUp;
        float newY = savedPosXZ.y; // default keep height

        // Center ray: choose nearest valid ground under center
        bool centerOK = false;
        RaycastHit centerHit = new RaycastHit();
        var centerHits = Physics.RaycastAll(origin, Vector3.down, rayDownDist, mask, QueryTriggerInteraction.Ignore);
        if (centerHits != null && centerHits.Length > 0)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < centerHits.Length; i++)
            {
                var h = centerHits[i];
                var ht = h.collider != null ? h.collider.transform : null;
                bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                if (selfHit) continue;
                if (h.normal.y < minGroundNormalY) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    centerHit = h;
                    centerOK = true;
                }
            }
        }

        // Footprint support: boxcast down to find any support under token extents
        bool supportOK = false;
        RaycastHit supportHit = new RaycastHit();
        var supportHits = Physics.BoxCastAll(origin, halfExtNow, Vector3.down, transform.rotation, rayDownDist, mask, QueryTriggerInteraction.Ignore);
        if (supportHits != null && supportHits.Length > 0)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < supportHits.Length; i++)
            {
                var h = supportHits[i];
                var ht = h.collider != null ? h.collider.transform : null;
                bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                if (selfHit) continue;
                if (h.normal.y < minGroundNormalY) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    supportHit = h;
                    supportOK = true;
                }
            }
        }

        bool gotGround = false;
        float chosenY = currentBottomY;
        if (centerOK)
        {
            float cy = centerHit.point.y;
            if (cy < currentBottomY)
            {
                // Step down immediately when center falls below current bottom (edge/cliff)
                chosenY = cy;
            }
            else
            {
                float sy = supportOK ? supportHit.point.y : cy;
                cy = Mathf.Min(cy, allowedMaxY);
                sy = Mathf.Min(sy, allowedMaxY);
                chosenY = Mathf.Max(cy, sy);
            }
            gotGround = true;
        }
        else if (supportOK)
        {
            // No center hit; prefer highest support under footprint, but allow large step-down via corners
            float px = Mathf.Max(0f, cbNow.extents.x - 0.02f);
            float pz = Mathf.Max(0f, cbNow.extents.z - 0.02f);
            Vector3[] corners = new Vector3[4]
            {
                new Vector3(origin.x - px, origin.y, origin.z - pz),
                new Vector3(origin.x - px, origin.y, origin.z + pz),
                new Vector3(origin.x + px, origin.y, origin.z - pz),
                new Vector3(origin.x + px, origin.y, origin.z + pz),
            };
            bool anyCorner = false;
            float minCornerY = float.PositiveInfinity;
            for (int i = 0; i < 4; i++)
            {
                if (Physics.Raycast(corners[i], Vector3.down, out RaycastHit ch, rayDownDist, mask, QueryTriggerInteraction.Ignore))
                {
                    var ht = ch.collider != null ? ch.collider.transform : null;
                    bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                    if (selfHit) continue;
                    if (ch.normal.y < minGroundNormalY) continue;
                    anyCorner = true;
                    if (ch.point.y < minCornerY) minCornerY = ch.point.y;
                }
            }
            if (anyCorner && (currentBottomY - minCornerY) >= 0.35f)
            {
                chosenY = minCornerY;
            }
            else
            {
                float sy = supportHit.point.y;
                float farY = sy;
                var farHits = Physics.RaycastAll(origin, Vector3.down, maxRayDistance, mask, QueryTriggerInteraction.Ignore);
                if (farHits != null && farHits.Length > 0)
                {
                    float best = float.PositiveInfinity;
                    for (int i = 0; i < farHits.Length; i++)
                    {
                        var h = farHits[i];
                        var ht = h.collider != null ? h.collider.transform : null;
                        bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                        if (selfHit) continue;
                        if (h.normal.y < minGroundNormalY) continue;
                        if (h.distance < best)
                        {
                            best = h.distance;
                            farY = h.point.y;
                        }
                    }
                }
                if (farY < sy - 0.05f)
                {
                    chosenY = farY;
                }
                else
                {
                    sy = Mathf.Min(sy, allowedMaxY);
                    chosenY = sy;
                }
            }
            gotGround = true;
        }
        else
        {
            // Nothing within short window; try an extended downward search to catch very high cliffs
            float farDist = maxRayDistance;
            var farHits = Physics.RaycastAll(origin, Vector3.down, farDist, mask, QueryTriggerInteraction.Ignore);
            if (farHits != null && farHits.Length > 0)
            {
                float best = float.PositiveInfinity;
                for (int i = 0; i < farHits.Length; i++)
                {
                    var h = farHits[i];
                    var ht = h.collider != null ? h.collider.transform : null;
                    bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                    if (selfHit) continue;
                    if (h.normal.y < minGroundNormalY) continue;
                    if (h.distance < best)
                    {
                        best = h.distance;
                        centerHit = h;
                        centerOK = true;
                    }
                }
                if (centerOK)
                {
                    chosenY = centerHit.point.y;
                    gotGround = true;
                }
            }
        }

        if (gotGround)
        {
            float groundY = chosenY + feetLift;
            newY = savedPosXZ.y + (groundY - currentBottomY);
            lastGroundY = chosenY;
            hasGroundY = true;
            framesSinceSeenLastGround = 0;
            if (centerOK) lastGroundCollider = centerHit.collider; else if (supportOK) lastGroundCollider = supportHit.collider;
        }
        else
        {
            // Keep height; note we temporarily lost ground
            framesSinceSeenLastGround++;
            hasGroundY = false;
        }
        targetPos.y = newY;

        if (cachedCols != null && cachedCols.Length > 0)
        {
            for (int iter = 0; iter < 2; iter++)
            {
                Vector3 tmpPos = transform.position;
                Quaternion tmpRot = transform.rotation;
                transform.position = new Vector3(targetPos.x, targetPos.y, targetPos.z);
                Bounds cb = cachedCols[0].bounds;
                for (int i = 1; i < cachedCols.Length; i++) cb.Encapsulate(cachedCols[i].bounds);
                Vector3 half = cb.extents + new Vector3(0.002f, 0.002f, 0.002f);
                Collider[] others = Physics.OverlapBox(cb.center, half, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                Vector3 totalHoriz = Vector3.zero;
                for (int i = 0; i < others.Length; i++)
                {
                    var ec = others[i];
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
                                Vector3 sep = dir.normalized * dist;
                                Vector3 horiz = Vector3.ProjectOnPlane(sep, Vector3.up);
                                if (horiz.sqrMagnitude > 0.000001f) totalHoriz += horiz;
                            }
                        }
                    }
                }
                transform.position = tmpPos;
                transform.rotation = tmpRot;
                if (totalHoriz.sqrMagnitude > 0.000001f) targetPos += totalHoriz;
                else break;
            }
        }

        if (cachedCols != null && cachedCols.Length > 0)
        {
            float yDelta = targetPos.y - savedPosXZ.y;
            float originY2 = cbNow.max.y + yDelta + Mathf.Max(0.05f, rayStartHeight);
            Vector3 origin2 = new Vector3(targetPos.x, originY2, targetPos.z);
            float currentBottomY2 = currentBottomY + yDelta;
            float allowedMaxY2 = currentBottomY2 + allowedUp;

            bool centerOK2 = false;
            RaycastHit centerHit2 = new RaycastHit();
            var centerHits2 = Physics.RaycastAll(origin2, Vector3.down, maxRayDistance, mask, QueryTriggerInteraction.Ignore);
            if (centerHits2 != null && centerHits2.Length > 0)
            {
                float best2 = float.PositiveInfinity;
                for (int i = 0; i < centerHits2.Length; i++)
                {
                    var h = centerHits2[i];
                    var ht = h.collider != null ? h.collider.transform : null;
                    bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                    if (selfHit) continue;
                    if (h.normal.y < minGroundNormalY) continue;
                    if (h.distance < best2)
                    {
                        best2 = h.distance;
                        centerHit2 = h;
                        centerOK2 = true;
                    }
                }
            }

            bool supportOK2 = false;
            RaycastHit supportHit2 = new RaycastHit();
            var supportHits2 = Physics.BoxCastAll(origin2, halfExtNow, Vector3.down, transform.rotation, maxRayDistance, mask, QueryTriggerInteraction.Ignore);
            if (supportHits2 != null && supportHits2.Length > 0)
            {
                float best2 = float.PositiveInfinity;
                for (int i = 0; i < supportHits2.Length; i++)
                {
                    var h = supportHits2[i];
                    var ht = h.collider != null ? h.collider.transform : null;
                    bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                    if (selfHit) continue;
                    if (h.normal.y < minGroundNormalY) continue;
                    if (h.distance < best2)
                    {
                        best2 = h.distance;
                        supportHit2 = h;
                        supportOK2 = true;
                    }
                }
            }

            bool gotGround2 = false;
            float chosenY2 = currentBottomY2;
            if (centerOK2)
            {
                float cy2 = centerHit2.point.y;
                if (cy2 < currentBottomY2)
                {
                    chosenY2 = cy2;
                }
                else
                {
                    float sy2 = supportOK2 ? supportHit2.point.y : cy2;
                    cy2 = Mathf.Min(cy2, allowedMaxY2);
                    sy2 = Mathf.Min(sy2, allowedMaxY2);
                    chosenY2 = Mathf.Max(cy2, sy2);
                }
                gotGround2 = true;
            }
            else if (supportOK2)
            {
                float px2 = Mathf.Max(0f, cbNow.extents.x - 0.02f);
                float pz2 = Mathf.Max(0f, cbNow.extents.z - 0.02f);
                Vector3[] corners2 = new Vector3[4]
                {
                    new Vector3(origin2.x - px2, origin2.y, origin2.z - pz2),
                    new Vector3(origin2.x - px2, origin2.y, origin2.z + pz2),
                    new Vector3(origin2.x + px2, origin2.y, origin2.z - pz2),
                    new Vector3(origin2.x + px2, origin2.y, origin2.z + pz2),
                };
                bool anyCorner2 = false;
                float minCornerY2 = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    if (Physics.Raycast(corners2[i], Vector3.down, out RaycastHit ch2, maxRayDistance, mask, QueryTriggerInteraction.Ignore))
                    {
                        var ht = ch2.collider != null ? ch2.collider.transform : null;
                        bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                        if (selfHit) continue;
                        if (ch2.normal.y < minGroundNormalY) continue;
                        anyCorner2 = true;
                        if (ch2.point.y < minCornerY2) minCornerY2 = ch2.point.y;
                    }
                }
                if (anyCorner2 && (currentBottomY2 - minCornerY2) >= 0.35f)
                {
                    chosenY2 = minCornerY2;
                }
                else
                {
                    float sy2 = supportHit2.point.y;
                    sy2 = Mathf.Min(sy2, allowedMaxY2);
                    chosenY2 = sy2;
                }
                gotGround2 = true;
            }
            else
            {
                var farHits2 = Physics.RaycastAll(origin2, Vector3.down, maxRayDistance, mask, QueryTriggerInteraction.Ignore);
                if (farHits2 != null && farHits2.Length > 0)
                {
                    float best2 = float.PositiveInfinity;
                    for (int i = 0; i < farHits2.Length; i++)
                    {
                        var h = farHits2[i];
                        var ht = h.collider != null ? h.collider.transform : null;
                        bool selfHit = ht == transform || (ht != null && ht.IsChildOf(transform));
                        if (selfHit) continue;
                        if (h.normal.y < minGroundNormalY) continue;
                        if (h.distance < best2)
                        {
                            best2 = h.distance;
                            centerHit2 = h;
                            centerOK2 = true;
                        }
                    }
                    if (centerOK2)
                    {
                        chosenY2 = centerHit2.point.y;
                        gotGround2 = true;
                    }
                }
            }

            if (gotGround2)
            {
                float groundY2 = chosenY2 + feetLift;
                float newY2 = savedPosXZ.y + yDelta + (groundY2 - currentBottomY2);
                targetPos.y = newY2;
                lastGroundY = chosenY2;
                hasGroundY = true;
                framesSinceSeenLastGround = 0;
                if (centerOK2) lastGroundCollider = centerHit2.collider; else if (supportOK2) lastGroundCollider = supportHit2.collider;
            }
        }

        // Final safety: iteratively lift out of any residual intersections with ground colliders (vertical-only)
        if (cachedCols != null && cachedCols.Length > 0)
        {
            for (int iter = 0; iter < 5; iter++)
            {
                Vector3 savedPos = transform.position;
                Quaternion savedRot = transform.rotation;
                transform.position = new Vector3(targetPos.x, targetPos.y, targetPos.z);
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
                transform.position = savedPos;
                transform.rotation = savedRot;
                if (maxLift > 0.0005f)
                {
                    targetPos.y += maxLift;
                }
                else
                {
                    break;
                }
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
        hasProjected = false;
        if (tokenSetup != null) tokenSetup.ForceSnapImmediate();
    }
}
