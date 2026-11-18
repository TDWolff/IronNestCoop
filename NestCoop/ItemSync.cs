using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppNewtonsoft.Json;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// 0.9.9: carried items — punchcards, map tokens, fire-mission cards and every other DraggableItem.
///
/// From the dump: DraggableItem (CurrentLocation Deck/Surface/Slot, IsBeingDragged, surfaceRef, SlotRef, deckRef,
/// EndDragInternal, MoveToSlot(ItemSlot), MoveToDeck(DraggableItemDeckArea)); MissionSaveManager
/// CaptureDraggableSurfaceState(item) → DraggableSurfaceSaveState (surface tag, local + world pose, scale, active,
/// location) and RestoreDraggableSurfaceState(item, state, DragSurface[]) — the game's own checkpoint code for one item;
/// MissionSaveIdentity.Id is the item's stable id where it has one (map tokens, cards).
///
///  * Whoever finishes moving an item (drag released, slotted, sent to a deck) sends its resting state ~0.35 s later
///    (after the game's slide animations): item id, location, slot/deck id, and the captured state as JSON
///    (ProgressionSaveUtility.JsonSettings). The host relays to the other guests; the last move wins.
///  * Receivers place it with the game's code: MoveToSlot / MoveToDeck, else RestoreDraggableSurfaceState.
///  * An item on a player's own clipboard (a DragSurface under that player's camera) can't be shown on everyone
///    else's clipboard, so it's sent as "carried": others hide it (renderers + colliders off) until it's put down.
///  * An item you're dragging yourself ignores remote moves until you let go (then yours is sent).
/// Debug (DebugLog on): ItemReplayKey re-applies the state BEFORE your last move of the last item you moved, through
/// the receive path, so it can be tested without a second player: move an item, move it again, press the key → it
/// jumps back to the first spot. Records (RecordItem) are skipped: the record player isn't synced on purpose.
/// </summary>
internal static class ItemSync
{
    const byte LocDeck = 0, LocSurface = 1, LocSlot = 2, LocCarried = 3;

    static readonly Dictionary<IntPtr, (DraggableItem item, float due)> pending = new();
    static readonly Dictionary<string, DraggableItem> byId = new();
    static readonly HashSet<IntPtr> hidden = new();
    static float nextRescan;
    // Debug replay: per item, the last two states we sent ("one move back"), and which item moved last.
    static readonly Dictionary<string, (byte[] previous, byte[] latest)> history = new();
    static string lastItem;

    // ---------------- send ----------------

    /// <summary>Patches: this item was moved by the local player — send it once it has settled.</summary>
    public static void Moved(DraggableItem item, string how)
    {
        // Outside a session only for the debug replay (DebugLog on): recorded, not sent.
        if (item == null || Net.ApplyingRemote || (!Net.Active && !Mod.DebugLog.Value)) return;
        if (item.GetComponent<RecordItem>() != null) return; // records / the record player stay local (on purpose)
        PatchLog.Hit($"DraggableItem.{how}", item.name);
        pending[item.Pointer] = (item, Time.unscaledTime + 0.35f);
    }

    public static void Update()
    {
        if (pending.Count == 0) return;
        List<IntPtr> done = null;
        foreach (var (ptr, (item, due)) in pending)
        {
            if (Time.unscaledTime < due) continue;
            (done ??= new()).Add(ptr);
            if (item == null || item.IsBeingDragged) continue; // still held: EndDrag will queue it again
            try { Send(item); } catch (Exception e) { Mod.Log.Warning($"ItemSync: couldn't send {item.name}: {e.Message}"); }
        }
        if (done != null) foreach (var p in done) pending.Remove(p);
    }

