using System.Collections.Generic;
using UnityEngine;

namespace Observe;

/// <summary>
/// Client-side, temporary map markers for Observe.
///
/// When a scan reveals valuables we also drop the game's own map icon for each one — but only
/// for valuables that aren't already on the map — and remove our own markers again after a
/// configurable duration. This is purely local: <c>Map.AddValuable</c> instantiates a marker
/// under the local <see cref="Map"/>, it never sets the valuable's <c>discovered</c> state and
/// never sends an RPC. So only the scanning player sees these, and the game's own markers (for
/// valuables you genuinely discovered) are never touched or removed.
///
/// Vanilla note: <c>MapValuable</c> markers live until their target is collected; there is no
/// built-in expiry. So for the markers WE add we track the spawned object and destroy it once
/// its timer runs out (see <see cref="Tick"/>).
/// </summary>
internal static class MapReveal
{
    private struct Marker
    {
        public GameObject Go;
        public float Expire;
    }

    // Valuable -> the marker WE spawned for it (and when it should disappear).
    private static readonly Dictionary<ValuableObject, Marker> _markers = new();

    // Snapshot, rebuilt once per scan, of every valuable that already has a map marker
    // (vanilla's discovered markers as well as our own still-alive ones).
    private static readonly HashSet<ValuableObject> _onMap = new();

    /// <summary>
    /// Snapshot which valuables currently have a map marker. Call once at the start of a scan,
    /// before the per-valuable <see cref="AddOrRefresh"/> calls, so we never stack a second
    /// marker on top of one the game (or a previous scan) already placed.
    /// </summary>
    internal static void RefreshOnMapSet()
    {
        _onMap.Clear();
        foreach (var mv in UnityEngine.Object.FindObjectsOfType<MapValuable>())
            if (mv != null && mv.target != null)
                _onMap.Add(mv.target);
    }

    /// <summary>
    /// Ensure <paramref name="valuable"/> shows on the map for <paramref name="duration"/> more
    /// seconds. If it's already one of ours, just extends the timer; if it's already on the map
    /// for any other reason, leaves it alone; otherwise adds the native marker and tracks it.
    /// </summary>
    internal static void AddOrRefresh(ValuableObject valuable, float duration)
    {
        var map = Map.Instance;
        if (map == null || valuable == null)
            return;

        float expire = Time.time + duration;

        // Already ours → just push back its expiry.
        if (_markers.TryGetValue(valuable, out var existing))
        {
            existing.Expire = expire;
            _markers[valuable] = existing;
            return;
        }

        // Already on the map for another reason (genuinely discovered) → don't double it up.
        if (_onMap.Contains(valuable))
            return;

        // Not on the map yet → add the game's own marker and capture it so we can remove it later.
        var parent = map.OverLayerParent;
        int before = parent != null ? parent.childCount : -1;
        map.AddValuable(valuable);

        var go = CaptureNewMarker(parent, before, valuable);
        if (go == null)
            return; // AddValuable was a no-op (shop / arena / lobby) — nothing to track.

        _markers[valuable] = new Marker { Go = go, Expire = expire };
        _onMap.Add(valuable);
    }

    /// <summary>
    /// Find the marker <c>Map.AddValuable</c> just instantiated. It parents the new marker under
    /// <c>OverLayerParent</c> as the last child, so we check that first and verify by target,
    /// falling back to a short scan of the newly added children.
    /// </summary>
    private static GameObject? CaptureNewMarker(Transform? parent, int childCountBefore, ValuableObject valuable)
    {
        if (parent == null || parent.childCount <= childCountBefore)
            return null; // nothing was added.

        var last = parent.GetChild(parent.childCount - 1).GetComponent<MapValuable>();
        if (last != null && last.target == valuable)
            return last.gameObject;

        for (int i = parent.childCount - 1; i >= 0 && i >= childCountBefore; i--)
        {
            var mv = parent.GetChild(i).GetComponent<MapValuable>();
            if (mv != null && mv.target == valuable)
                return mv.gameObject;
        }
        return last != null ? last.gameObject : null;
    }

    /// <summary>
    /// Per-frame upkeep: destroy our markers whose timer has elapsed (or whose target/marker is
    /// already gone), and drop everything if we've left the level. Cheap — the set is tiny and
    /// usually empty.
    /// </summary>
    internal static void Tick()
    {
        if (_markers.Count == 0)
            return;

        // Left the level / map torn down → our markers died with the scene.
        if (Map.Instance == null || !SemiFunc.RunIsLevel())
        {
            _markers.Clear();
            _onMap.Clear();
            return;
        }

        float now = Time.time;
        List<ValuableObject>? remove = null;
        foreach (var kv in _markers)
        {
            if (kv.Value.Go == null || kv.Key == null || now >= kv.Value.Expire)
            {
                // Key is never a true-null reference (Unity's == null is the destroyed overload).
                (remove ??= new()).Add(kv.Key!);
                if (kv.Value.Go != null)
                    UnityEngine.Object.Destroy(kv.Value.Go);
            }
        }

        if (remove == null)
            return;
        foreach (var key in remove)
        {
            _markers.Remove(key);
            _onMap.Remove(key);
        }
    }

    /// <summary>Destroy every marker we own. Used when unloading / disabling the mod.</summary>
    internal static void ClearAll()
    {
        foreach (var kv in _markers)
            if (kv.Value.Go != null)
                UnityEngine.Object.Destroy(kv.Value.Go);
        _markers.Clear();
        _onMap.Clear();
    }
}
