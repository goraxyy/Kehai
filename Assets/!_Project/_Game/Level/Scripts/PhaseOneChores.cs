using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

// Public work-state hooks for TaskManager, EnvWorld and scripted/trained bots. Nothing here
// reads Karen's position, plan, belief or senses. Targets are the same interactables as E.
public static class PhaseOneChores
{
    public readonly struct Chore
    {
        public readonly string Kind, Id, Where;
        public readonly Component Target;
        public readonly Vector3 Position;
        public readonly float HoldSeconds;
        public Chore(string kind, string id, string where, Component target, Vector3 position, float seconds)
        { Kind = kind; Id = id; Where = where; Target = target; Position = position; HoldSeconds = seconds; }
    }
    public static IEnumerable<Chore> Pending()
    {
        var mic = ClosingMic.Instance;
        if (FlashSaleState.Active && mic != null)
            yield return new Chore("call_off_sale", "pa_microphone", "PA microphone, staff room", mic, mic.transform.position, SaleRules.HoldSeconds);
        var cams = StoreCameras.Instance;
        if (cams != null && cams.Looped && cams.Box != null)
            yield return new Chore("reset_screen", "camera_box", "camera box, backstreet", cams.Box, cams.Box.transform.position, BackstreetFix.HoldSeconds);
        foreach (var sw in RoomLightSwitch.All)
            if (sw != null && sw.IsOff)
                yield return new Chore("restore_room_lights", "room_light_" + sw.GetEntityId(), sw.Room + " light switch", sw, sw.transform.position, 0f);
        foreach (var alarm in FreezerAlarm.All)
            if (alarm != null && alarm.Ringing && alarm.Door != null)
                yield return new Chore("close_freezer", "freezer_" + alarm.GetEntityId(), "freezer door, " + alarm.Room, alarm.Door, alarm.transform.position, 0f);
        foreach (var complaint in CustomerComplaint.All)
            if (complaint != null && complaint.Active)
                yield return new Chore("hear_complaint", "complaint_" + complaint.GetEntityId(), "angry shopper, till", complaint.GetComponent<CustomerNPC>(), complaint.FixPosition, CustomerComplaint.HoldSeconds);
    }
    public static bool Any
    {
        get { foreach (var chore in Pending()) return true; return false; }
    }
}
