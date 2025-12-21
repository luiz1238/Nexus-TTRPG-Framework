using UnityEngine;
using Nexus;

[RequireComponent(typeof(BoxCollider))]
public class TokenSetup : MonoBehaviour
{
    [Header("Simple Physics Mode")]
    [SerializeField] private bool simplePhysicsMode = false;
    
    
    [Header("Scale Settings")]
    [SerializeField] private float minScale = 0.10f;
    [SerializeField] private float maxScale = 4f;
    [SerializeField] private float scaleStep = 0.05f;
    [SerializeField] private float currentScale = 0.80f;
    
    [Header("Sprite Settings")]
    [SerializeField] private bool billboardToCamera = true;
    
    [Header("Directional 4-Way Billboard")]
    [SerializeField] private bool directional4WayBillboard = false;
    [SerializeField] private Sprite directionalFrontSprite;
    [SerializeField] private Sprite directionalBackSprite;
    
    // Proximity Squash (hardcoded)
    private const bool SQUASH_NEAR_CAMERA = true;
    private const float SQUASH_RANGE = 0.4f;
    private const float SQUASH_NEAR_OFFSET = 0.05f;
    private const float MIN_Y_SCALE_AT_MAX_SQUASH = 0.2f;
    private const float MAX_X_SCALE_AT_MAX_SQUASH = 1.4f;
    private const float SQUASH_LERP_SPEED = 10f;
    
    
    [Header("Token States")]
    [SerializeField] private Sprite defaultSprite;
    [SerializeField] private Sprite alternativeSprite1;
    [SerializeField] private Sprite alternativeSprite2;
    [SerializeField] private Sprite alternativeSprite3;
    [SerializeField] private Sprite alternativeSprite4;
    
    private Rigidbody rb;
    private BoxCollider boxCollider;
    private Camera mainCamera;

    private Transform spriteTransform;
    private SpriteRenderer spriteRenderer;
    private Material spriteMaterial;
    private Vector3 spriteBaseScale = Vector3.one;
    private bool simpleGrounded = false;
    private float simpleClampEndTime = -1f;
    
    [SerializeField] private bool lockYawTo4Angles = false;
    private Vector3 lastSpriteEvalPos;
    private bool lastFlipX = false;
    private bool lastWasBack = false;
    private Vector3 fourWayForward = Vector3.forward;
    private Vector3 fourWayRight = Vector3.right;
    private bool fourWayBasisInit = false;
    private int lastMoveAxis = 0; // 0 none, 1 forward/back, 2 right/left
    private const float moveAxisHysteresis = 0.15f;
    
    // State tracking
    private int currentState = 1;
    private bool isCenterScreenOver = false;
    private Camera playerCamera;
    private float squashOffset;
    private Vector3 targetScale;
    
    private void Awake()
    {
        SetupComponents();
        
        // Default: dynamic with gravity
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (simplePhysicsMode)
        {
            EnforceSimplePhysics();
            simpleGrounded = false;
            simpleClampEndTime = Time.time + 1.0f;
        }
    }

    private void FixedUpdate() { }
    
