using System;
using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

// A real freezer door in the cold room. Normal opening is quiet; Karen's prop starts the alarm.
public sealed class FreezerAlarm : MonoBehaviour
{
    static readonly List<FreezerAlarm> all = new List<FreezerAlarm>();
    public static IReadOnlyList<FreezerAlarm> All => all;
    public static event Action<FreezerAlarm, bool> Changed;
    public FurnitureDoor Door { get; private set; }
    public string Room { get; private set; }
    public bool Ringing { get; private set; }
    float nextBeep;
    public void Configure(FurnitureDoor door) { Door = door; Room = RoomLightSwitch.RoomOf(transform.position); }
    public bool StartAlarm()
    {
        if (Ringing || Door == null) return false;
        Door.SetOpen(true);
        Ringing = true;
        nextBeep = 0f;
        TaskManager.NotifyWorldChanged();
        Changed?.Invoke(this, true);
        return true;
    }
    public bool Close()
    {
        if (!Ringing) return false;
        if (Door != null) Door.SetOpen(false);
        Ringing = false;
        TaskManager.NotifyWorldChanged();
        Changed?.Invoke(this, false);
        return true;
    }
    public static bool Masks(Vector3 source)
    {
        string room = RoomLightSwitch.RoomOf(source);
        foreach (var alarm in all) if (alarm != null && alarm.Ringing && alarm.Room == room) return true;
        return false;
    }
    public static bool IsStep(NoiseKind kind) => kind == NoiseKind.Footstep || kind == NoiseKind.CrouchStep ||
        kind == NoiseKind.Sprint || kind == NoiseKind.KarenStep;
    public static bool Masks(Vector3 source, NoiseKind kind) => IsStep(kind) && Masks(source);
    void Update()
    {
        if (!Ringing || GamePause.Paused) return;
        if (Door == null || !Door.IsOpen) { Close(); return; }
        if (Time.time < nextBeep) return;
        nextBeep = Time.time + .8f;
        if (!Edition.Training) OneShotAudio.PlayAt(ProceduralAudio.FreezerAlarmBeep(), transform.position, .8f);
        NoiseBus.Emit(transform.position, .8f, NoiseKind.Environmental, NoiseAuthor.World);
    }
    void OnEnable() => all.Add(this);
    void OnDisable() { Close(); all.Remove(this); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); Changed = null; }
}