    static void Send(DraggableItem item)
    {
        bool send = Net.CanSend;
        if (!send && !Mod.DebugLog.Value) return;
        string id = IdOf(item);
        byte loc; string container = "", json = "";
        if (OnOwnClipboard(item)) loc = LocCarried;
        else
        {
            loc = (byte)item.CurrentLocation;
            if (loc == LocSlot && item.SlotRef != null) container = Ids.Of(item.SlotRef);
            else if (loc == LocDeck && item.deckRef != null) container = Ids.Of(item.deckRef);
            var state = MissionSaveManager.CaptureDraggableSurfaceState(item);
            if (state != null) json = JsonConvert.SerializeObject(state, ProgressionSaveUtility.JsonSettings);
        }
        var data = Net.Write(MsgType.ItemState, w => { w.Write(id); w.Write(loc); w.Write(container); w.Write(json); });
        history[id] = (history.TryGetValue(id, out var h) ? h.latest : null, data);
        lastItem = id;
        if (send) Net.SendToSession(data, reliable: true);
        if (Mod.DebugLog.Value) Mod.Log.Msg($"ItemSync: {(send ? "sent" : "recorded (no session)")} {id} {LocName(loc)} {container}");
    }

    /// <summary>Stable id: the game's MissionSaveIdentity where there is one, else the hierarchy path.</summary>
    static string IdOf(DraggableItem item)
    {
        var ident = item.GetComponent<MissionSaveIdentity>();
        return ident != null && !string.IsNullOrEmpty(ident.Id) ? "msi:" + ident.Id : Ids.Of(item);
    }

    static bool OnOwnClipboard(DraggableItem item)
    {
        var surf = item.surfaceRef;
        var cam = Camera.main;
        return item.CurrentLocation == DraggableItem.ItemLocation.Surface && surf != null && cam != null && surf.transform.IsChildOf(cam.transform);
    }

    // ---------------- receive ----------------

    public static void Handle(int from, BinaryReader r, byte[] data)
    {
        string id = r.ReadString();
        byte loc = r.ReadByte();
        string container = r.ReadString();
        string json = r.ReadString();
        if (Net.Role == Role.Host) Net.Broadcast(data, reliable: true, exceptPeer: from); // relay
        Apply(id, loc, container, json);
    }

    static void Apply(string id, byte loc, string container, string json)
    {
        var item = Find(id);
        if (item == null) { Mod.Log.Warning($"ItemSync: item {id} not found here"); return; }
        if (item.IsBeingDragged) { Mod.Log.Msg($"ItemSync: ignored a remote move of {item.name} (you're holding it)"); return; }
        pending.Remove(item.Pointer);
        Net.ApplyingRemote = true;
        try
        {
            if (loc == LocCarried) { SetHidden(item, true); return; }
            SetHidden(item, false);
            if (loc == LocSlot && Ids.ResolveTransform(container)?.GetComponent<ItemSlot>() is ItemSlot slot)
            {
                if (item.SlotRef == null || item.SlotRef.Pointer != slot.Pointer) item.MoveToSlot(slot);
            }
            else if (loc == LocDeck && Ids.ResolveTransform(container)?.GetComponent<DraggableItemDeckArea>() is DraggableItemDeckArea deck)
            {
                if (item.deckRef == null || item.deckRef.Pointer != deck.Pointer || item.CurrentLocation != DraggableItem.ItemLocation.Deck) item.MoveToDeck(deck);
            }
            else if (!string.IsNullOrEmpty(json))
            {
                var obj = JsonConvert.DeserializeObject(json, Il2CppType.Of<MissionSaveManager.DraggableSurfaceSaveState>(), ProgressionSaveUtility.JsonSettings);
                var state = obj?.TryCast<MissionSaveManager.DraggableSurfaceSaveState>();
                if (state == null) { Mod.Log.Warning($"ItemSync: bad state for {id}"); return; }
                var all = DragSurface.AllSurfaces;
                var surfaces = new DragSurface[all.Count];
                for (int i = 0; i < surfaces.Length; i++) surfaces[i] = all[i];
                RestoreState(item, state, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DragSurface>(surfaces));
            }
            PatchLog.Hit("ItemSync.Apply", $"{item.name} -> {LocName(loc)} {container}");
        }
        catch (Exception e) { Mod.Log.Error($"ItemSync: applying {id} failed: {e}"); }
        finally { Net.ApplyingRemote = false; }
    }

    static void RestoreState(DraggableItem item, MissionSaveManager.DraggableSurfaceSaveState state,
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DragSurface> surfaces)
        => MissionSaveManager.RestoreDraggableSurfaceState(item, state, surfaces);

