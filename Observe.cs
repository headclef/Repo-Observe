using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Observe;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Observe : BaseUnityPlugin
{
    private const string PluginGuid = "headclef.Observe";
    private const string PluginName = "Observe";
    private const string PluginVersion = "1.1.0";

    internal static Observe Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    // ── Config ──
    internal static ConfigEntry<KeyboardShortcut> ObserveKey = null!;
    internal static ConfigEntry<float> Radius = null!;
    internal static ConfigEntry<float> Cooldown = null!;
    internal static ConfigEntry<bool> ShowCooldownBar = null!;
    internal static ConfigEntry<bool> ShowOnMap = null!;
    internal static ConfigEntry<float> MapMarkerDuration = null!;

    private GUIStyle? _hudStyle;

    private void Awake()
    {
        Instance = this;
        this.gameObject.transform.parent = null;
        this.gameObject.hideFlags = HideFlags.HideAndDontSave;

        BindConfiguration();
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }

    private void BindConfiguration()
    {
        const string section = "Observe";

        ObserveKey = Config.Bind(section, "Observe Key", new KeyboardShortcut(KeyCode.F),
            "Key to press to reveal nearby valuables.");

        Radius = Config.Bind(section, "Radius", 10f,
            new ConfigDescription(
                "Radius in meters around you within which valuables are revealed.",
                new AcceptableValueRange<float>(1f, 50f)));

        Cooldown = Config.Bind(section, "Cooldown", 10f,
            new ConfigDescription(
                "Cooldown in seconds between uses.",
                new AcceptableValueRange<float>(0f, 60f)));

        ShowCooldownBar = Config.Bind(section, "Show Cooldown Bar", true,
            "Show an on-screen bar and countdown while Observe is on cooldown.");

        ShowOnMap = Config.Bind(section, "Show On Map", true,
            "Also mark revealed valuables on the Map tool when you scan (in addition to the "
            + "through-wall world marker). Client-side only — just you see them.");

        MapMarkerDuration = Config.Bind(section, "Map Marker Duration", 10f,
            new ConfigDescription(
                "How long (seconds) revealed valuables stay marked on the map after a scan. "
                + "Independent of the cooldown. Valuables you genuinely discover stay on the "
                + "map as usual — this only governs the temporary markers Observe adds.",
                new AcceptableValueRange<float>(1f, 60f)));
    }

    /// <summary>
    /// Lightweight self-contained HUD: while Observe is on cooldown, draw a draining
    /// bar (length = remaining / total) with the remaining seconds, centered near the
    /// bottom of the screen. No dependency on the UI mod; hidden when ready or disabled.
    /// </summary>
    private void OnGUI()
    {
        if (!ShowCooldownBar.Value) return;
        if (!SemiFunc.RunIsLevel()) return;

        float remaining = Patches.ObservePatch.CooldownRemaining();
        if (remaining <= 0f) return;

        float total = Mathf.Max(Cooldown.Value, 0.0001f);
        float fill = Mathf.Clamp01(remaining / total);

        const float w = 280f, h = 22f;
        float x = (Screen.width - w) / 2f;
        float y = Screen.height - 200f;

        Color prev = GUI.color;

        // Outer frame
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x - 2f, y - 2f, w + 4f, h + 4f), Texture2D.whiteTexture);

        // Track
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

        // Remaining fill (drains toward ready)
        GUI.color = new Color(1f, 0.82f, 0.1f, 0.95f);
        GUI.DrawTexture(new Rect(x, y, w * fill, h), Texture2D.whiteTexture);

        // Label (dark shadow first so it stays readable over the yellow fill)
        _hudStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 14
        };
        string text = $"Observe  {remaining:0.0}s";
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(x + 1f, y + 1f, w, h), text, _hudStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y, w, h), text, _hudStyle);

        GUI.color = prev;
    }
}
