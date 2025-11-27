using UnityEngine;
using Cinemachine;
using UnityEngine.Rendering.PostProcessing;
using Tenkoku.Core;

namespace Nexus
{
    /* Free-fly camera (refined)
     * - 360° rotation
     * - Shift + Scroll adjusts speed
     * - Simple controls
     */
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float baseSpeed = 5f;
        [SerializeField] private float scrollStep = 1f;
        [SerializeField] private float minSpeed = 1f;
        [SerializeField] private float maxSpeed = 100f;
        [SerializeField] private bool usePlanarMovement = false;

        [Header("Mouse Look Settings")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Transform pitchPivotOverride;
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private float verticalClamp = 179f;
        [SerializeField] private float mouseSmoothTime = 0.05f;

        [Header("Keybinds")]
        [SerializeField] private KeyCode upKey = KeyCode.Space;
        [SerializeField] private KeyCode downKey = KeyCode.LeftControl;

        [Header("Roll/Tilt")]
        [SerializeField] private float rollSpeed = 45f;
        [SerializeField] private float rollSmoothTime = 0.2f;
        [SerializeField] private float rollClamp = 179f;

        [Header("Wiggle")]
        [SerializeField] private bool enableWiggle = true;
        [SerializeField] private float wiggleAmplitude = 0.03f;
        [SerializeField] private float wiggleFrequency = 2f;
        [SerializeField] private int wiggleSeed = 0;
        [SerializeField] private float wiggleAmplitudeXY = 0.015f;
        [SerializeField] private float wiggleRotAmplitudePitch = 0.35f;
        [SerializeField] private float wiggleRotAmplitudeYaw = 0.35f;

        [Header("Cinemachine")]
        [SerializeField] private CinemachineVirtualCamera vcam;
        [SerializeField] private float zoomMin = -30f;
        [SerializeField] private float zoomMax = -2f;
        [SerializeField] private float zoomSensitivity = 2f;
        [SerializeField] private float zoomSmoothTime = 0.08f;
        [SerializeField] private bool scrollZoomUsesFOV = true;
        [SerializeField] private float scrollFOVMin = 30f;
        [SerializeField] private float scrollFOVMax = 90f;
        [SerializeField] private float scrollFOVSensitivity = 2f;

        [Header("FOV (Dynamic)")]
        [SerializeField] private bool dynamicFOV = true;
        [SerializeField] private float idleFOV = 60f;
        [SerializeField] private float maxFOV = 75f;
        [SerializeField] private float speedForMaxFOV = 8f;
        [SerializeField] private float fovSmoothTime = 0.2f;

        [Header("Movement Smoothing")]
        [SerializeField] private float positionSmoothTime = 0.08f;
        [SerializeField] private float maxMoveSpeed = 100f;

        [Header("Depth of Field (PostProcessing v2)")]
        [SerializeField] private bool dofEnable = true;
        [SerializeField] private bool dofFocusCenterRay = true;
        [SerializeField] private LayerMask dofRayMask = ~0;
        [SerializeField] private float dofMaxRayDistance = 500f;
        [SerializeField] private float dofDefaultDistance = 5f;
        [SerializeField] private float dofFocusSmoothTime = 0.08f;
        [SerializeField] private float dofAperture = 8f;
        [SerializeField] private float dofFocalLength = 50f;

        private float verticalRotation = 0f;
        private float currentSpeed;
        private float targetYaw;
        private float currentYaw;
        private float currentPitch;
        private float yawVelocity;
        private float pitchVelocity;
        private float targetRoll;
        private float currentRoll;
        private float rollVelocity;
        private Vector3 moveVelocity;
        private float measuredSpeed;
        private Vector3 lastPivotPos;

        private CinemachineTransposer transposer;
        private Vector3 baseFollowOffset = new Vector3(0f, 0f, -10f);
        private float targetZoomZ = -10f;
        private float zoomZVelocity;
        private float currentFov;
        private float fovVelocity;
        private float targetFovScroll;
        private DepthOfField dof;
        private float dofFocusCurrent;
        private float dofFocusVelocity;

        public bool InputLocked { get; set; } = false;

        private void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            currentSpeed = baseSpeed;
            targetYaw = transform.eulerAngles.y;
            currentYaw = targetYaw;
            currentPitch = 0f;

            // Auto-find vcam and transposer
            if (vcam == null)
                vcam = GetComponentInChildren<CinemachineVirtualCamera>(true);
            if (vcam != null)
            {
                transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
                if (transposer != null)
                {
                    // Ensure offset is in target-local space (includes pitch) so zoom follows true camera forward
                    transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTarget;

                    // Keep only Z as base so zoom doesn't drift sideways/upwards
                    baseFollowOffset = new Vector3(0f, 0f, transposer.m_FollowOffset.z);
                    targetZoomZ = baseFollowOffset.z;
                }
                // Avoid lateral offsets caused by Collider obstacle avoidance
                var vcamCollider = vcam.GetComponent<CinemachineCollider>();
                if (vcamCollider != null)
                    vcamCollider.enabled = false;
                currentFov = idleFOV;
                vcam.m_Lens.FieldOfView = currentFov;
                targetFovScroll = currentFov;
            }

            // Find a global PostProcessVolume with DepthOfField
            if (dofEnable)
            {
                var volumes = Object.FindObjectsOfType<PostProcessVolume>();
                PostProcessVolume best = null;
                float bestPriority = float.NegativeInfinity;
                foreach (var vol in volumes)
                {
                    if (!vol.enabled || vol.profile == null) continue;
                    if (!vol.isGlobal) continue;
                    if (vol.priority >= bestPriority)
                    {
                        best = vol;
                        bestPriority = vol.priority;
                    }
                }
                if (best != null && best.profile != null)
                {
                    best.profile.TryGetSettings(out dof);
                    if (dof != null)
                    {
                        dof.aperture.value = dofAperture;
                        dof.focalLength.value = dofFocalLength;
                        dofFocusCurrent = dofDefaultDistance;
                        dof.focusDistance.value = dofFocusCurrent;
                    }
                }
            }

            lastPivotPos = transform.position;

            // Bind Tenkoku sky system to this camera (offline or fallback)
            if (playerCamera != null)
            {
                playerCamera.tag = "MainCamera";
                var tenModules = Object.FindObjectsOfType<TenkokuModule>();
                foreach (var ten in tenModules)
                {
                    if (ten == null) continue;
                    ten.mainCamera = playerCamera.transform;
                    ten.manualCamera = playerCamera.transform;
                    ten.useCamera = playerCamera.transform;
                    ten.useCameraCam = playerCamera;
                    if (ten.GetComponent<Nexus.TenkokuCameraBinder>() == null)
                    {
                        ten.gameObject.AddComponent<Nexus.TenkokuCameraBinder>();
                    }
                }
            }
        }

