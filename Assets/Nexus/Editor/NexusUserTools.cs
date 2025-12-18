#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;

public static class NexusUserTools
{
    private const string UserRoot = "Assets/NexusUser";

    [MenuItem("Tools/Nexus/User Folder/Create Structure", false, 0)]
    public static void CreateStructure()
    {
        EnsureFolder("Assets", "NexusUser");
        EnsureFolder(UserRoot, "Resources");
        EnsureFolder(UserRoot + "/Resources", "Tokens");
        EnsureFolder(UserRoot, "Scenes");
        EnsureFolder(UserRoot + "/Scenes", "Subscenes");
        EnsureFolder(UserRoot, "Sprites");
        EnsureFolder(UserRoot, "Prefabs");
        AssetDatabase.Refresh();
        Debug.Log("NexusUser structure ready at Assets/NexusUser");
        // Seed demo content by default
        CreateDemoContent();
        // Ensure all user subscenes are registered in build and config
        EnsureAllUserSubscenesInConfigAndBuild();
    }

    private static void AssignThumbnailToConfig(string sceneName, string spriteAssetPath)
    {
        string configPath = UserRoot + "/Resources/SceneConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<SceneConfig>(configPath);
        if (config == null) return;
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spriteAssetPath);
        if (sprite == null) return;
        for (int i = 0; i < config.scenes.Count; i++)
        {
            var entry = config.scenes[i];
            if (entry != null && entry.sceneName == sceneName)
            {
                entry.thumbnail = sprite;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                break;
            }
        }
    }

    // Internal: seed default demo content (no menu)
    private static void CreateDemoContent()
    {
        // Try to copy user-provided demo assets first
        bool copiedUserDemo = false;
        string srcToken2 = "Assets/Nexus/Resources/Tokens/Personagem/2SidedToken.prefab";
        string destToken2 = UserRoot + "/Resources/Tokens/Personagem/2SidedToken.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(srcToken2) != null)
        {
            CreateFoldersForPath(Path.GetDirectoryName(destToken2).Replace("\\", "/"));
            if (!File.Exists(destToken2))
            {
                if (AssetDatabase.CopyAsset(srcToken2, destToken2)) copiedUserDemo = true;
            }
            else
            {
                copiedUserDemo = true;
            }
        }

        string srcToken4 = "Assets/Nexus/Resources/Tokens/Personagem/4SidedToken.prefab";
        string destToken4 = UserRoot + "/Resources/Tokens/Personagem/4SidedToken.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(srcToken4) != null)
        {
            CreateFoldersForPath(Path.GetDirectoryName(destToken4).Replace("\\", "/"));
            if (!File.Exists(destToken4))
            {
                if (AssetDatabase.CopyAsset(srcToken4, destToken4)) copiedUserDemo = true;
            }
            else
            {
                copiedUserDemo = true;
            }
        }

        string srcScene = "Assets/Nexus/Resources/Scenes/SubScenes/Demo Playground.unity";
        string destScene = UserRoot + "/Scenes/Subscenes/Demo Playground.unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(srcScene) != null)
        {
            CreateFoldersForPath(Path.GetDirectoryName(destScene).Replace("\\", "/"));
            if (!File.Exists(destScene))
            {
                if (AssetDatabase.CopyAsset(srcScene, destScene))
                {
                    copiedUserDemo = true;
                }
            }
            else
            {
                copiedUserDemo = true;
            }
            // Register scene regardless of copy success using dest if exists, else source
            string pathToUse = File.Exists(destScene) ? destScene : srcScene;
            AddSceneToBuildSettings(pathToUse);
            EnsureSceneConfigHasDemo("Demo Playground");
            AssignUserSceneConfigToMainScene();
        }

        if (copiedUserDemo)
        {
            AssetDatabase.Refresh();
            Debug.Log("Demo content ensured from user assets: 2SidedToken and 4SidedToken prefabs, and Demo Playground scene");
            return;
        }
        Debug.LogWarning("User demo assets not found; no demo content was created. Expected 2SidedToken.prefab, 4SidedToken.prefab and Demo Playground.unity.");
    }

    private static void MoveSelectionTo(string destFolder)
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            string src = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(src)) continue;
            if (Directory.Exists(src))
            {
                string dst = destFolder + "/" + Path.GetFileName(src);
                string unique = AssetDatabase.GenerateUniqueAssetPath(dst);
                string err = AssetDatabase.MoveAsset(src, unique);
                if (!string.IsNullOrEmpty(err)) Debug.LogError(err);
            }
            else
            {
                string dst = destFolder + "/" + Path.GetFileName(src);
                string unique = AssetDatabase.GenerateUniqueAssetPath(dst);
                string err = AssetDatabase.MoveAsset(src, unique);
                if (!string.IsNullOrEmpty(err)) Debug.LogError(err);
            }
        }
        AssetDatabase.Refresh();
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    private static void CreateFoldersForPath(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static void CreateDemoSprite(string path, int width, int height)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color bg = new Color(0.9f, 0.9f, 0.95f, 1f);
        Color border = new Color(0.25f, 0.4f, 0.9f, 1f);
        Vector2 c = new Vector2(width * 0.5f, height * 0.5f);
        float r = Mathf.Min(width, height) * 0.45f;
        float borderW = Mathf.Max(2f, r * 0.08f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                if (d > r)
                {
                    tex.SetPixel(x, y, new Color(0, 0, 0, 0));
                }
                else if (d > r - borderW)
                {
                    tex.SetPixel(x, y, border);
                }
                else
                {
                    tex.SetPixel(x, y, bg);
                }
            }
        }
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, png);
        Object.DestroyImmediate(tex);
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == scenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    private static void EnsureSceneConfigHasDemo(string demoSceneName)
    {
        // Always use the user's SceneConfig under Resources for runtime auto-load
        string configPath = UserRoot + "/Resources/SceneConfig.asset";
        CreateFoldersForPath(Path.GetDirectoryName(configPath).Replace("\\", "/"));
        SceneConfig config = AssetDatabase.LoadAssetAtPath<SceneConfig>(configPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SceneConfig>();
            config.mainSceneName = "MainScene";
            AssetDatabase.CreateAsset(config, configPath);
        }
        if (config != null)
        {
            bool has = false;
            for (int i = 0; i < config.scenes.Count; i++)
            {
                if (config.scenes[i] != null && config.scenes[i].sceneName == demoSceneName) { has = true; break; }
            }
            if (!has)
            {
                config.scenes.Add(new SceneConfig.SceneEntry { sceneName = demoSceneName });
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }
    }

    private static void AssignUserSceneConfigToMainScene()
    {
        string configPath = UserRoot + "/Resources/SceneConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<SceneConfig>(configPath);
        if (config == null) return;
        string mainScenePath = "Assets/Nexus/Resources/Scenes/MainScene.unity";
        if (!File.Exists(mainScenePath)) return;
        var scene = EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);
        var mgr = Object.FindObjectOfType<global::SceneManager>();
        if (mgr != null)
        {
            mgr.config = config;
            EditorUtility.SetDirty(mgr);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    private static void EnsureSceneConfigHasScene(string sceneName)
    {
        string configPath = UserRoot + "/Resources/SceneConfig.asset";
        CreateFoldersForPath(Path.GetDirectoryName(configPath).Replace("\\", "/"));
        SceneConfig config = AssetDatabase.LoadAssetAtPath<SceneConfig>(configPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<SceneConfig>();
            config.mainSceneName = "MainScene";
            AssetDatabase.CreateAsset(config, configPath);
        }
        bool has = false;
        for (int i = 0; i < config.scenes.Count; i++)
        {
            if (config.scenes[i] != null && config.scenes[i].sceneName == sceneName) { has = true; break; }
        }
        if (!has)
        {
            config.scenes.Add(new SceneConfig.SceneEntry { sceneName = sceneName });
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }
    }

    private static void EnsureAllUserSubscenesInConfigAndBuild()
    {
        string folder = UserRoot + "/Scenes/Subscenes";
        if (!AssetDatabase.IsValidFolder(folder)) return;
        string[] guids = AssetDatabase.FindAssets("t:scene", new[] { folder });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity")) continue;
            AddSceneToBuildSettings(path);
            string name = Path.GetFileNameWithoutExtension(path);
            EnsureSceneConfigHasScene(name);
        }
        AssignUserSceneConfigToMainScene();
    }
}
#endif
