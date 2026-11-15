using System;
using System.Collections.Generic;

public enum GuideStep { Arrival, Lobby, Door, ClockIn, TaskList, Waiting, Crate, Stock, OneDown,
    Mop, Spill, MopRack, Till, Directions, Bin, Skip, Coffee, Power, Closing, ClockOut, Done, Finished }
public enum GuideSignal { EnteredStore, SawStaffDoor, OpenedDoor, EnteredStaffRoom, ClockedIn,
    TaskListOpened, NeedsStock, CrateHeld, Stocked, NeedsMop, MopHeld, Cleaned, MopHome,
    TillWaiting, Served, QuestionAsked, Answered, BinFull, BagHeld, Disposed, LowEnergy,
    DrankCoffee, PowerLost, PowerRestored, ClosingTime, ClosingSaid, ClockedOut, ReportOpened }

// No Unity dependency: one active line, one reminder, and event facts retained if work happens early.
public sealed class FirstShiftGuideState
{
    readonly HashSet<GuideSignal> facts = new HashSet<GuideSignal>();
    readonly HashSet<GuideStep> jobs = new HashSet<GuideStep>();
    public GuideStep Step { get; private set; } = GuideStep.Arrival;
    public float Age { get; private set; }
    public bool Skipped { get; private set; }
    public bool Running => !Skipped && Step != GuideStep.Finished;
    public bool ThoughtVisible => Running && (Age < 4f || Age >= 30f && Age < 34f);
    public void Observe(GuideSignal signal) => facts.Add(signal);
    public void Skip() { Skipped = true; Step = GuideStep.Finished; }
    bool Has(GuideSignal signal) => facts.Contains(signal);
    void Go(GuideStep step) { Step = step; Age = 0f; }
    public void Tick(float seconds)
    {
        if (!Running) return;
        Age += Math.Max(0f, seconds);
        if (Has(GuideSignal.ClockedOut) && Step < GuideStep.Done) { Go(GuideStep.Done); return; }
        // Closing supersedes unfinished lessons; no optional job may block clock-out guidance.
        if (Has(GuideSignal.ClosingTime) && Step < GuideStep.Closing) { Go(GuideStep.Closing); return; }
        if (Has(GuideSignal.ClockedIn) && Step < GuideStep.TaskList) { Go(GuideStep.TaskList); return; }
        switch (Step)
        {
            case GuideStep.Arrival: if (Has(GuideSignal.EnteredStore)) Go(GuideStep.Lobby); break;
            case GuideStep.Lobby: if (Has(GuideSignal.SawStaffDoor) || Has(GuideSignal.EnteredStaffRoom)) Go(GuideStep.Door); break;
            case GuideStep.Door: if (Has(GuideSignal.OpenedDoor) || Has(GuideSignal.EnteredStaffRoom)) Go(GuideStep.ClockIn); break;
            case GuideStep.ClockIn: if (Has(GuideSignal.ClockedIn)) Go(GuideStep.TaskList); break;
            case GuideStep.TaskList: if (Has(GuideSignal.TaskListOpened)) Go(GuideStep.Waiting); break;
            case GuideStep.Waiting:
                if (Offer(GuideSignal.NeedsStock, GuideStep.Crate)) break;
                if (Offer(GuideSignal.NeedsMop, GuideStep.Mop)) break;
                if (Offer(GuideSignal.TillWaiting, GuideStep.Till)) break;
                if (Offer(GuideSignal.QuestionAsked, GuideStep.Directions)) break;
                if (Offer(GuideSignal.BinFull, GuideStep.Bin)) break;
                if (Offer(GuideSignal.LowEnergy, GuideStep.Coffee)) break;
                Offer(GuideSignal.PowerLost, GuideStep.Power); break;
            case GuideStep.Crate: if (Has(GuideSignal.CrateHeld) || Has(GuideSignal.Stocked)) Go(GuideStep.Stock); break;
            case GuideStep.Stock: if (Has(GuideSignal.Stocked)) Go(GuideStep.OneDown); break;
            case GuideStep.OneDown: if (Age >= 4f) Go(GuideStep.Waiting); break;
            case GuideStep.Mop: if (Has(GuideSignal.MopHeld) || Has(GuideSignal.Cleaned)) Go(GuideStep.Spill); break;
            case GuideStep.Spill: if (Has(GuideSignal.Cleaned)) Go(GuideStep.MopRack); break;
            case GuideStep.MopRack: if (Has(GuideSignal.MopHome) || Age >= 60f) Go(GuideStep.Waiting); break;
            case GuideStep.Till: if (Has(GuideSignal.Served)) Go(GuideStep.Waiting); break;
            case GuideStep.Directions: if (Has(GuideSignal.Answered)) Go(GuideStep.Waiting); break;
            case GuideStep.Bin: if (Has(GuideSignal.BagHeld) || Has(GuideSignal.Disposed)) Go(GuideStep.Skip); break;
            case GuideStep.Skip: if (Has(GuideSignal.Disposed)) Go(GuideStep.Waiting); break;
            case GuideStep.Coffee: if (Has(GuideSignal.DrankCoffee)) Go(GuideStep.Waiting); break;
            case GuideStep.Power: if (Has(GuideSignal.PowerRestored)) Go(GuideStep.Waiting); break;
            case GuideStep.Closing: if (Has(GuideSignal.ClosingSaid)) Go(GuideStep.ClockOut); break;
            case GuideStep.ClockOut: if (Has(GuideSignal.ClockedOut)) Go(GuideStep.Done); break;
            case GuideStep.Done: if (Age >= 4f || Has(GuideSignal.ReportOpened)) Go(GuideStep.Finished); break;
        }
    }
    bool Offer(GuideSignal signal, GuideStep step)
    {
        if (!Has(signal) || jobs.Contains(step)) return false;
        jobs.Add(step); Go(step); return true;
    }
    public static bool Eligible(KehaiEdition edition, int shift, bool batch, bool bot, bool replay) =>
        edition != KehaiEdition.Training && shift == 1 && !batch && !bot && !replay;
    public static string Thought(GuideStep step, string shelf = null)
    {
        switch (step)
        {
            case GuideStep.Arrival: return "Okay. I need to clock in first.";
            case GuideStep.Lobby: return "The staff room's at the back, past the tills.";
            case GuideStep.ClockIn: return "There's the clock.";
            case GuideStep.TaskList: return "Right. Let's see what needs doing.";
            case GuideStep.Crate: return "Shelves first. That crate by the radio restocks a whole shelf.";
            case GuideStep.Stock: return string.IsNullOrEmpty(shelf) ? "This shelf's running low." : shelf + "'s running low.";
            case GuideStep.OneDown: return "One down.";
            case GuideStep.Mop: return "Something's spilled. The mop's in the WC.";
            case GuideStep.Spill: return "Now, where was it…";
            case GuideStep.MopRack: return "Better put it back where it lives.";
            case GuideStep.Till: return "Someone's waiting at the till.";
            case GuideStep.Directions: return "They're asking me something.";
            case GuideStep.Bin: return "That bin's full.";
            case GuideStep.Skip: return "It goes in the skip, out back.";
            case GuideStep.Coffee: return "I'm flagging. There's coffee in the staff room.";
            case GuideStep.Power: return "Power's gone. The breakers are out back.";
            case GuideStep.Closing: return "Closing time. I have to say so on the PA.";
            case GuideStep.ClockOut: return "Once the last customer's gone, I can clock out.";
            case GuideStep.Done: return "First night done.";
            default: return "";
        }
    }
    public static string Prompt(GuideStep step)
    {
        switch (step)
        {
            case GuideStep.Door: return "E to open doors";
            case GuideStep.ClockIn: return "E to clock in";
            case GuideStep.TaskList: return "C shows the task list";
            case GuideStep.Crate: case GuideStep.Mop: return "E to pick up";
            case GuideStep.Stock: return "E to stock the shelf";
            case GuideStep.Spill: return "Hold E to mop";
            case GuideStep.MopRack: return "E to hang it up";
            case GuideStep.Till: return "E to serve";
            case GuideStep.Directions: return "Up / Down to choose, E to answer";
            case GuideStep.Bin: return "E to bag it";
            case GuideStep.Skip: return "E to throw it in";
            case GuideStep.Coffee: return "E for coffee";
            case GuideStep.Power: return "Hold E to reset the breakers (20 s)";
            case GuideStep.Closing: return "E to announce closing";
            case GuideStep.ClockOut: return "E to clock out";
            default: return "";
        }
    }
}
