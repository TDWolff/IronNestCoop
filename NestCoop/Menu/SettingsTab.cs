using Il2CppInterop.Runtime;
using Il2CppKamgam.UGUIComponentsForSettings;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace NestCoop.Menu;

/// <summary>
/// A "Co-op" tab in the game's own Settings screen (both the main-menu clipboard and the Esc clipboard), with NestCoop
/// options in the game's own widgets.
///
/// From the UI dump + Kamgam proxies (Il2CppKamgam.UGUIComponentsForSettings / .SettingsGenerator):
///   …/Canvas/Settings menu/Settings
///     TabsCtn   [HorizontalLayoutGroup] → TabButtonUGUI (Game|Graphics|Audio|Controls): Text, Content, GroupID, SetActive()
///     ContentCtn → Content (Game|Graphics|Audio|Controls)/Scroll View/Viewport/Content → headline + Layout [VerticalLayoutGroup]
///   Widgets: ToggleUGUI (Value, Text), OptionsButtonUGUI (SetOptions, SelectedIndex, TextTf), StepperUGUI (Value,
///   MinValue, MaxValue, Text), TextfieldUGUI (Text, TextTf). A widget is tied to a real game setting by a separate
///   *UGUIResolver (SettingResolver) component — removed from our copies, so they can never change game settings.
/// Build: clone the Audio tab + panel under an inactive holder (no Awake), point the tab at our panel, empty the panel,
/// then fill it with clones of the first widget of each kind found anywhere in the settings. Values are synced by
/// polling while the tab is visible (no delegates into IL2CPP). Localisation components are stripped so our labels stay.
/// </summary>
internal static class SettingsTab
{
    static readonly string[] SettingsRoots =
    {
        "Main Camera/Options Menu Parent/Clipboard Main Menu/Canvas/Settings menu/Settings",
        "Barbet/ - Main Menu - /MainMenu Interactable objects/Clipboard Menu/Canvas/Settings menu/Settings",
    };

    sealed class Tab
    {
        public GameObject Panel;
        public TextfieldUGUI Name;
        public OptionsButtonUGUI Network, Style;
        public OptionsButtonUGUI MaxPlayers, Resync; // the game has no StepperUGUI in its settings: number pickers
        public ToggleUGUI Terminal, Debug;
    }

    static readonly List<Tab> tabs = new();
    static readonly HashSet<string> built = new();
    static float nextTry;
    static bool gaveUp;

    // ---------------- per frame ----------------

    public static void Update()
    {
        if (!gaveUp && built.Count < SettingsRoots.Length && Time.unscaledTime >= nextTry)
        {
            nextTry = Time.unscaledTime + 3f;
            foreach (var path in SettingsRoots)
            {
                if (built.Contains(path)) continue;
                var root = Find(path);
                if (root == null) continue;
                built.Add(path);
                try { var t = Build(root); if (t != null) tabs.Add(t); }
                catch (Exception e) { Mod.Log.Error($"SettingsTab: couldn't add the Co-op tab to {path}: {e}"); gaveUp = true; }
            }
        }
        foreach (var t in tabs)
            if (t.Panel != null && t.Panel.activeInHierarchy) Poll(t);
    }

    // ---------------- build ----------------

