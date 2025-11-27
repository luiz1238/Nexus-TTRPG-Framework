using UnityEngine;
using UnityEngine.EventSystems;

public class CameraTokenPicker : MonoBehaviour
{
    public CameraRigController rig;
    public Camera rayCamera;
    public LayerMask tokenMask = ~0;

    [Header("Select/Follow Settings")]
    [SerializeField] bool followOnSelect = true;
    [SerializeField] bool holdShiftToInvertFollow = true;
    [SerializeField] KeyCode toggleFollowKey = KeyCode.F;
    [SerializeField] KeyCode clearFocusKey = KeyCode.Escape;

    void Awake()
    {
        if (!rayCamera)
        {
            rayCamera = (Nexus.CameraManager.Instance != null ? Nexus.CameraManager.Instance.MainCamera : Camera.main);
        }
        if (!rig) rig = FindObjectOfType<CameraRigController>();
    }

    void Update()
    {
        // Refresh ray camera each frame to follow active player camera
        if (Nexus.CameraManager.Instance != null && Nexus.CameraManager.Instance.MainCamera != null)
            rayCamera = Nexus.CameraManager.Instance.MainCamera;
        else if (!rayCamera)
            rayCamera = Camera.main;

        if (rayCamera == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;
            var token = RaycastForToken(Input.mousePosition);
            if (token)
            {
                bool follow = followOnSelect;
                if (holdShiftToInvertFollow && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                    follow = !followOnSelect;
                if (rig) rig.FocusOnToken(token, follow);
            }
        }

        if (Input.GetKeyDown(toggleFollowKey))
        {
            if (rig)
            {
                bool newFollow = !rig.IsFollowing();
                rig.SetFollow(newFollow);
            }
        }

        if (Input.GetKeyDown(clearFocusKey) || Input.GetMouseButtonDown(1))
        {
            if (rig) rig.ClearFocus();
        }
    }

    CameraTokenTarget RaycastForToken(Vector3 screenPos)
    {
        if (!rayCamera) return null;
        Ray ray = rayCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out var hit, 1000f, tokenMask, QueryTriggerInteraction.Ignore))
        {
            var token = hit.collider.GetComponentInParent<CameraTokenTarget>();
            return token;
        }
        return null;
    }
}
