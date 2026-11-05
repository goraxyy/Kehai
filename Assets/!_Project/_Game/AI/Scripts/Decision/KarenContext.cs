using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // What she's trying to do right now: once her "goals", her actions since 0.2.0. Each is
    // scored twice a second and the best one wins (Actions.cs); tricks are the ways to do one.
    // No action shares a name with a trick. The order is the old goals', so the rung F
    // policy's observation keeps its layout.
    public enum ActionId { Roam, Check, Search, Flush, Disrupt, Corner, LieInWait, Loom, Hunt, BackOff, Lure }

    public static class ActionNames
    {
        // As people say it: "Lie in wait", not "LieInWait".
        public static string Name(this ActionId a)
        {
            switch (a)
            {
                case ActionId.LieInWait: return "Lie in wait";
                case ActionId.BackOff: return "Back off";
                default: return a.ToString();
            }
        }
    }

    // The handful of scalars the decision layer actually uses (Karen.md §6.2), refreshed at
    // the appraisal rate. Everything above the body reads this, not the raw grid.
    public struct Appraisal
    {
        public float Confidence;       // max region belief       → hunt vs search
        public float Entropy;          // over regions, nats      → flush vs sweep
        public float EntropyNorm;      // 0..1
        public float Staleness;        // seconds since strong evidence
        public float Containment;      // belief mass inside a closable pocket
        public float Panic;            // Director's estimate of the player
        public float Setpoint;
        public float Pressure;         // Director's setpoint error, PI-controlled
        public float Tension;          // budget for big tactics
        public float TaskLoad;         // share of shift tasks complete — near 1 = nearly free
        public float EnergyBelief;     // what she thinks your energy is
        public float Awareness;        // sight detection level right now
        public int PeakRegion;
        public Vector3 PeakPosition;
        public bool InRecovery;
        public bool Blinking;          // the blink channel says your eyes are shut
    }

    // Everything a goal, tactic or primitive may touch, in one place. Notice what is not
    // here: the player. Karen acts on what she believes.
    public sealed class KarenContext
    {
        public KarenBrain Brain;
        public KarenBody Body;
        public StoreMap Map;
        public BeliefGrid Belief;
        public KarenDirector Director;
        public KarenLedger Ledger;
        public KarenWorld World;
        public ThoughtLog Log;
        public KarenRng Rng;
        public KarenConfig Config;
        public TaskManager Tasks;
        public ShiftManager Shift;
        public ITacticPolicy Policy;   // rung G only (Policy/TacticPolicy.cs)
        public Appraisal A;

        public float Now => Time.time;
        public float ShiftTime => Brain != null ? Brain.ShiftTime : 0f;
        public int ShiftNumber => Shift != null ? Mathf.Max(1, Shift.ShiftNumber) : 1;
        public RungFeatures Features => Config.Features;

        public string Where(Vector3 p) => Map.Describe(p);
        public string RegionName(int region) => Map.RegionName(region);

        public void Think(string kind, string text) => Brain?.Note(kind, text);   // a test's context has no brain
    }
}