    static Tab Build(Transform settings)
    {
        var tabsCtn = settings.Find("TabsCtn");
        var contentCtn = settings.Find("ContentCtn");
        var tplTab = tabsCtn?.Find("TabButtonUGUI (Audio)");
        var tplPanel = contentCtn?.Find("Content (Audio)");
        if (tplTab == null || tplPanel == null) { Mod.Log.Warning($"SettingsTab: unexpected settings layout under {settings.name}"); return null; }

        // Widget templates: first of each kind anywhere in this settings screen (inactive ones included).
        var tplToggle = settings.GetComponentsInChildren<ToggleUGUI>(true).FirstOrDefault();
        var tplOptions = settings.GetComponentsInChildren<OptionsButtonUGUI>(true).FirstOrDefault();
        var tplText = settings.GetComponentsInChildren<TextfieldUGUI>(true).FirstOrDefault();
        Mod.Log.Msg($"SettingsTab: templates toggle={tplToggle != null} options={tplOptions != null} textfield={tplText != null}");

        var holder = new GameObject("NestCoop SettingsTab Holder");
        holder.SetActive(false);
        try
        {
            // Tab button
            var tabGo = Clone(tplTab.gameObject, holder.transform, "TabButtonUGUI (Co-op)");
            var tab = tabGo.GetComponent<TabButtonUGUI>();

            // Panel: clone Audio's, keep the first headline + first Layout, empty the Layout.
            var panel = Clone(tplPanel.gameObject, holder.transform, "Content (Co-op)");
            var content = panel.transform.Find("Scroll View/Viewport/Content");
            Transform headline = null, layout = null;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var c = content.GetChild(i);
                bool isLayout = c.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() != null;
                if (isLayout && layout == null) { layout = c; continue; }
                if (!isLayout && headline == null) { headline = c; continue; }
                UnityEngine.Object.DestroyImmediate(c.gameObject);
            }
            if (layout == null) { Mod.Log.Warning("SettingsTab: no Layout in the Audio panel"); return null; }
            for (int i = layout.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(layout.GetChild(i).gameObject);
            SetAnyText(headline, "NestCoop co-op");

            var t = new Tab { Panel = panel };
            if (tplText != null) t.Name = AddWidget<TextfieldUGUI>(tplText.gameObject, layout, "Your name (shown to other players)");
            if (tplOptions != null) t.Network = AddWidget<OptionsButtonUGUI>(tplOptions.gameObject, layout, "Network");
            if (tplOptions != null) t.MaxPlayers = AddWidget<OptionsButtonUGUI>(tplOptions.gameObject, layout, "Max players (host)");
            if (tplOptions != null) t.Resync = AddWidget<OptionsButtonUGUI>(tplOptions.gameObject, layout, "Resync guests (host)");
            if (tplToggle != null) t.Terminal = AddWidget<ToggleUGUI>(tplToggle.gameObject, layout, "Show the NestCoop Terminal");
            if (tplOptions != null) t.Style = AddWidget<OptionsButtonUGUI>(tplOptions.gameObject, layout, "Co-op menu style");
            if (tplToggle != null) t.Debug = AddWidget<ToggleUGUI>(tplToggle.gameObject, layout, "Debug logging");

            // Strip game settings logic + localisation from everything we cloned.
            Strip(tabGo); Strip(panel);

            // Point the tab at our panel before it goes live (its Awake may register with the tab group).
            tab.Content = panel;
            tab.GamepadContent = null;
            tab.Text = "Co-op";
            SetTabLabels(tab, "Co-op");

            // Into place: tab after the others, panel hidden until the tab is chosen.
            panel.transform.SetParent(contentCtn, false);
            panel.SetActive(false);
            tabGo.transform.SetParent(tabsCtn, false);
            tabGo.transform.SetAsLastSibling();

            Init(t);
            Mod.Log.Msg($"SettingsTab: added a Co-op tab to {Ids.NamePath(settings)}");
            return t;
        }
        finally { UnityEngine.Object.Destroy(holder); }
    }

    static GameObject Clone(GameObject src, Transform parent, string name)
    {
        var go = UnityEngine.Object.Instantiate(src, parent);
        go.name = name;
        return go;
    }

    static T AddWidget<T>(GameObject template, Transform layout, string label) where T : Component
    {
        var go = Clone(template, layout, $"NestCoop {label}");
        go.SetActive(true);
        var w = go.GetComponent<T>();
        SetLabel(w, label);
        return w;
    }

    static void SetLabel(Component w, string label)
    {
        switch (w)
        {
            case ToggleUGUI t: t.Text = label; break;
            case StepperUGUI s: s.Text = label; break;
            case OptionsButtonUGUI o: if (o.TextTf != null) o.TextTf.text = label; break;
            case TextfieldUGUI f: SetFirstLabelText(f.transform, label, except: f.InputTf != null ? f.InputTf.transform : null); break;
        }
    }

