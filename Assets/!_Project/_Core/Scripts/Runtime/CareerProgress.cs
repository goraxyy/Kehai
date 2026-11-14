using System;

// Pure career rules; successful progress is saved independently from attempted-shift statistics.
public static class CareerRules
{
    public const int ShiftCount = 4;
    public static bool Complete(int completed) => completed >= ShiftCount;
    public static int Upcoming(int completed) => Math.Min(ShiftCount, Math.Max(0, completed) + 1);
    public static bool CanStart(int completed) => completed >= 0 && !Complete(completed);
}

// The attempted shift and successful progress are distinct: dying on night four never wins.
public sealed class CareerProgress
{
    public int CompletedShifts { get; private set; }
    public int ShiftNumber { get; private set; }
    public bool IsActive { get; private set; }
    public bool Complete => CareerRules.Complete(CompletedShifts);
    public int UpcomingShift => IsActive ? ShiftNumber : CareerRules.Upcoming(CompletedShifts);
    public void Restore(int completed)
    {
        if (IsActive) return;
        CompletedShifts = System.Math.Max(0, completed);
        ShiftNumber = CompletedShifts;
    }
    public bool TryStart()
    {
        if (IsActive || Complete) return false;
        ShiftNumber = CompletedShifts + 1;
        IsActive = true;
        return true;
    }
    public bool End(bool completed)
    {
        if (!IsActive) return false;
        IsActive = false;
        if (completed) CompletedShifts = ShiftNumber;
        return true;
    }
}
