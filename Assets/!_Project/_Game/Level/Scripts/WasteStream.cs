using UnityEngine;

public enum WasteStream { Waste, Paper, Plastic }
public static class WasteStreams
{
    public static Color ColorOf(WasteStream stream) => stream==WasteStream.Paper ? new Color(.23f,.48f,.8f)
        : stream==WasteStream.Plastic ? new Color(.9f,.65f,.12f) : new Color(.26f,.3f,.29f);
    public static string Label(WasteStream stream) => stream.ToString().ToLowerInvariant();
}