    /// <summary>The first TextMeshProUGUI that isn't part of the input box: the field's label.</summary>
    static void SetFirstLabelText(Transform root, string text, Transform except)
    {
        foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (except != null && tmp.transform.IsChildOf(except)) continue;
            tmp.text = text;
            return;
        }
    }

    static void SetAnyText(Transform root, string text)
    {
        if (root == null) return;
        var tmp = root.GetComponentsInChildren<TextMeshProUGUI>(true).FirstOrDefault();
        if (tmp != null) tmp.text = text;
    }

    static void SetTabLabels(TabButtonUGUI tab, string text)
    {
        if (tab.NormalTextTf != null) tab.NormalTextTf.text = text;
        if (tab.ActiveTextTf != null) tab.ActiveTextTf.text = text;
    }

    /// <summary>Remove setting resolvers (they'd bind our copies to real game settings) and localisation.</summary>
    static void Strip(GameObject go)
    {
        int n = 0;
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            var t = mb.GetIl2CppType();
            bool resolver = false;
            for (var bt = t; bt != null; bt = bt.BaseType)
                if (bt.Name == "SettingResolver") { resolver = true; break; }
            if (resolver || t.Name == "StaticLocalisedText" || t.Name.Contains("Localis"))
            {
                UnityEngine.Object.DestroyImmediate(mb);
                n++;
            }
        }
        if (n > 0) Mod.Log.Msg($"SettingsTab: removed {n} resolver/localisation components from {go.name}");
    }

    // ---------------- values ----------------

    static readonly string[] NetworkOptions = { "Steam (friends)", "Localhost (testing)" };
    static readonly string[] StyleOptions = { "Game clipboard", "Simple overlay" };

    static Il2CppSystem.Collections.Generic.IList<string> Il2CppList(string[] items)
    {
        var l = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var s in items) l.Add(s);
        return l.Cast<Il2CppSystem.Collections.Generic.IList<string>>();
    }

    static readonly List<int> MaxPlayersValues = new() { 2, 3, 4, 5, 6, 7, 8 };
    static readonly List<int> ResyncValues = new() { 0, 1, 2, 5, 10, 15, 30 };

    /// <summary>A number picker: the list of values (plus the current one if it was set to something else in the cfg).</summary>
    static void InitNumber(OptionsButtonUGUI o, List<int> values, int current, Func<int, string> label)
    {
        if (!values.Contains(current)) { values.Add(current); values.Sort(); }
        o.SetOptions(Il2CppList(values.Select(label).ToArray()));
        o.SelectedIndex = values.IndexOf(current);
    }

    /// <summary>The picked value if it differs from the setting, else null.</summary>
    static int? PickedNumber(OptionsButtonUGUI o, List<int> values, int current)
    {
        int i = o.SelectedIndex;
        if (i < 0 || i >= values.Count || values[i] == current) return null;
        return values[i];
    }

    static void Init(Tab t)
    {
        if (t.Name != null) t.Name.Text = Mod.PlayerName.Value;
        if (t.Network != null) { t.Network.SetOptions(Il2CppList(NetworkOptions)); t.Network.SelectedIndex = Net.UseSteam ? 0 : 1; }
        if (t.Style != null) { t.Style.SetOptions(Il2CppList(StyleOptions)); t.Style.SelectedIndex = IsGameStyle ? 0 : 1; }
        if (t.MaxPlayers != null) InitNumber(t.MaxPlayers, MaxPlayersValues, Mod.MaxPlayers.Value, v => v.ToString());
        if (t.Resync != null) InitNumber(t.Resync, ResyncValues, Mod.ResyncMinutes.Value, v => v == 0 ? "Off" : $"Every {v} min");
        if (t.Terminal != null) t.Terminal.Value = Mod.TerminalEnabled.Value;
        if (t.Debug != null) t.Debug.Value = Mod.DebugLog.Value;
    }

    static bool IsGameStyle => string.Equals(Mod.MenuStyle.Value, "Game", StringComparison.OrdinalIgnoreCase);

    /// <summary>While the tab is visible: copy widget values into the settings (and back if they changed elsewhere).</summary>
    static void Poll(Tab t)
    {
        bool changed = false;
        if (t.Name != null)
        {
            string name = (t.Name.Text ?? "").Trim();
            if (name != Mod.PlayerName.Value) { Mod.PlayerName.Value = name; changed = true; }
        }
        if (t.Network != null)
        {
            bool wantSteam = t.Network.SelectedIndex == 0;
            if (wantSteam != Net.UseSteam)
            {
                if (Net.Active) { t.Network.SelectedIndex = Net.UseSteam ? 0 : 1; Overlay.Toast("Leave the co-op session to change the network"); }
                else { Mod.TransportName.Value = wantSteam ? "Steam" : "LiteNetLib"; changed = true; }
            }
        }
        if (t.Style != null && (t.Style.SelectedIndex == 0) != IsGameStyle) { Mod.MenuStyle.Value = t.Style.SelectedIndex == 0 ? "Game" : "Imgui"; changed = true; }
        if (t.MaxPlayers != null && PickedNumber(t.MaxPlayers, MaxPlayersValues, Mod.MaxPlayers.Value) is int mp) { Mod.MaxPlayers.Value = mp; changed = true; }
        if (t.Resync != null && PickedNumber(t.Resync, ResyncValues, Mod.ResyncMinutes.Value) is int rs) { Mod.ResyncMinutes.Value = rs; changed = true; }
        if (t.Terminal != null && t.Terminal.Value != Mod.TerminalEnabled.Value) { Mod.TerminalEnabled.Value = t.Terminal.Value; NestCoop.Terminal.ApplyEnabled(); changed = true; }
        if (t.Debug != null && t.Debug.Value != Mod.DebugLog.Value) { Mod.DebugLog.Value = t.Debug.Value; changed = true; }
        if (changed) MelonPreferences.Save();
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