        private void Update()
        {
            if (InputLocked)
                return;

            HandleSpeedAdjust();
            HandleMovement();
            HandleMouseLook();
            HandleRoll();

            // Apply orientation every frame (own axis)
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, currentRoll);

            // Measure speed for dynamic FOV
            float dt = Time.deltaTime;
            measuredSpeed = dt > 0f ? (transform.position - lastPivotPos).magnitude / dt : 0f;
            lastPivotPos = transform.position;

            // FOV handling: scroll FOV zoom OR speed-based dynamic FOV
            if (vcam != null)
            {
                if (scrollZoomUsesFOV)
                {
                    currentFov = Mathf.SmoothDamp(currentFov, targetFovScroll, ref fovVelocity, fovSmoothTime);
                    vcam.m_Lens.FieldOfView = currentFov;
                }
                else if (dynamicFOV)
                {
                    float tspd = Mathf.Clamp01(measuredSpeed / Mathf.Max(0.0001f, speedForMaxFOV));
                    float targetFov = Mathf.Lerp(idleFOV, maxFOV, tspd);
                    currentFov = Mathf.SmoothDamp(currentFov, targetFov, ref fovVelocity, fovSmoothTime);
                    vcam.m_Lens.FieldOfView = currentFov;
                }
            }

            // Ensure orientation matches free-fly controller when Aim = Do Nothing
            if (vcam != null)
            {
                vcam.transform.rotation = transform.rotation;
            }

