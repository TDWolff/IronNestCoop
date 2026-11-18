using Il2CppTMPro;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// World-space text in the game's own font: a clone of the Esc clipboard's 3D TextMeshPro text
/// ("…/Play Tutorial Button/OPTIONS MENU (TMP)", from the UI dump) with its localisation removed.
/// Used by the terminal label and the avatar name tags. TMP 3D text reads from its -Z side.
/// </summary>
internal static class Text3D
{
    const string TemplatePath = "Main Camera/Options Menu Parent/Clipboard Main Menu/Canvas/Main Menu Cover/Play Tutorial Button/OPTIONS MENU (TMP)";

    /// <summary>New text object under <paramref name="parent"/>, or null if the template isn't loaded.</summary>
    public static TextMeshPro Create(Transform parent, string text, float scale, Color color)
    {
        var src = Find(TemplatePath);
        if (src == null) return null;
        var holder = new GameObject("text holder"); holder.SetActive(false);
        var go = UnityEngine.Object.Instantiate(src.gameObject, holder.transform);
        foreach (var mb in go.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetIl2CppType().Name == "StaticLocalisedText") UnityEngine.Object.DestroyImmediate(mb);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(true);
        UnityEngine.Object.Destroy(holder);
        var tmp = go.GetComponent<TextMeshPro>();
        if (tmp == null) return null;
        tmp.richText = false; // player names are shown literally
        tmp.text = text;
        tmp.color = color;
        return tmp;
    }

    static Transform Find(string path)
    {
        var parts = path.Split('/');
        for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetSceneAt(s).GetRootGameObjects())
            {
                if (r.name != parts[0]) continue;
                var cur = r.transform;
                for (int i = 1; i < parts.Length && cur != null; i++) cur = cur.Find(parts[i]);
                if (cur != null) return cur;
            }
        return null;
    }
}
