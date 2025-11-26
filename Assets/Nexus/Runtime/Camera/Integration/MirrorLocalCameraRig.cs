using Mirror;
using UnityEngine;

public class MirrorLocalCameraRig : NetworkBehaviour
{
    [Header("Rig Prefab")]
    [Tooltip("Assign a prefab that contains: CameraRigController on root, child 'FollowProxy' and 'LookProxy', and a Cinemachine Virtual Camera inside.")]
    public GameObject cameraRigPrefab;

    [Header("Parenting")] 
    public bool parentUnderPlayer = true;
    public Transform parentOverride;

    [Header("Options")]
    [Tooltip("If there's a scene rig present, disable/destroy it when local rig spawns.")]
    public bool disableSceneRigIfLocal = true;

    [Header("Camera Override")]
    [Tooltip("If set, this Camera will be used instead of Camera.main (useful when multiple MainCamera tags exist, e.g. menu + gameplay).")]
    public Camera gameplayCameraOverride;

    GameObject localRig;

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        if (Application.isBatchMode) return; // headless server safety
        SpawnLocalRig();
    }

    void SpawnLocalRig()
    {
        if (localRig != null) return;
        if (!cameraRigPrefab)
        {
            Debug.LogError("MirrorLocalCameraRig: cameraRigPrefab is not assigned on Player prefab.");
            return;
        }

        if (disableSceneRigIfLocal)
        {
            var sceneRigs = Object.FindObjectsOfType<CameraRigController>(true);
            foreach (var r in sceneRigs)
            {
                if (r && r.gameObject.scene.IsValid())
                {
                    r.gameObject.SetActive(false);
                }
            }
        }

        localRig = Instantiate(cameraRigPrefab);
        localRig.name = cameraRigPrefab.name + " (Local)";

        if (parentUnderPlayer)
        {
            var parent = parentOverride ? parentOverride : transform;
            localRig.transform.SetParent(parent, worldPositionStays: false);
            localRig.transform.localPosition = Vector3.zero;
            localRig.transform.localRotation = Quaternion.identity;
        }

        WireUp(localRig);
    }

    void WireUp(GameObject rigGO)
    {
        var rig = rigGO.GetComponentInChildren<CameraRigController>(true);
        if (!rig)
        {
            Debug.LogWarning("MirrorLocalCameraRig: No CameraRigController found on rig prefab.");
        }

        var mainCam = gameplayCameraOverride ? gameplayCameraOverride : Camera.main;

        // Ensure a picker exists
        var picker = rigGO.GetComponentInChildren<CameraTokenPicker>(true);
        if (!picker)
        {
            var input = new GameObject("CameraInput");
            input.transform.SetParent(rigGO.transform, false);
            picker = input.AddComponent<CameraTokenPicker>();
        }

        if (picker)
        {
            picker.rig = rig;
            picker.rayCamera = mainCam ? mainCam : picker.rayCamera;
        }
    }
}
