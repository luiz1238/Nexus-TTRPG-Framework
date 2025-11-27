using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Token spawner UI with template-based system.
/// Manually bind all UI elements in the inspector.
/// Templates are cloned and populated with data.
/// </summary>
public class TokenSpawnerUI : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private TokenLibraryManager libraryManager;
    [SerializeField] private Camera mainCamera;
    
    [Header("UI Panel")]
    [SerializeField] private GameObject uiPanel;
    
    [Header("Templates (Assign from scene hierarchy)")]
    [SerializeField] private GameObject categoryButtonTemplate;
    [SerializeField] private GameObject tokenCardTemplate;
    
    [Header("Containers (Where clones go)")]
    [SerializeField] private Transform categoryContainer;
    [SerializeField] private Transform tokenGridContainer;
    
    [Header("Controls")]
    [SerializeField] private TMP_InputField searchField;
    [SerializeField] private Button refreshButton;
    
    [Header("Spawn Settings")]
    [SerializeField] private float spawnDistance = 2f;
    [SerializeField] private float spawnHeightAboveGround = 0f;
    [SerializeField] private float maxRaycastDistance = 100f;
    [SerializeField] private float collisionCheckRadius = 0.5f;
    [SerializeField] private int maxSpawnAttempts = 10;
    [SerializeField] private LayerMask spawnSurfaceMask = ~0; // all by default
    
    // Internal state
    private string currentCategory = "";
    private Dictionary<string, GameObject> categoryButtons = new Dictionary<string, GameObject>();
    private List<GameObject> tokenCards = new List<GameObject>();
    
    private void Start()
    {
        if (libraryManager == null)
            libraryManager = FindObjectOfType<TokenLibraryManager>();
        
        if (mainCamera == null)
            FindLocalPlayerCamera();
        
        if (refreshButton != null)
            refreshButton.onClick.AddListener(RefreshLibrary);
        
        if (searchField != null)
            searchField.onValueChanged.AddListener(OnSearchChanged);
        
        HideTemplates();
        
        if (uiPanel != null)
            uiPanel.SetActive(false);
        
        LoadCategories();
    }

    
    
    private void Update()
    {
        // Find camera if not found yet (for multiplayer)
        if (mainCamera == null || !IsLocalPlayerCamera(mainCamera))
        {
            FindLocalPlayerCamera();
        }
        
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleUI();
        }
    }
    
    private void HideTemplates()
    {
        if (categoryButtonTemplate != null)
            categoryButtonTemplate.SetActive(false);
        
        if (tokenCardTemplate != null)
            tokenCardTemplate.SetActive(false);
    }
    
    public void ToggleUI()
    {
        if (uiPanel == null) return;
        
        bool isOpen = !uiPanel.activeSelf;
        uiPanel.SetActive(isOpen);
        
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        var tabletop = FindObjectOfType<Nexus.TabletopManager>();
        if (tabletop != null)
            tabletop.InputLocked = isOpen;
        
        var playerController = FindObjectOfType<Nexus.PlayerController>();
        if (playerController != null)
            playerController.InputLocked = isOpen;
    }
    
    private void LoadCategories()
    {
        if (libraryManager == null || categoryContainer == null || categoryButtonTemplate == null)
            return;
        
        ClearContainer(categoryContainer, categoryButtonTemplate);
        categoryButtons.Clear();
        
        List<TokenCategory> categories = libraryManager.GetAllCategories();
        
        foreach (TokenCategory category in categories)
        {
            GameObject clone = Instantiate(categoryButtonTemplate, categoryContainer);
            clone.SetActive(true);
            clone.name = category.categoryName;
            
            TextMeshProUGUI text = clone.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.text = $"{category.categoryName} ({category.tokens.Count})";
            
            Button btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                string catName = category.categoryName;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectCategory(catName));
            }
            
            categoryButtons[category.categoryName] = clone;
        }
        
        if (categories.Count > 0)
            SelectCategory(categories[0].categoryName);
    }
    
    public void SelectCategory(string categoryName)
    {
        currentCategory = categoryName;
        UpdateCategoryVisuals();
        LoadTokens();
    }
    
    private void UpdateCategoryVisuals()
    {
        foreach (var kvp in categoryButtons)
        {
            bool isSelected = kvp.Key == currentCategory;
            
            Transform selectedMarker = kvp.Value.transform.Find("Selected");
            if (selectedMarker != null)
                selectedMarker.gameObject.SetActive(isSelected);
        }
    }
    
    private void LoadTokens()
    {
        if (libraryManager == null || tokenGridContainer == null || tokenCardTemplate == null)
            return;
        
        ClearContainer(tokenGridContainer, tokenCardTemplate);
        tokenCards.Clear();
        
        List<TokenData> tokens = libraryManager.GetTokensInCategory(currentCategory);
        
        if (searchField != null && !string.IsNullOrWhiteSpace(searchField.text))
        {
            string search = searchField.text.ToLower();
            tokens = tokens.FindAll(t => t.tokenName.ToLower().Contains(search));
        }
        
        foreach (TokenData token in tokens)
        {
            GameObject clone = Instantiate(tokenCardTemplate, tokenGridContainer);
            clone.SetActive(true);
            clone.name = token.tokenName;
            
            RawImage preview = clone.GetComponentInChildren<RawImage>();
            if (preview != null)
            {
                Sprite sprite = token.GetSprite();
                if (sprite != null)
                {
                    preview.texture = sprite.texture;
                    Rect r = sprite.rect;
                    preview.uvRect = new Rect(
                        r.x / sprite.texture.width,
                        r.y / sprite.texture.height,
                        r.width / sprite.texture.width,
                        r.height / sprite.texture.height
                    );
                }
            }
            
            TextMeshProUGUI text = clone.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
                text.text = token.tokenName;
            
            Button btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                TokenData tokenRef = token;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SpawnToken(tokenRef));
            }
            
            tokenCards.Add(clone);
        }
    }
    
    private void SpawnToken(TokenData tokenData)
    {
        if (tokenData == null || mainCamera == null) return;
        
        Vector3 spawnPos = CalculateSpawnPosition();
        
        // If in a networked session, request server to spawn so it replicates to all clients
        if (Mirror.NetworkServer.active || Mirror.NetworkClient.isConnected)
        {
            var localPlayerIdentity = Mirror.NetworkClient.localPlayer;
            var localPlayer = localPlayerIdentity != null
                ? localPlayerIdentity.GetComponent<Nexus.Networking.NetworkPlayer>()
                : FindLocalNetworkPlayer();
            if (localPlayer != null)
            {
                localPlayer.CmdSpawnTokenByName(tokenData.tokenName, spawnPos);
            }
        }
        else
        {
            // Offline/local: spawn locally
            GameObject spawned = libraryManager.SpawnToken(tokenData, spawnPos, Quaternion.identity);
            if (spawned != null)
            {
                // No debug: leave physics to the prefab/defaults
            }
        }
    }
    
    private Vector3 CalculateSpawnPosition()
    {
        // Use mouse position on screen to raycast, clamp max distance to prevent horizon spawns
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        int mask = spawnSurfaceMask.value != 0 ? spawnSurfaceMask.value : Physics.DefaultRaycastLayers;
        float clampDistance = Mathf.Min(maxRaycastDistance, 15f);
        RaycastHit[] hits = Physics.RaycastAll(ray, clampDistance, mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        var localPlayer = Mirror.NetworkClient.isConnected ? FindLocalNetworkPlayer() : null;
        Transform localRoot = localPlayer != null ? localPlayer.transform : null;
        float camY = mainCamera.transform.position.y;
        for (int i = 0; i < hits.Length; i++)
        {
            if (localRoot != null && hits[i].transform.IsChildOf(localRoot)) continue;
            if (hits[i].normal.y >= 0.4f && hits[i].point.y <= camY + 0.01f)
                return hits[i].point; // prefer walkable surfaces below or at camera height
        }

        // Fallback: fixed forward clamp, then drop to ground
        Vector3 forwardPoint = mainCamera.transform.position + mainCamera.transform.forward * Mathf.Max(1f, Mathf.Min(clampDistance, spawnDistance));
        int maskDown = spawnSurfaceMask.value != 0 ? spawnSurfaceMask.value : Physics.DefaultRaycastLayers;
        RaycastHit[] downs = Physics.RaycastAll(new Ray(forwardPoint + Vector3.up * 10f, Vector3.down), clampDistance, maskDown, QueryTriggerInteraction.Ignore);
        System.Array.Sort(downs, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < downs.Length; i++)
        {
            if (downs[i].normal.y >= 0.4f && downs[i].point.y <= camY + 0.01f)
                return downs[i].point; // choose highest valid below camera
        }
        return forwardPoint;
    }
    
    private void AdjustTokenToGround(GameObject token)
    {
        Collider[] colliders = token.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) return;
        
        Bounds bounds = colliders[0].bounds;
        foreach (Collider col in colliders)
            bounds.Encapsulate(col.bounds);
        
        RaycastHit hit;
        if (Physics.Raycast(token.transform.position + Vector3.up * 10f, Vector3.down, out hit, maxRaycastDistance))
        {
            float adjustment = (hit.point.y + spawnHeightAboveGround) - bounds.min.y;
            Vector3 newPos = token.transform.position + Vector3.up * adjustment;
            
            if (HasCollision(newPos, bounds, token))
                newPos = FindFreePosition(newPos, bounds, token);
            
            token.transform.position = newPos;
        }
    }
    
    private bool HasCollision(Vector3 position, Bounds bounds, GameObject token)
    {
        Vector3 center = position + (bounds.center - token.transform.position);
        Collider[] overlaps = Physics.OverlapBox(center, bounds.extents * 0.95f);
        
        foreach (Collider col in overlaps)
        {
            if (col.transform.IsChildOf(token.transform) || col.gameObject == token)
                continue;
            return true;
        }
        return false;
    }
    
    private Vector3 FindFreePosition(Vector3 pos, Bounds bounds, GameObject token)
    {
        for (int i = 0; i < maxSpawnAttempts; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            float dist = collisionCheckRadius * (1 + i * 0.3f);
            Vector3 offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            Vector3 testPos = pos + offset;
            
            if (!HasCollision(testPos, bounds, token))
                return testPos;
        }
        return pos;
    }
    
    private void RefreshLibrary()
    {
        if (libraryManager != null)
            libraryManager.RefreshLibrary();
        LoadCategories();
    }
    
    private void OnSearchChanged(string searchText)
    {
        LoadTokens();
    }
    
    private void ClearContainer(Transform container, GameObject template)
    {
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
            if (child != template)
                Destroy(child);
        }
    }
    
    /// <summary>
    /// Find the local player's camera for multiplayer support
    /// </summary>
    private void FindLocalPlayerCamera()
    {
        Camera[] cameras = FindObjectsOfType<Camera>();
        
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

    private bool IsLocalPlayerCamera(Camera cam)
    {
        if (cam == null) return false;
        var networkPlayer = cam.GetComponentInParent<Nexus.Networking.NetworkPlayer>();
        return networkPlayer != null && networkPlayer.isLocalPlayer;
    }

    private Nexus.Networking.NetworkPlayer FindLocalNetworkPlayer()
    {
        var players = FindObjectsOfType<Nexus.Networking.NetworkPlayer>();
        foreach (var p in players)
        {
            if (p.isLocalPlayer) return p;
        }
        return null;
    }
}