    /// <summary>Every DraggableItem in the loaded scenes right now (cards are spawned at runtime, so no cache).</summary>
    static DraggableItem[] AllItems() =>
        UnityEngine.Object.FindObjectsByType<DraggableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    static DraggableItem Find(string id)
    {
        if (byId.TryGetValue(id, out var it) && it != null) return it;
        if (!id.StartsWith("msi:"))
        {
            var t = Ids.ResolveTransform(id);
            return t != null ? t.GetComponent<DraggableItem>() : null;
        }
        if (Time.unscaledTime < nextRescan) return null;
        nextRescan = Time.unscaledTime + 0.5f;
        byId.Clear();
        foreach (var d in AllItems())
        {
            var ident = d.GetComponent<MissionSaveIdentity>();
            if (ident != null && !string.IsNullOrEmpty(ident.Id)) byId["msi:" + ident.Id] = d;
        }
        return byId.TryGetValue(id, out it) ? it : null;
    }

    /// <summary>"Carried" by another player: hide it here without disabling it (the game's logic keeps running).</summary>
    static void SetHidden(DraggableItem item, bool hide)
    {
        bool isHidden = hidden.Contains(item.Pointer);
        if (hide == isHidden) return;
        foreach (var rend in item.GetComponentsInChildren<Renderer>(true)) rend.enabled = !hide;
        foreach (var col in item.GetComponentsInChildren<Collider>(true)) col.enabled = !hide;
        if (hide) hidden.Add(item.Pointer); else hidden.Remove(item.Pointer);
    }

    static string LocName(byte loc) => loc switch { LocDeck => "deck", LocSurface => "surface", LocSlot => "slot", LocCarried => "carried", _ => loc.ToString() };

    public static void Reset()
    {
        pending.Clear();
        byId.Clear();
        foreach (var d in AllItems())
            if (d != null && hidden.Contains(d.Pointer)) SetHidden(d, false);
        hidden.Clear();
    }

    // ---------------- debug ----------------

    /// <summary>ItemReplayKey: move the last item you moved back to where you had it before, through the receive path.</summary>
    public static void DebugReplay()
    {
        if (lastItem == null || !history.TryGetValue(lastItem, out var h) || h.previous == null)
        { Overlay.Toast("ItemSync: move the same item twice during a session first"); return; }
        history[lastItem] = (null, h.previous); // a second press needs two new moves
        using var r = new BinaryReader(new MemoryStream(h.previous));
        r.ReadByte();
        string id = r.ReadString(); byte loc = r.ReadByte(); string container = r.ReadString(); string json = r.ReadString();
        Mod.Log.Msg($"ItemSync: replaying the previous state of {id} ({LocName(loc)} {container})");
        Overlay.Toast($"Moving it back to where you had it before ({LocName(loc)})");
        Apply(id, loc, container, json);
    }
}

// ---------------- patches (first hits logged with DebugLog on) ----------------

[HarmonyPatch(typeof(DraggableItem), nameof(DraggableItem.EndDragInternal))]
static class Item_EndDrag { static void Postfix(DraggableItem __instance) => ItemSync.Moved(__instance, "EndDragInternal"); }

[HarmonyPatch(typeof(DraggableItem), nameof(DraggableItem.EndDragFromManager))]
static class Item_EndDragFromManager { static void Postfix(DraggableItem __instance) => ItemSync.Moved(__instance, "EndDragFromManager"); }

[HarmonyPatch(typeof(DraggableItem), nameof(DraggableItem.MoveToSlot), typeof(ItemSlot))]
static class Item_MoveToSlot { static void Postfix(DraggableItem __instance) => ItemSync.Moved(__instance, "MoveToSlot"); }

[HarmonyPatch(typeof(DraggableItem), nameof(DraggableItem.MoveToDeck), typeof(DraggableItemDeckArea))]
static class Item_MoveToDeck { static void Postfix(DraggableItem __instance) => ItemSync.Moved(__instance, "MoveToDeck"); }

[HarmonyPatch(typeof(DraggableItem), nameof(DraggableItem.HandlePickedUpToClipboard))]
static class Item_ToClipboard { static void Postfix(DraggableItem __instance) => ItemSync.Moved(__instance, "HandlePickedUpToClipboard"); }
