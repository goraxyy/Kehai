using UnityEngine;

namespace Kehai.Karen
{
    // What an eye remembers after seeing you go into a hiding place (0.5.1, item 2).
    //
    // It holds only what was seen: the place, and where you were when last in view. Her
    // destination and the 3 m catch are both measured to that remembered point, never to where
    // you are now. Whether you are still in there is a yes/no that the eye's own occupancy
    // check supplies (SightSensor.CanCatchWitnessedHide); it can take a catch away, never start
    // one or move the point. Leaving, or moving to another hiding place, takes the catch away for
    // good, even if you slip back in unseen. She still goes and looks, so nothing she does before
    // the catch moment depends on whether you are still inside.
    //
    // This file is held to the "she never reads the player" scan (Karen040Files).
    public sealed class WitnessedHide
    {
        public const float CatchDistance = 3f;   // metres from her to the remembered point
        public const float Lifetime = 60f;       // seconds before the memory is dropped
        public const float EntryWindow = 0.5f;   // last seen at most this long before concealment
        public const float EntryReach = 0.6f;    // last seen this close to the place's interior
        public const float EntryTravel = 1.5f;   // and moved at most this far since

        public bool Active { get; private set; }
        public Vector3 Position { get; private set; }
        public HidingPlace Place { get; private set; }
        public int Entry { get; private set; }   // counts remembered entries, so a fresh one reads as new

        bool catchEligible;
        float expires;

        // Was concealment in `place` at `now`, at `hiddenAt`, a witnessed entry? Yes only if the
        // eye last saw you (at `lastSeen`, at time `lastSeenAt`) right beside that place, a moment
        // before, and you've moved little since. The last view can be a little earlier than the
        // concealment: under a rack the board breaks the eye line before 80% of you is under it.
        // A loss behind a wall a metre away isn't an entry.
        public static bool Witnessed(HidingPlace place, Vector3 lastSeen, float lastSeenAt, Vector3 hiddenAt, float now, float window = EntryWindow) =>
            place != null && now - lastSeenAt <= window &&
            place.DistanceTo(lastSeen) <= EntryReach &&
            Vector3.Distance(lastSeen, hiddenAt) <= EntryTravel;

        public void Remember(HidingPlace place, Vector3 observedPosition, float now)
        {
            Place = place;
            Position = observedPosition;
            expires = now + Lifetime;
            Active = catchEligible = true;
            Entry++;
        }

        // The eye's occupancy check, each tick: `occupied` is the hiding place you are in now
        // (null if none), `concealed` whether it hides you.
        public void UpdateOccupancy(HidingPlace occupied, bool concealed, float now)
        {
            if (!Active) return;
            if (Place == null || !Place.Usable || now >= expires) { Clear(); return; }
            if (occupied != Place || !concealed) catchEligible = false;
        }

        // From her position (`observer`) to the remembered point, not to you.
        public bool CanCatch(Vector3 observer) =>
            Active && catchEligible && Vector3.Distance(observer, Position) <= CatchDistance;

        public void Clear()
        {
            Active = catchEligible = false;
            Place = null;
        }
    }
}
