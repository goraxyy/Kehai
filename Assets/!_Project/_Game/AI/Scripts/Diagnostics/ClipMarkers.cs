using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // One kind of moment worth a clip: how good a clip it tends to make (weight), how much of
    // the shift to show before and after it, who is in it, and tags for the writer.
    public sealed class ClipMarkerKind
    {
        public readonly string Id;
        public readonly int Weight;
        public readonly float PreRoll, PostRoll;
        public readonly string[] Subjects, Tags;
        public readonly bool AlwaysKept;

        public ClipMarkerKind(string id, int weight, float preRoll, float postRoll, string[] subjects, string[] tags, bool alwaysKept = false)
        {
            Id = id;
            Weight = weight;
            PreRoll = preRoll;
            PostRoll = postRoll;
            Subjects = subjects;
            Tags = tags;
            AlwaysKept = alwaysKept;
        }
    }

    // Everything that tunes the clip finder, in one table. Markers only observe the shift;
    // they never change what Karen or anyone else does.
    public static class ClipMarkers
    {
        public const float MergeGap = 8f;           // markers this close become one moment
        public const float MaxMoment = 45f;         // ...but a moment never runs longer than a clip can
        public const float MaxSpan = 20f;           // how far a long marker (a blackout) stretches its moment
        public const float ChaseBoost = 1.5f;       // a moment with a chase or a catch in it
        public const int MinScore = 5;              // quieter moments are dropped, unless always kept

        public const float NearMissMetres = 3f;
        public const float FoundBlindMetres = 2f;
        public const float FoundBlindConfidence = 0.5f;    // "she's fairly sure" and up
        public const float StuckMetres = 0.5f;
        public const float StuckSeconds = 6f;
        public const float UndoneWorkSeconds = 60f;
        public const float UndoneSpillMetres = 15f;
        public const float LoudMistakeSeconds = 3f;        // from the noise to "she heard it"
        public const float HeadingThereSeconds = 4f;       // from "she heard it" to her closing in
        public const float HeadingThereMetres = 1f;
        public const float TellToTrickSeconds = 10f;
        public const float EscapeLead = 12f;               // how much of a long chase an escape shows
        public const float PropTrickSeconds = 30f;         // from her choosing a trick to it happening

        public const float NearMissCooldown = 10f;
        public const float FoundBlindCooldown = 20f;
        public const float StuckCooldown = 20f;
        public const float BlinkCooldown = 3f;
        public const float LearnedCooldown = 20f;
        public const float PaCooldown = 15f;               // a PA countdown is one call, not six
        public const float LoudMistakeCooldown = 10f;
        public const float ClockRefusedCooldown = 10f;

        // The tricks that build something in the store or take someone over.
        public static readonly HashSet<string> PropTricks = new HashSet<string>
        {
            "crate_wall", "fog", "door_lock", "camera_bolt_on", "shelf_relocation", "mimicry"
        };

        public static readonly ClipMarkerKind[] All =
        {
            //   id                 weight  pre  post  subjects                   tags
            K("blink_move",         10,     4f,  3f,   S("karen", "player"),       T("blink")),
            K("catch",              10,     8f,  5f,   S("karen", "player"),       T("chase", "catch", "overtime")),
            K("near_miss",           9,     5f,  3f,   S("karen", "player"),       T("stealth", "close call")),
            K("escape",              8,     3f,  3f,   S("karen", "player"),       T("chase", "escape")),
            K("found_blind",         8,     5f,  3f,   S("karen", "player"),       T("her mind", "belief")),
            K("blackout",            8,     3f,  3f,   S("karen", "store"),        T("power", "trick")),
            K("possessed",           8,     2f,  6f,   S("karen", "customer"),     T("possession", "trick")),
            K("learned",             7,     3f,  3f,   S("karen"),                 T("learning", "her mind")),
            K("undone_work",         7,     4f,  4f,   S("karen", "player"),       T("work", "sabotage")),
            K("pa_call",             6,     2f,  6f,   S("karen"),                 T("pa", "voice")),
            K("prop_trick",          6,     2f,  6f,   S("karen"),                 T("trick", "prop")),
            K("clock_refused",       6,     3f,  4f,   S("karen", "player"),       T("overtime", "work")),
            K("loud_mistake",        6,     2f,  5f,   S("player", "karen"),       T("noise", "stealth")),
            K("tell_then_trick",     5,     2f,  4f,   S("karen"),                 T("tell", "trick", "fair play")),
            K("shift_review",        5,     4f,  0f,   S("player"),               T("review", "end of shift")),
            K("karen_stuck",          4,     2f,  2f,   S("karen"),                 T("bug", "funny")),
            K("customer_chaos",      3,     3f,  3f,   S("customer"),             T("customer", "funny")),
            K("manual_good",        10,    15f,  3f,   S("player"),               T("manual"), alwaysKept: true),
            K("manual_bug",          0,    15f,  3f,   S("player"),               T("manual", "bug"), alwaysKept: true),
        };

        static readonly Dictionary<string, ClipMarkerKind> byId = new Dictionary<string, ClipMarkerKind>();

        static ClipMarkers()
        {
            foreach (ClipMarkerKind k in All) byId[k.Id] = k;
        }

        public static ClipMarkerKind Get(string id) => byId.TryGetValue(id, out ClipMarkerKind k) ? k : null;

        static ClipMarkerKind K(string id, int weight, float pre, float post, string[] subjects, string[] tags, bool alwaysKept = false) =>
            new ClipMarkerKind(id, weight, pre, post, subjects, tags, alwaysKept);

        static string[] S(params string[] s) => s;
        static string[] T(params string[] t) => t;
    }

    // Something that happened, worth a clip. `End` is later than `T` for things that last
    // (a blackout, a warning and its trick, a chase that got away).
    public struct ClipMarker
    {
        public string Id;
        public float T;
        public float End;
        public Vector2 At;              // floor-plan position, when it happened somewhere
        public bool HasPlace;
        public string Text;
        public float Value;             // e.g. the blackout's seconds; NaN when there is none

        public ClipMarkerKind Kind => ClipMarkers.Get(Id);
    }

    // Markers close together, as one stretch of the shift to cut a clip from.
    public sealed class ClipMoment
    {
        public float Start, End, Score;
        public bool Chase, Kept;
        public readonly List<ClipMarker> Markers = new List<ClipMarker>();
        public readonly List<string> Subjects = new List<string>();
        public readonly List<string> Tags = new List<string>();
        public readonly List<string> CaptionSeed = new List<string>();
    }
}
