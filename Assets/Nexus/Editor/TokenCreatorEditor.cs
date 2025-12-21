#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Mirror;
using Nexus.Networking;

public static class TokenCreatorEditor
{
    [MenuItem("GameObject/Nexus/Create Token", false, 10)]
    public static void CreateToken()
    {
        GameObject root = new GameObject("Token");
        Undo.RegisterCreatedObjectUndo(root, "Create Token");

        var token = root.AddComponent<TokenSetup>();

        GameObject spriteGO = new GameObject("Sprite");
        Undo.RegisterCreatedObjectUndo(spriteGO, "Create Token Sprite");
        spriteGO.transform.SetParent(root.transform, false);
        spriteGO.transform.localPosition = Vector3.zero;
        spriteGO.AddComponent<SpriteRenderer>();

        var bc = root.GetComponent<BoxCollider>();
        if (bc == null) bc = root.AddComponent<BoxCollider>();
        bc.size = new Vector3(1f, 0.2f, 1f);
        bc.center = new Vector3(0f, 0.1f, 0f);

        var dragger = root.AddComponent<TokenDraggable>();
        root.AddComponent<NetworkIdentity>();
        root.AddComponent<NetworkedToken>();

        SceneView sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            Vector3 pos = sv.pivot;
            root.transform.position = pos;
        }
        else
        {
            root.transform.position = Vector3.zero;
        }

        Selection.activeGameObject = root;
    }
}
#endif