            // Drive DOF focus distance from center raycast
            if (dofEnable && dof != null)
            {
                Transform camT = playerCamera ? playerCamera.transform : (Camera.main ? Camera.main.transform : null);
                float targetFocus = dofDefaultDistance;
                if (dofFocusCenterRay && camT != null)
                {
                    Ray ray = new Ray(camT.position, camT.forward);
                    if (Physics.Raycast(ray, out RaycastHit hit, dofMaxRayDistance, dofRayMask, QueryTriggerInteraction.Ignore))
                    {
                        targetFocus = hit.distance;
                    }
                }
                dofFocusCurrent = Mathf.SmoothDamp(dofFocusCurrent, targetFocus, ref dofFocusVelocity, dofFocusSmoothTime);
                dof.focusDistance.value = Mathf.Max(0.01f, dofFocusCurrent);
            }
        }

        private void HandleSpeedAdjust()
        {
            if (Input.GetKey(KeyCode.LeftShift))
            {
                float scroll = Input.mouseScrollDelta.y;
                if (scroll != 0f)
                {
                    currentSpeed += scroll * scrollStep;
                    currentSpeed = Mathf.Clamp(currentSpeed, minSpeed, maxSpeed);
                    Debug.Log($"Camera Speed: {currentSpeed:F1}");
                }
            }
            else
            {
                float scroll = Input.mouseScrollDelta.y;
                // Do not apply camera zoom when holding R (reserved for token rotation)
                if (scroll != 0f && !Input.GetKey(KeyCode.R))
                {
                    if (scrollZoomUsesFOV)
                    {
                        targetFovScroll = Mathf.Clamp(targetFovScroll + (-scroll * scrollFOVSensitivity), scrollFOVMin, scrollFOVMax);
                    }
                    else
                    {
                        targetZoomZ = Mathf.Clamp(targetZoomZ + scroll * zoomSensitivity, zoomMin, zoomMax);
                    }
                }
            }
        }

        private void HandleMovement()
        {
            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");

            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            if (usePlanarMovement)
            {
                forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
                right = Vector3.Cross(Vector3.up, forward).normalized;
            }

            Vector3 move = forward * vertical + right * horizontal;

            if (Input.GetKey(upKey)) move += Vector3.up;
            if (Input.GetKey(downKey)) move += Vector3.down;

            if (move.sqrMagnitude > 1f) move.Normalize();

            Vector3 targetPos = transform.position + move * currentSpeed * Time.deltaTime;
            transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref moveVelocity, positionSmoothTime, maxMoveSpeed, Time.deltaTime);
        }

        private void HandleMouseLook()
        {
            if (!Input.GetMouseButton(1))
                return;

            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            targetYaw += mouseX;
            verticalRotation -= mouseY;

            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, mouseSmoothTime);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, verticalRotation, ref pitchVelocity, mouseSmoothTime);
        }

        private void HandleRoll()
        {
            float dt = Time.deltaTime;
            float rollInput = 0f;
            if (Input.GetKey(KeyCode.Q)) rollInput -= 1f;
            if (Input.GetKey(KeyCode.E)) rollInput += 1f;
            if (Mathf.Abs(rollInput) > 0.001f)
            {
                targetRoll += rollInput * rollSpeed * dt;
                targetRoll = Mathf.Repeat(targetRoll + 180f, 360f) - 180f;
            }
            currentRoll = Mathf.SmoothDampAngle(currentRoll, targetRoll, ref rollVelocity, rollSmoothTime);
        }

        private void LateUpdate()
        {
            // Apply zoom & wiggle to Cinemachine Transposer follow offset
            if (transposer != null)
            {
                Vector3 off;
                if (scrollZoomUsesFOV)
                {
                    // Free-fly: no positional offset when using FOV zoom (avoid orbiting)
                    off = Vector3.zero;
                }
                else
                {
                    float z = Mathf.SmoothDamp(transposer.m_FollowOffset.z, targetZoomZ, ref zoomZVelocity, zoomSmoothTime);
                    off = baseFollowOffset;
                    off.z = z;
                    if (enableWiggle)
                    {
                        float t = Time.time * wiggleFrequency;
                        float ax = Mathf.Sin(t + wiggleSeed * 0.73f);
                        float ay = Mathf.Cos(t * 1.31f + wiggleSeed * 1.17f);
                        float az = Mathf.Sin(t * 0.93f + wiggleSeed * 0.51f);
                        off.x += ax * wiggleAmplitudeXY;
                        off.y += ay * wiggleAmplitudeXY;
                        off.z += az * (wiggleAmplitude * 0.5f);
                    }
                }
                transposer.m_FollowOffset = off;
                if (vcam != null && enableWiggle)
                {
                    float tR = Time.time * wiggleFrequency;
                    float rPitch = Mathf.Sin(tR * 1.07f + wiggleSeed * 2.11f) * wiggleRotAmplitudePitch;
                    float rYaw = Mathf.Cos(tR * 0.89f + wiggleSeed * 1.59f) * wiggleRotAmplitudeYaw;
                    vcam.transform.rotation = transform.rotation * Quaternion.Euler(rPitch, rYaw, 0f);
                }
            }
        }
    }
}