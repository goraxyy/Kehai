using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Eval
{
    // Drives the real player body from code (IDEAS.md §1 "Action space").
    //
    // Everything goes through the same systems a person uses: PlayerMotor moves the
    // CharacterController (so walls, stamina and footstep noise are identical), and
    // PlayerInteract performs the interaction (so a mop still takes three seconds of holding
    // and a full inventory is still full). The driver only decides where to walk and when
    // to "press" something — the agent's hands, not a cheat.
    [DisallowMultipleComponent]
    public sealed class AgentDriver : MonoBehaviour, IMotorInput
    {
        public float turnSpeed = 540f;      // degrees per second
        public float cornerRadius = 0.45f;

        // Skittish players keep glancing behind them; the jitter is what the Panic Index
        // reads as yaw jitter.
        [System.NonSerialized] public float lookJitter;
        [System.NonSerialized] public bool verbose;
        float nextTrace;
        int replans, progressReplans;
        Vector3 progressFrom;
        float replanAt = -1f;
        float sidestepUntil = -1f;
        Vector3 sidestep;
        int sidestepSign = 1;

        PlayerMotor motor;
        PlayerInteract hands;
        CarrySlot carry;
        NavMeshPath path;                  // built in Awake: Unity won't allocate one in a field initializer
        Vector3[] corners = new Vector3[0];
        int corner;
        Vector3 goal;
        float arrive = 1f;
        bool moving;
        Vector3 lookTarget;
        bool hasLookTarget;
        float stuckFor;
        Vector3 lastPosition;
        float jitterPhase;

        public bool Sprint { get; set; }
        public bool Crouch { get; set; }
        public bool Jump => false;
        public Vector3 MoveWorld { get; private set; }
        public float YawDelta { get; private set; }
        public float PitchDelta { get; private set; }

        public bool Moving => moving;
        public bool PathFailed { get; private set; }
        public Vector3 Goal => goal;
        public PlayerInteract Hands => hands;
        public CarrySlot Carry => carry;
        public PlayerMotor Motor => motor;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            hands = GetComponent<PlayerInteract>();
            carry = GetComponent<CarrySlot>();
            path = new NavMeshPath();
        }

        public void Engage()
        {
            if (motor != null) motor.externalInput = this;
            Cursor.lockState = CursorLockMode.None;
        }

        public void Release()
        {
            if (motor != null && ReferenceEquals(motor.externalInput, this)) motor.externalInput = null;
            Stop();
        }

        // ---- walking ------------------------------------------------------------------

        public bool GoTo(Vector3 target, float arriveWithin = 1f)
        {
            progressFrom = transform.position;
            progressReplans = replans;
            return Plan(target, arriveWithin);
        }

        bool Plan(Vector3 target, float arriveWithin)
        {
            PathFailed = false;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit from, 3f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(target, out NavMeshHit to, 3f, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) ||
                path.status == NavMeshPathStatus.PathInvalid)
            {
                PathFailed = true;
                moving = false;
                return false;
            }

            corners = path.corners;
            corner = corners.Length > 1 ? 1 : 0;
            goal = to.position;
            arrive = arriveWithin;
            moving = true;
            stuckFor = 0f;
            if (verbose)
            {
                float length = 0f;
                for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
                Debug.Log($"[driver] path {path.status} {corners.Length} corners, {length:0.0} m, from {Kehai.Store.StoreMap.Current.NameAt(transform.position)} to {Kehai.Store.StoreMap.Current.NameAt(goal)} (replan {replans})");
            }
            return true;
        }

        public void Stop()
        {
            moving = false;
            MoveWorld = Vector3.zero;
        }

        public bool Arrived => !moving && !PathFailed;

        public float DistanceTo(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        public void LookAt(Vector3 point)
        {
            lookTarget = point;
            hasLookTarget = true;
        }

        public void ClearLook() => hasLookTarget = false;

        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 facing = transform.forward;

            if (moving)
            {
                Vector3 here = transform.position;
                if (DistanceTo(goal) <= arrive)
                {
                    Stop();
                }
                else
                {
                    while (corner < corners.Length - 1 && DistanceTo(corners[corner]) < cornerRadius) corner++;
                    Vector3 d = corners[Mathf.Min(corner, corners.Length - 1)] - here;
                    d.y = 0f;
                    MoveWorld = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
                    facing = MoveWorld.sqrMagnitude > 0f ? MoveWorld : facing;

                    // Someone standing in the aisle: step round them.
                    if (Time.time < sidestepUntil) MoveWorld = (sidestep * 0.85f + MoveWorld * 0.35f).normalized;

                    // A closed door in the way: open it, as a person presses E on reaching it,
                    // then re-plan once it has swung (the open panel is carved out of the NavMesh).
                    HingeDoor door = HingeDoor.ClosedAhead(here, MoveWorld, 2.2f);
                    if (door != null && !door.Locked && door.OpenFor(here, Kehai.Karen.NoiseAuthor.Player))
                        replanAt = Time.time + 1.2f;
                    if (replanAt > 0f && Time.time > replanAt)
                    {
                        replanAt = -1f;
                        Plan(goal, arrive);
                    }

                    // Wedged on a corner or a customer: re-plan, then give up.
                    Vector3 moved = here - lastPosition;
                    moved.y = 0f;
                    // Held still by Karen's lecture: that's waiting, not being stuck.
                    bool held = motor != null && motor.movementLocked;
                    if (held) progressFrom = here;
                    stuckFor = !held && moved.magnitude < 0.3f * dt ? stuckFor + dt : 0f;
                    if (stuckFor > 1.5f)
                    {
                        stuckFor = 0f;
                        replans++;
                        Collider blocker = Blocker(here, d);
                        if (verbose) Debug.Log($"[driver] stuck at {here} ({Kehai.Store.StoreMap.Current.NameAt(here)}) heading for corner {corner}/{corners.Length - 1} at {corners[Mathf.Min(corner, corners.Length - 1)]}, blocked by {Describe(blocker)}");

                        // A person (a shopper, Karen) isn't on the NavMesh: walk round them.
                        bool person = blocker != null && (blocker.GetComponentInParent<NavMeshAgent>() != null || blocker.GetComponentInParent<CustomerNPC>() != null);
                        if (person)
                        {
                            // Round whichever side has more room, alternating when it's even.
                            Vector3 left = Vector3.Cross(Vector3.up, d.normalized);
                            float roomLeft = Room(here, left), roomRight = Room(here, -left);
                            sidestepSign = Mathf.Abs(roomLeft - roomRight) > 0.3f ? (roomLeft > roomRight ? 1 : -1) : -sidestepSign;
                            sidestep = left * sidestepSign;
                            sidestepUntil = Time.time + 1.1f;
                        }

                        // Wedged for good (the NavMesh says yes, the body says no): give up, so
                        // the action fails and the agent can choose something else.
                        if ((here - progressFrom).sqrMagnitude < 1f && replans - progressReplans >= (person ? 6 : 3))
                        {
                            Stop();
                            PathFailed = true;
                        }
                        else if (!person && !Plan(goal, arrive)) Stop();
                    }
                    if ((here - progressFrom).sqrMagnitude >= 1f)
                    {
                        progressFrom = here;
                        progressReplans = replans;
                    }
                    if (verbose && Time.time > nextTrace)
                    {
                        nextTrace = Time.time + 3f;
                        Debug.Log($"[driver] at {here} {Kehai.Store.StoreMap.Current.NameAt(here)} → corner {corner}/{corners.Length - 1}, {DistanceTo(goal):0.0} m to go, speed {(motor != null ? motor.PlanarSpeed : 0f):0.0}");
                    }
                }
                lastPosition = here;
            }
            else
            {
                MoveWorld = Vector3.zero;
            }

            if (hasLookTarget)
            {
                Vector3 l = lookTarget - transform.position;
                l.y = 0f;
                if (l.sqrMagnitude > 0.01f) facing = l.normalized;
            }

            float desired = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(transform.eulerAngles.y, desired);
            float step = Mathf.Clamp(delta, -turnSpeed * dt, turnSpeed * dt);

            if (lookJitter > 0f)
            {
                jitterPhase += dt;
                step += Mathf.Sin(jitterPhase * 7.3f) * lookJitter * dt * 60f;
            }
            YawDelta = step;

            // Keep the head level so the interaction ray looks where the body faces.
            float pitch = motor != null ? motor.Pitch : 0f;
            PitchDelta = Mathf.Clamp(pitch, -30f * dt * 60f, 30f * dt * 60f) * 0.2f;
        }

        // What the body is pushing against: a sphere cast from the feet and the chest.
        Collider Blocker(Vector3 from, Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return null;
            float feet = from.y - (motor != null ? 1f : 0f);
            foreach (float h in new[] { 0.5f, 1.2f })
            {
                RaycastHit[] hits = Physics.SphereCastAll(new Vector3(from.x, feet + h, from.z), 0.3f, direction.normalized, 1.2f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit hit in hits)
                    if (!hit.collider.transform.IsChildOf(transform)) return hit.collider;
            }
            return null;
        }

        static float Room(Vector3 from, Vector3 direction)
        {
            Vector3 origin = from + Vector3.up * 0.2f;
            return Physics.SphereCast(origin, 0.3f, direction, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 2f;
        }

        static string Describe(Collider c)
        {
            if (c == null) return "nothing hit";
            Transform t = c.transform;
            string path = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
            return $"{path} ({c.GetType().Name})";
        }

        // ---- hands ----------------------------------------------------------------------

        public void Tap(IInteractable target)
        {
            hands.forcedTarget = target;
            hands.TapInteract();
        }

        public void Hold(IInteractable target, bool on)
        {
            hands.forcedTarget = on ? target : null;
            hands.forcedHold = on;
        }

        public void ReleaseHands()
        {
            hands.forcedTarget = null;
            hands.forcedHold = false;
        }

        public void Drop(float throwSpeed = 0f) => hands.DropNow(throwSpeed);
    }
}
