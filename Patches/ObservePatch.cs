using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace Observe.Patches;

[HarmonyPatch]
internal static class ObservePatch
{
    private static float _lastObserveTime = -999f;
    private static Coroutine? _revealRoutine;

    /// <summary>
    /// Seconds of cooldown left before Observe can be used again (0 = ready).
    /// Read by the plugin's HUD to draw the cooldown bar.
    /// </summary>
    internal static float CooldownRemaining()
    {
        float remaining = Observe.Cooldown.Value - (Time.time - _lastObserveTime);
        return Mathf.Max(remaining, 0f);
    }

    /// <summary>
    /// Postfix on PlayerController.Update — listens for the Observe key and, when pressed
    /// (and off cooldown), reveals every valuable within the configured radius.
    ///
    /// Client-side only: we call <c>ValuableDiscover.instance.New(...)</c> directly — the
    /// local half of the game's own Discover flow — instead of <c>ValuableObject.Discover</c>.
    /// That spawns the native discovery graphic on this client WITHOUT sending an RPC, so
    /// only the pressing player sees the reveal, and it works whether or not the item was
    /// already discovered (we bypass the <c>discovered</c> gate entirely).
    /// </summary>
    [HarmonyPatch(typeof(PlayerController), "Update")]
    [HarmonyPostfix]
    private static void PlayerController_Update_Postfix(PlayerController __instance)
    {
        try
        {
            // Only the local player drives input.
            if (__instance != PlayerController.instance)
                return;

            // Expire any temporary map markers whose time is up (cheap; usually a no-op).
            MapReveal.Tick();

            // Warm the discovery graphic once, lazily, after we're in a level.
            TryLazyPrewarm();

            if (!ObserveKeyPressed())
                return;

            // Only while actually in a level (not the menu / loading).
            if (!SemiFunc.RunIsLevel())
                return;

            float cooldown = Observe.Cooldown.Value;
            if (Time.time - _lastObserveTime < cooldown)
            {
                Observe.Logger.LogDebug(
                    $"Observe on cooldown ({cooldown - (Time.time - _lastObserveTime):F1}s remaining)");
                return;
            }

            _lastObserveTime = Time.time;
            if (_revealRoutine != null)
                Observe.Instance.StopCoroutine(_revealRoutine);
            _revealRoutine = Observe.Instance.StartCoroutine(RevealNearbyValuables(__instance.transform.position));
        }
        catch (Exception ex)
        {
            Observe.Logger.LogError($"Observe exception: {ex.Message}");
        }
    }

    // Reveal across several frames (a few per frame) instead of all at once. Spawning the
    // discovery graphic for many valuables in a single frame causes a one-off hitch on the
    // first use (prefab/audio/shader warm-up plus a burst of Instantiate calls); spreading
    // it over a handful of frames keeps the reveal feeling instant without the stutter.
    private const int RevealsPerFrame = 4;

    private static IEnumerator RevealNearbyValuables(Vector3 origin)
    {
        var discover = ValuableDiscover.instance;
        if (discover == null)
        {
            Observe.Logger.LogWarning("ValuableDiscover.instance is null — cannot reveal.");
            _revealRoutine = null;
            yield break;
        }

        float radius = Observe.Radius.Value;
        float sqrRadius = radius * radius;
        int revealed = 0, thisFrame = 0;

        // Map layer: also drop a temporary native map marker for each revealed valuable.
        bool showOnMap = Observe.ShowOnMap.Value;
        float mapDuration = Observe.MapMarkerDuration.Value;
        if (showOnMap)
            MapReveal.RefreshOnMapSet();

        // ValuableObject has a transform (via its GameObject) and a physGrabObject the
        // discovery graphic attaches to.
        var valuables = UnityEngine.Object.FindObjectsOfType<ValuableObject>();
        foreach (var valuable in valuables)
        {
            if (valuable == null || valuable.physGrabObject == null)
                continue;

            if ((valuable.transform.position - origin).sqrMagnitude > sqrRadius)
                continue;

            // Spawn the game's own local discovery graphic (no RPC). State.Discover is the
            // full "found it" pop with the value; the graphic manages its own lifetime, so
            // the reveal lasts exactly the game's default duration.
            discover.New(valuable.physGrabObject, ValuableDiscoverGraphic.State.Discover, null);

            // And mark it on the map (also local, also no RPC) for the configured duration.
            if (showOnMap)
                MapReveal.AddOrRefresh(valuable, mapDuration);

            revealed++;

            if (++thisFrame >= RevealsPerFrame)
            {
                thisFrame = 0;
                yield return null;
            }
        }

        // Observe.Logger.LogInfo($"Observe revealed {revealed} valuable(s) within {radius}m.");
        _revealRoutine = null;
    }

    /// <summary>
    /// True the frame the Observe key goes down. We check the raw main key (plus any
    /// configured modifiers) instead of <c>KeyboardShortcut.IsDown()</c>: IsDown() also
    /// requires that NO other key is held, so holding WASD to move would suppress the
    /// press entirely. We only care that the main key went down and the configured
    /// modifiers (if any) are held.
    /// </summary>
    private static bool ObserveKeyPressed()
    {
        var key = Observe.ObserveKey.Value;
        if (key.MainKey == KeyCode.None || !Input.GetKeyDown(key.MainKey))
            return false;

        foreach (var modifier in key.Modifiers)
            if (!Input.GetKey(modifier))
                return false;

        return true;
    }

    private static bool _prewarmStarted;

    /// <summary>Kick off a one-time, deferred warm of the discovery graphic, once per session.</summary>
    private static void TryLazyPrewarm()
    {
        if (_prewarmStarted || !SemiFunc.RunIsLevel())
            return;
        _prewarmStarted = true;
        Observe.Instance.StartCoroutine(PrewarmDelayed());
    }

    /// <summary>
    /// Lazily pre-load the discovery-graphic prefab so the first real reveal is smooth.
    /// Waits a couple of seconds (off the level-load spike), then instantiates the prefab
    /// once and tears it straight back down — deactivated before it can render or Update,
    /// so it is silent and invisible. ValuableDiscoverGraphic has no Awake/OnEnable and its
    /// Start never touches the (unset) target, so a target-less instance is safe.
    /// </summary>
    private static IEnumerator PrewarmDelayed()
    {
        yield return new WaitForSeconds(2f);

        var discover = ValuableDiscover.instance;
        if (discover == null || discover.graphicPrefab == null)
            yield break;

        try
        {
            var go = UnityEngine.Object.Instantiate(discover.graphicPrefab, discover.transform);
            go.SetActive(false);
            UnityEngine.Object.Destroy(go);
            Observe.Logger.LogDebug("Observe pre-warmed the discovery graphic.");
        }
        catch (Exception ex)
        {
            Observe.Logger.LogDebug($"Observe pre-warm skipped: {ex.Message}");
        }
    }
}