    private void Start()
    {
        if (Nexus.CameraManager.Instance != null)
        {
            mainCamera = Nexus.CameraManager.Instance.MainCamera;
        }
        else
        {
            if (Camera.main != null) mainCamera = Camera.main;
        }
        
        // Store default sprite if not assigned
        if (defaultSprite == null && spriteRenderer != null)
        {
            defaultSprite = spriteRenderer.sprite;
        }
        
        // Setup custom lit material for sprite
        SetupSpriteMaterial();
        
        // Apply initial state and scale
        ApplyState(currentState);
        ApplyScale(currentScale);
        if (spriteTransform != null) { spriteBaseScale = spriteTransform.localScale; }
        lastSpriteEvalPos = transform.position;
        if (spriteRenderer != null)
        {
            lastFlipX = spriteRenderer.flipX;
        }
        // Initialize 4-way basis using camera direction at start for continuity
        if (mainCamera != null)
        {
            Vector3 toCam = mainCamera.transform.position - transform.position; toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.000001f)
            {
                fourWayForward = toCam.normalized;
                fourWayRight = Vector3.Cross(Vector3.up, fourWayForward).normalized;
                fourWayBasisInit = true;
            }
        }
    }
    
    private void Update()
    {
        // Find local player camera if not found yet (for multiplayer)
        if (mainCamera == null)
        {
            if (Nexus.CameraManager.Instance != null)
                mainCamera = Nexus.CameraManager.Instance.MainCamera;
            else if (Camera.main != null)
                mainCamera = Camera.main;
        }
        
        // Only raycast to check focus when relevant keys are pressed
        bool stateKey =
            InputManager.Instance.GetDown(InputAction.TokenState1) ||
            InputManager.Instance.GetDown(InputAction.TokenState2) ||
            InputManager.Instance.GetDown(InputAction.TokenState3) ||
            InputManager.Instance.GetDown(InputAction.TokenState4) ||
            InputManager.Instance.GetDown(InputAction.TokenState5) ||
            InputManager.Instance.GetDown(InputAction.TokenState6);
        bool scaleKey =
            InputManager.Instance.GetDown(InputAction.TokenScaleUp) ||
            InputManager.Instance.GetDown(InputAction.TokenScaleDown);
        
        if (stateKey || scaleKey)
        {
            CheckCenterScreenOver();
        }
        else
        {
            isCenterScreenOver = false;
        }
        
        // Handle state changes only when center screen is over token
        if (isCenterScreenOver)
        {
            // State changes
            if (InputManager.Instance.GetDown(InputAction.TokenState1))
            {
                SetState(1);
            }
            else if (InputManager.Instance.GetDown(InputAction.TokenState2))
            {
                SetState(2);
            }
            else if (InputManager.Instance.GetDown(InputAction.TokenState3))
            {
                SetState(3);
            }
            else if (InputManager.Instance.GetDown(InputAction.TokenState4))
            {
                SetState(4);
            }
            else if (InputManager.Instance.GetDown(InputAction.TokenState5))
            {
                SetState(5);
            }
            else if (InputManager.Instance.GetDown(InputAction.TokenState6))
            {
                SetState(6);
            }
            
            // Scale changes
            if (InputManager.Instance.GetDown(InputAction.TokenScaleUp))
            {
                IncreaseScale();
            }
            else if (InputManager.Instance.GetDown(InputAction.TokenScaleDown))
            {
                DecreaseScale();
            }
        }
    }
    
    private void LateUpdate()
    {
        // Billboard sprite to face camera
        if (billboardToCamera && spriteTransform != null && mainCamera != null)
        {
            Vector3 directionToCamera = mainCamera.transform.position - spriteTransform.position;
            directionToCamera.y = 0;
            
            if (directionToCamera.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToCamera);
                spriteTransform.rotation = targetRotation;
            }
        }
        if (directional4WayBillboard && spriteRenderer != null && mainCamera != null)
        {
            Vector3 move = transform.position - lastSpriteEvalPos;
            move.y = 0f;
            bool moved = move.sqrMagnitude > 0.000001f;

            Sprite target = spriteRenderer.sprite;
            bool flipX = false;

            if (moved)
            {
                // Movement-based: compare move to camera axes
                Vector3 camFwd = mainCamera.transform.forward; camFwd.y = 0f; camFwd.Normalize();
                Vector3 camRight = mainCamera.transform.right;  camRight.y = 0f;  camRight.Normalize();
                Vector3 moveDir = move.normalized;
                float dpFwd = Vector3.Dot(moveDir, camFwd);
                float dpRight = Vector3.Dot(moveDir, camRight);
                float af = Mathf.Abs(dpFwd);
                float ar = Mathf.Abs(dpRight);
                float diff = af - ar;
                bool useFwdBack;
                float forwardDeadzone = 0.25f;
                if (Mathf.Abs(dpFwd) < forwardDeadzone)
                {
                    useFwdBack = false;
                }
                else
                {
                    if (lastMoveAxis == 0)
                        useFwdBack = af >= ar;
                    else if (lastMoveAxis == 1)
                        useFwdBack = diff >= -moveAxisHysteresis;
                    else
                        useFwdBack = diff > moveAxisHysteresis;
                }
                if (useFwdBack)
                {
                    if (dpFwd < 0f)
                    {
                        target = directionalFrontSprite != null ? directionalFrontSprite : (defaultSprite != null ? defaultSprite : spriteRenderer.sprite);
                        flipX = lastFlipX; // preserve lateral flip on forward/back movement
                        lastWasBack = false;
                        // Update facing basis to camera so orbit logic starts from this sprite
                        Vector3 toCam = mainCamera.transform.position - transform.position; toCam.y = 0f;
                        if (toCam.sqrMagnitude > 0.000001f)
                        {
                            fourWayForward = toCam.normalized; // front faces camera
                            fourWayRight = Vector3.Cross(Vector3.up, fourWayForward).normalized;
                            fourWayBasisInit = true;
                        }
                    }
                    else
                    {
                        target = directionalBackSprite != null ? directionalBackSprite : (directionalFrontSprite != null ? directionalFrontSprite : (defaultSprite != null ? defaultSprite : spriteRenderer.sprite));
                        flipX = lastFlipX; // preserve lateral flip on forward/back movement
                        lastWasBack = true;
                        Vector3 toCam = mainCamera.transform.position - transform.position; toCam.y = 0f;
                        if (toCam.sqrMagnitude > 0.000001f)
                        {
                            fourWayForward = -toCam.normalized; // back faces away from camera
                            fourWayRight = Vector3.Cross(Vector3.up, fourWayForward).normalized;
                            fourWayBasisInit = true;
                        }
                    }
                    lastMoveAxis = 1;
                }
                else
                {
                    Sprite sideBase;
                    if (lastWasBack)
                        sideBase = directionalBackSprite != null ? directionalBackSprite : (directionalFrontSprite != null ? directionalFrontSprite : (defaultSprite != null ? defaultSprite : spriteRenderer.sprite));
                    else
                        sideBase = directionalFrontSprite != null ? directionalFrontSprite : (defaultSprite != null ? defaultSprite : spriteRenderer.sprite);

                    if (dpRight > 0f)
                    {
                        target = sideBase;
                        if (!lastWasBack)
                            flipX = false;
                        else
                            flipX = true;
                    }
                    else
                    {
                        target = sideBase;
                        if (!lastWasBack)
                            flipX = true;
                        else
                            flipX = false;
                    }
                    lastFlipX = flipX;
                    lastMoveAxis = 2;
                }
            }
            else
            {
                // Camera orbit-based: compare camera position relative to last movement-facing basis
                Vector3 toCam = mainCamera.transform.position - transform.position; toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.000001f)
                {
                    Vector3 dir = toCam.normalized;
                    if (!fourWayBasisInit)
                    {
                        fourWayForward = dir; // default to camera-facing if basis not yet set
                        fourWayRight = Vector3.Cross(Vector3.up, fourWayForward).normalized;
                        fourWayBasisInit = true;
                    }
                    float f = Vector3.Dot(fourWayForward, dir);
                    // Orbit affects only front/back, not left/right flip
                    if (f > 0f)
                    {
                        target = directionalFrontSprite != null ? directionalFrontSprite : (defaultSprite != null ? defaultSprite : spriteRenderer.sprite);
                        flipX = lastFlipX;
                        lastWasBack = false;
                    }
                    else
                    {
                        target = directionalBackSprite != null ? directionalBackSprite : (directionalFrontSprite != null ? directionalFrontSprite : (defaultSprite != null ? defaultSprite : spriteRenderer.sprite));
                        flipX = lastFlipX;
                        lastWasBack = true;
                    }
                }
            }

            if (spriteRenderer.sprite != target) spriteRenderer.sprite = target;
            spriteRenderer.flipX = flipX;
            lastSpriteEvalPos = transform.position;
        }
        if (lockYawTo4Angles && !billboardToCamera)
        {
            Vector3 e = transform.eulerAngles;
            float snappedY = Mathf.Round(e.y / 180f) * 180f;
            if (Mathf.Abs(Mathf.DeltaAngle(e.y, snappedY)) > 0.01f)
            {
                e.y = snappedY;
                transform.eulerAngles = e;
            }
        }
        if (SQUASH_NEAR_CAMERA && spriteTransform != null && mainCamera != null)
        {
            Vector3 camPos = mainCamera.transform.position;
            Vector3 camFwd = mainCamera.transform.forward;
            Vector3 toSprite = spriteTransform.position - camPos;
            float depth = Vector3.Dot(toSprite, camFwd);
            float near = mainCamera.nearClipPlane + SQUASH_NEAR_OFFSET;
            float t = 1f - Mathf.Clamp01((depth - near) / Mathf.Max(0.0001f, SQUASH_RANGE));
            if (t <= 0f)
            {
                Vector3 target = new Vector3(spriteBaseScale.x, spriteBaseScale.y, 1f);
                spriteTransform.localScale = Vector3.Lerp(spriteTransform.localScale, target, Time.deltaTime * SQUASH_LERP_SPEED);
            }
            else
            {
                Vector3 vp3 = mainCamera.WorldToViewportPoint(spriteTransform.position);
                float ax = Mathf.Abs(vp3.x - 0.5f);
                float ay = Mathf.Abs(vp3.y - 0.5f);
                float sum = Mathf.Max(0.0001f, ax + ay);
                float horizWeight = ax / sum;
                float vertWeight = ay / sum;
                float targetX = Mathf.Lerp(spriteBaseScale.x, spriteBaseScale.x * MAX_X_SCALE_AT_MAX_SQUASH, t * horizWeight);
                float targetY = Mathf.Lerp(spriteBaseScale.y, spriteBaseScale.y * MIN_Y_SCALE_AT_MAX_SQUASH, t * vertWeight);
                spriteTransform.localScale = Vector3.Lerp(spriteTransform.localScale, new Vector3(targetX, targetY, 1f), Time.deltaTime * SQUASH_LERP_SPEED);
            }
        }
    }
    
    // Usa posição do mouse para detectar foco do token
    private void CheckCenterScreenOver()
    {
        if (mainCamera == null)
            return;
        
        // Raycast a partir da posição do mouse na tela
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
        {
            if (hit.collider != null)
            {
                var ts = hit.collider.GetComponentInParent<TokenSetup>();
                isCenterScreenOver = (ts != null && ts.gameObject == gameObject);
            }
            else
            {
                isCenterScreenOver = false;
            }
        }
        else
        {
            isCenterScreenOver = false;
        }
    }
    
    private void SetupSpriteMaterial()
    {
        if (spriteRenderer == null)
            return;
        
        // Try to find lit sprite shader
        Shader litShader = Shader.Find("Custom/SpriteLitBillboard");
        if (litShader != null)
        {
            spriteMaterial = new Material(litShader);
            spriteRenderer.material = spriteMaterial;
            spriteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            spriteRenderer.receiveShadows = true;
            Debug.Log($"Applied Custom/SpriteLitBillboard shader to {gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"Custom/SpriteLitBillboard shader not found! Falling back to default material on {gameObject.name}");
        }
    }
    
    #region State Management
    
    public void SetState(int state)
    {
        currentState = state;
        ApplyState(state);
    }
    
    private void ApplyState(int state)
    {
        if (spriteRenderer == null)
            return;
        
        switch (state)
        {
            case 1: // Default sprite, normal
                spriteRenderer.sprite = defaultSprite;
                spriteRenderer.flipX = false;
                break;
                
            case 2: // Default sprite, flipped
                spriteRenderer.sprite = defaultSprite;
                spriteRenderer.flipX = true;
                break;
                
            case 3: // Alternative sprite 1, normal
                if (alternativeSprite1 != null)
                    spriteRenderer.sprite = alternativeSprite1;
                else
                    Debug.LogWarning($"Alternative Sprite 1 not assigned on {gameObject.name}");
                spriteRenderer.flipX = false;
                break;
                
            case 4: // Alternative sprite 1, flipped
                if (alternativeSprite1 != null)
                    spriteRenderer.sprite = alternativeSprite1;
                else
                    Debug.LogWarning($"Alternative Sprite 1 not assigned on {gameObject.name}");
                spriteRenderer.flipX = true;
                break;
                
            case 5: // Alternative sprite 2, normal
                if (alternativeSprite2 != null)
                    spriteRenderer.sprite = alternativeSprite2;
                else
                    Debug.LogWarning($"Alternative Sprite 2 not assigned on {gameObject.name}");
                spriteRenderer.flipX = false;
                break;
                
            case 6: // Alternative sprite 2, flipped
                if (alternativeSprite2 != null)
                    spriteRenderer.sprite = alternativeSprite2;
                else
                    Debug.LogWarning($"Alternative Sprite 2 not assigned on {gameObject.name}");
                spriteRenderer.flipX = true;
                break;
        }
    }
    
    #endregion
    
    #region Scale Management
    
    public void IncreaseScale()
    {
        float newScale = Mathf.Min(currentScale + scaleStep, maxScale);
        ApplyScale(newScale);
    }
    
    public void DecreaseScale()
    {
        float newScale = Mathf.Max(currentScale - scaleStep, minScale);
        ApplyScale(newScale);
    }
    
    public void SetScale(float scale)
    {
        currentScale = Mathf.Clamp(scale, minScale, maxScale);
        ApplyScale(currentScale);
    }
    
    private void ApplyScale(float scale)
    {
        currentScale = scale;
        transform.localScale = Vector3.one * scale;
    }
    
    public float GetCurrentScale()
    {
        return currentScale;
    }
    
    #endregion
    
    private void SetupComponents()
    {
        // Find sprite renderer
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteTransform = spriteRenderer.transform;
            spriteTransform.localPosition = Vector3.zero;
            spriteTransform.localRotation = Quaternion.identity;
        }
        else
        {
            Debug.LogWarning($"No SpriteRenderer found on {gameObject.name} or its children!");
        }
        
        // Setup Rigidbody (optional)
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            // Tokens rely on snapping, not physics
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            // Freeze X and Z rotation to keep token upright
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        
        // Setup Box Collider - user configures manually
        boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null)
        {
            // Remove CapsuleCollider if exists
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                DestroyImmediate(capsule);
            }
            
            boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(1f, 0.2f, 1f);
            boxCollider.center = new Vector3(0, 0.1f, 0);
            
            Debug.Log($"Created BoxCollider on {gameObject.name}. Adjust size/position manually in Inspector.");
        }
        
        
    }
    
    private void OnValidate()
    {
        if (Application.isPlaying)
            return;
        
        // No RB tuning for tokens; snapping drives placement
        
        // Clamp scale
        currentScale = Mathf.Clamp(currentScale, minScale, maxScale);

        if (simplePhysicsMode)
        {
            EnforceSimplePhysics();
        }
    }

    

    

    

    

    

    

    

    

    

    private void EnforceSimplePhysics()
    {
        // Do not remove NetworkedToken here

        // Force cube-like rigidbody
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.drag = 0f;
            rb.angularDrag = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.detectCollisions = true;
            rb.WakeUp();
        }

        // Ensure colliders are solid
        var cols = GetComponentsInChildren<Collider>();
        if (cols != null)
        {
            foreach (var c in cols)
            {
                c.isTrigger = false;
            }
        }
    }
    
    public void SetPhysicsMaterial(PhysicMaterial material)
    {
        if (boxCollider != null)
        {
            boxCollider.material = material;
        }
    }
    
    public void SetPhysicsEnabled(bool enabled)
    {
        if (rb != null)
        {
            rb.isKinematic = !enabled;
            rb.useGravity = enabled;
        }
    }
    
    public int GetCurrentState()
    {
        return currentState;
    }
    
    public void MarkSpriteEvalNow()
    {
        lastSpriteEvalPos = transform.position;
    }
    public void NudgeFourWayFacingByQuarterTurns(int quarterTurns)
    {
        if (quarterTurns == 0) return;
        if (!fourWayBasisInit)
        {
            Vector3 toCam = mainCamera != null ? (mainCamera.transform.position - transform.position) : Vector3.forward;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.000001f)
            {
                fourWayForward = toCam.normalized;
                fourWayRight = Vector3.Cross(Vector3.up, fourWayForward).normalized;
                fourWayBasisInit = true;
            }
        }
        int turns = ((quarterTurns % 4) + 4) % 4;
        for (int i = 0; i < turns; i++)
        {
            Vector3 nf, nr;
            // Positive turn: rotate basis +90 degrees around Y (forward -> right)
            nf = fourWayRight;
            nr = -fourWayForward;
            fourWayForward = nf;
            fourWayRight = nr;
        }
        if (quarterTurns < 0)
        {
            // Adjust for negative by rotating 4 - turns more (equivalent to -|turns|)
            int negTurns = ((-quarterTurns) % 4);
            for (int i = 0; i < negTurns; i++)
            {
                Vector3 nf = -fourWayRight;
                Vector3 nr = fourWayForward;
                fourWayForward = nf;
                fourWayRight = nr;
            }
        }
    }
    public bool IsFourWayRotationLocked { get { return lockYawTo4Angles; } }
    public void ToggleSpriteFlipX()
    {
        if (spriteRenderer != null)
        {
            bool f = !spriteRenderer.flipX;
            spriteRenderer.flipX = f;
            lastFlipX = f;
        }
    }
    
    // Debug visualization
    private void OnDrawGizmosSelected()
    {
        // Draw collider bounds
        BoxCollider col = GetComponent<BoxCollider>();
        if (col != null)
        {
            Gizmos.color = isCenterScreenOver ? Color.yellow : Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(col.center, col.size);
        }
        
        // Draw sprite bounds
        SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(sr.bounds.center, sr.bounds.size);
        }
    }
    
    /// <summary>
    /// Finds the local player's camera for multiplayer support
    /// Each player will see billboards oriented to their own camera
    /// </summary>
    private void FindLocalPlayerCamera()
    {
        Camera[] cameras = Object.FindObjectsOfType<Camera>();
        
        // First pass: strictly prefer the local player's camera
        foreach (Camera cam in cameras)
        {
            if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;
            var networkPlayer = cam.GetComponentInParent<Nexus.Networking.NetworkPlayer>();
            if (networkPlayer != null && networkPlayer.isLocalPlayer)
            {
                mainCamera = cam;
                return;
            }
        }
        
        // Second pass: fallback to any enabled MainCamera
        foreach (Camera cam in cameras)
        {
            if (!cam.enabled || !cam.gameObject.activeInHierarchy) continue;
            if (cam.CompareTag("MainCamera"))
            {
                mainCamera = cam;
                return;
            }
        }
        
        // Final fallback: Camera.main
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }
}