using UnityEngine;

public class TokenDraggable : MonoBehaviour
{
    private bool isDragging = false;
    private Camera mainCamera;
    private Collider cachedCollider;
    private Vector3 lastMousePos;
    private static int draggingCount = 0;
    public static bool IsAnyDragging { get { return draggingCount > 0; } }

    void Start()
    {
        mainCamera = Camera.main;
        cachedCollider = GetComponent<Collider>();
        if (cachedCollider == null) cachedCollider = GetComponentInChildren<Collider>();
        if (cachedCollider == null)
        {
            Debug.LogWarning("O Token precisa de um Collider para ser arrastado!");
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.transform == transform)
                {
                    if (!isDragging)
                    {
                        isDragging = true;
                        draggingCount++;
                    }
                    lastMousePos = Input.mousePosition;
                }
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            if (isDragging)
            {
                isDragging = false;
                if (draggingCount > 0) draggingCount--;
            }
        }

        if (isDragging)
        {
            Vector3 mp = Input.mousePosition;
            if ((mp - lastMousePos).sqrMagnitude > 0.0001f)
            {
                MoveTokenToMouse();
                lastMousePos = mp;
            }
        }
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (isDragging && Mathf.Abs(scroll) > 0.001f)
        {
            var ts = GetComponent<TokenSetup>();
            if (ts == null) ts = GetComponentInChildren<TokenSetup>();
            if (ts != null)
            {
                ts.ToggleSpriteFlipX();
            }
        }
    }

    void MoveTokenToMouse()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        int groundMask = LayerMask.GetMask("Ground");
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundMask))
        {
            float pivotToBottom = 0f;
            if (cachedCollider != null)
            {
                pivotToBottom = cachedCollider.bounds.min.y - transform.position.y;
            }
            float newY = hit.point.y - pivotToBottom;
            transform.position = new Vector3(hit.point.x, newY, hit.point.z);
        }
    }
}
