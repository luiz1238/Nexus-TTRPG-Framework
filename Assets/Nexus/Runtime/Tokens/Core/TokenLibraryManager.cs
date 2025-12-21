using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Escaneia automaticamente prefabs em Resources/Tokens e organiza por subpastas
/// Você cria os prefabs manualmente e o sistema detecta automaticamente
/// </summary>
public class TokenLibraryManager : MonoBehaviour
{
    [Header("Library Settings")]
    [SerializeField] private string tokensResourcePath = "Tokens";
    
    [Header("Runtime Data")]
    [SerializeField] private List<TokenCategory> categories = new List<TokenCategory>();
    [SerializeField] private int totalTokens = 0;
    
    private Dictionary<string, TokenCategory> categoryDict = new Dictionary<string, TokenCategory>();
    
    private void Awake()
    {
        RefreshLibrary();
    }
    
    /// <summary>
    /// Recarrega toda a biblioteca escaneando Resources/Tokens
    /// </summary>
    public void RefreshLibrary()
    {
        categories.Clear();
        categoryDict.Clear();
        
        // Carrega TODOS os GameObjects da pasta Resources/Tokens
        GameObject[] allPrefabs = Resources.LoadAll<GameObject>(tokensResourcePath);
        
        if (allPrefabs.Length == 0)
        {
            Debug.LogWarning($"Nenhum prefab encontrado em Resources/{tokensResourcePath}/\nCrie seus prefabs manualmente nessa pasta!");
            return;
        }
        
        // Agrupa por categoria (subpasta)
        Dictionary<string, List<TokenData>> categorizedTokens = new Dictionary<string, List<TokenData>>();
        
        foreach (GameObject prefab in allPrefabs)
        {
            string path = GetPrefabPath(prefab);
            string category = ExtractCategoryFromPath(path);
            
            TokenData tokenData = new TokenData
            {
                prefab = prefab,
                tokenName = prefab.name,
                category = category,
                fullPath = path
            };
            
            if (!categorizedTokens.ContainsKey(category))
            {
                categorizedTokens[category] = new List<TokenData>();
            }
            
            categorizedTokens[category].Add(tokenData);
        }
        
        // Cria categorias ordenadas
        foreach (var kvp in categorizedTokens.OrderBy(x => x.Key))
        {
            TokenCategory cat = new TokenCategory
            {
                categoryName = kvp.Key,
                tokens = kvp.Value.OrderBy(t => t.tokenName).ToList()
            };
            
            categories.Add(cat);
            categoryDict[kvp.Key] = cat;
        }
        
        totalTokens = allPrefabs.Length;
        Debug.Log($"✓ Token Library: {totalTokens} tokens em {categories.Count} categorias");
        
        foreach (var cat in categories)
        {
            Debug.Log($"  - {cat.categoryName}: {cat.tokens.Count} tokens");
        }
    }
    
    private string GetPrefabPath(GameObject prefab)
    {
#if UNITY_EDITOR
        return AssetDatabase.GetAssetPath(prefab);
#else
        return prefab.name;
#endif
    }
    
    private string ExtractCategoryFromPath(string path)
    {
        // Path exemplo: "Assets/Resources/Tokens/Monsters/Undead/Skeleton.prefab"
        // Resultado: "Monsters/Undead"
        
        string[] parts = path.Split('/');
        
        int tokensIndex = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] == "Tokens")
            {
                tokensIndex = i;
                break;
            }
        }
        
        // Se tem subpasta depois de Tokens
        if (tokensIndex >= 0 && tokensIndex + 1 < parts.Length - 1)
        {
            List<string> categoryParts = new List<string>();
            for (int i = tokensIndex + 1; i < parts.Length - 1; i++)
            {
                categoryParts.Add(parts[i]);
            }
            return string.Join("/", categoryParts);
        }
        
        return "Uncategorized";
    }
    
    #region Public API
    
    public List<TokenCategory> GetAllCategories()
    {
        return new List<TokenCategory>(categories);
    }
    
    public List<TokenData> GetTokensInCategory(string categoryName)
    {
        if (categoryDict.TryGetValue(categoryName, out TokenCategory cat))
        {
            return cat.tokens;
        }
        return new List<TokenData>();
    }
    
    public TokenData FindToken(string tokenName)
    {
        foreach (var category in categories)
        {
            foreach (var token in category.tokens)
            {
                if (token.tokenName == tokenName)
                    return token;
            }
        }
        return null;
    }
    
    public GameObject SpawnToken(TokenData tokenData, Vector3 position, Quaternion rotation)
    {
        if (tokenData == null || tokenData.prefab == null)
            return null;
        
        GameObject instance = Instantiate(tokenData.prefab, position, rotation);
        instance.name = tokenData.tokenName;
        var ts = instance.GetComponent<TokenSetup>();
        // Ensure simple drag component exists (new logic)
        var dragger = instance.GetComponent<TokenDraggable>();
        if (dragger == null)
        {
            dragger = instance.AddComponent<TokenDraggable>();
        }
        
        return instance;
    }
    
    public GameObject SpawnToken(string tokenName, Vector3 position, Quaternion rotation)
    {
        TokenData data = FindToken(tokenName);
        return SpawnToken(data, position, rotation);
    }
    
    #endregion
}

#region Data Classes

[System.Serializable]
public class TokenCategory
{
    public string categoryName;
    public List<TokenData> tokens;
    public bool isExpanded = true;
}

[System.Serializable]
public class TokenData
{
    public GameObject prefab;
    public string tokenName;
    public string category;
    public string fullPath;
    
    public Sprite GetSprite()
    {
        if (prefab != null)
        {
            SpriteRenderer sr = prefab.GetComponentInChildren<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
                return sr.sprite;
        }
        return null;
    }
}

#endregion