using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // Match the bot's completed action to the fixture the forced plan actually changed.
    // A number of verbs also succeed when the job is already gone. Only their repair
    // result counts. The sale and complaint additionally distinguish repair from expiry.
    public sealed class TrickCheckCounter
    {
        public readonly string Verb, Message, Answer;
        TrickCheckCounter(string verb, string message, string answer = null)
        {
            Verb = verb;
            Message = message;
            Answer = answer;
        }

        static readonly Dictionary<string, TrickCheckCounter> counters = new Dictionary<string, TrickCheckCounter>
        {
            ["shelf_sweep"] = new TrickCheckCounter("restock", "restocked"),
            ["spill"] = new TrickCheckCounter("mop", "mopped"),
            ["bin_tamper"] = new TrickCheckCounter("bag_trash", "bagged"),
            ["blackout"] = new TrickCheckCounter("flip_breaker", "breakers reset"),
            ["fog"] = new TrickCheckCounter("reset_fog", "fog reset", "fog_reset"),
            ["camera_bolt_on"] = new TrickCheckCounter("unplug_camera", "unplugged"),
            ["camera_break"] = new TrickCheckCounter("fix_cameras", "cameras fixed"),
            ["tool_theft"] = new TrickCheckCounter("find_tool", "returned", "mop_returned"),
            ["understudy"] = new TrickCheckCounter("send_kenji", "sent to the tills", "understudy_sent_away"),
            ["door_lock"] = new TrickCheckCounter("unblock_door", "door unblocked", "box_moved"),
            ["flash_sale"] = new TrickCheckCounter("call_off_sale", "sale ended", "flash_sale"),
            ["screen_loop"] = new TrickCheckCounter("reset_screen", "cameras fixed", "screen_loop"),
            ["room_lights"] = new TrickCheckCounter("restore_room_lights", "room lights restored", "room_lights"),
            ["freezer_alarm"] = new TrickCheckCounter("close_freezer", "freezer closed", "freezer_alarm"),
            ["complaint"] = new TrickCheckCounter("hear_complaint", "complaint heard", "complaint"),
            ["overtime"] = new TrickCheckCounter("restart_clock", "clock restarted", "clock_restarted"),
        };

        public static TrickCheckCounter For(string id) => counters.TryGetValue(id, out var counter) ? counter : null;

        public bool Matches(Dictionary<string, object> result, string target)
        {
            if (result == null || !Equals(result["ok"], true) || !Equals(result["verb"], Verb) ||
                !Equals(result["message"], Message) || string.IsNullOrEmpty(target)) return false;
            // All three switches operate the same breaker panel.
            return Verb == "flip_breaker" ? (result["target"] as string)?.StartsWith("breaker_") == true :
                Equals(result["target"], target);
        }

        public static string Target(Tactic t, EnvWorld world, Vector3 at)
        {
            switch (t.Id)
            {
                case "blackout": return BreakerPanel.Instance != null ? "breaker panel" : null;
                case "fog": return FogState.ResetBox != null ? "fog_box" : null;
                case "overtime": return "time_clock";
                case "camera_break": case "screen_loop": return "camera_box";
                case "flash_sale": return "sale_microphone";
                case "understudy": return "kenji";
                case "spill": return world.SpillId(((SpillTactic)t).Left);
                case "camera_bolt_on": return world.ChoreId(((CameraBoltOnTactic)t).LastCamera, "camera");
                case "shelf_sweep":
                    var bay = ((ShelfSweepTactic)t).Swept;
                    for (int i = 0; i < world.Map.Bays.Count; i++)
                        if (world.Map.Bays[i].Unit == bay) return EnvWorld.BayId(i);
                    return null;
                case "bin_tamper":
                    int bin = EnvWorld.Bins().IndexOf(((BinTamperTactic)t).Refilled);
                    return bin >= 0 ? EnvWorld.BinId(bin) : null;
                case "door_lock":
                    var door = HingeDoor.All.FirstOrDefault(d => d != null && d.Blocker != null &&
                        d.Blocker.IsBlocking && d.Blocker.Box.transform.position == at);
                    return world.ChoreId(door, "door");
                case "tool_theft":
                    var rack = ToolSnapPoint.TakenRacks.FirstOrDefault(r => r.tool != null && r.tool.transform.position == at);
                    return rack != null ? world.ChoreId(rack.tool, "tool") : null;
                case "room_lights":
                    return world.ChoreId(RoomLightSwitch.All.FirstOrDefault(s => s.IsOff && s.transform.position == at), "light_switch");
                case "freezer_alarm":
                    return world.ChoreId(FreezerAlarm.All.FirstOrDefault(a => a.Ringing && a.transform.position == at), "freezer");
                case "complaint":
                    // Several shoppers can stand at the same till. Only an unambiguous target can pass.
                    var complaints = CustomerComplaint.All.Where(c => c.Active && c.FixPosition == at).Take(2).ToArray();
                    return complaints.Length == 1 ? world.ChoreId(complaints[0], "complaint") : null;
                default: return null;
            }
        }
    }
}
