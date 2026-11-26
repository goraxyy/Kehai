using System;
using System.Collections;
using System.Collections.Generic;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

public sealed class RoomLightSwitch : HighlightInteractable
{
    static readonly List<RoomLightSwitch> all = new List<RoomLightSwitch>();
    public static IReadOnlyList<RoomLightSwitch> All => all;
    public static event Action<RoomLightSwitch, bool> Changed;
    public string Room { get; private set; }
    public bool IsOff { get; private set; }
    public bool Warning { get; private set; }
    readonly Dictionary<Light, bool> lights = new Dictionary<Light, bool>();
    public static Func<Vector3, string> RoomOf = PhysicalRoom;
    public static string PhysicalRoom(Vector3 p)
    {
        var map = Application.isPlaying ? StoreMap.Current : null;
        if (map != null)
        {
            p.y = 0f;
            int cell = map.CellAt(p, 6f);
            int room = cell >= 0 ? map.CellRoom[cell] : -1;
            if (room >= 0 && room < map.Rooms.Count) return map.Rooms[room];
        }
        return StoreMap.AreaAt(p);
    }
    public static bool Allows(Vector3 at) => AllowsRoom(RoomOf(at));

    // The same, for a room already worked out (her light sense asks for every cell she sweeps).
    public static bool AllowsRoom(string room)
    {
        foreach (var sw in all) if (sw != null && sw.Room == room && sw.IsOff) return false;
        return true;
    }
    public void Configure(string room, IEnumerable<Light> lamps)
    {
        Room = room;
        lights.Clear();
        foreach (Light light in lamps) if (light != null) lights[light] = light.enabled;
    }
    public bool HasWorkingLights
    {
        get { foreach (Light light in lights.Keys) if (light != null && light.enabled) return true; return false; }
    }
    public void SetOff(bool off)
    {
        if (IsOff == off) return;
        IsOff = off;
        Apply();
        TaskManager.NotifyWorldChanged();
        Changed?.Invoke(this, off);
    }
    void Apply()
    {
        if (LightControl.Instance != null) LightControl.Instance.ApplyCircuits();
        else foreach (var pair in lights) if (pair.Key != null) pair.Key.enabled = !IsOff && pair.Value && PowerSystem.PowerOn;
    }
    // Exactly two visible dips before the effect lands. Aborting restores the circuit state.
    public IEnumerator FlickerTwice()
    {
        Warning = true;
        try
        {
            for (int i = 0; i < 2; i++)
            {
                foreach (Light light in lights.Keys) if (light != null) light.enabled = false;
                yield return new WaitForSeconds(.15f);
                Apply();
                yield return new WaitForSeconds(.2f);
            }
        }
        finally { Warning = false; Apply(); }
    }
    public void Restore() { StopAllCoroutines(); Warning = false; SetOff(false); Apply(); }
    public override void Interact(PlayerInteract player) => SetOff(!IsOff);
    public override string GetPrompt() => (IsOff ? "Switch on " : "Switch off ") + Room + " lights";
    void OnEnable() => all.Add(this);
    void OnDisable() { Restore(); all.Remove(this); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); Changed = null; RoomOf = PhysicalRoom; }
}
