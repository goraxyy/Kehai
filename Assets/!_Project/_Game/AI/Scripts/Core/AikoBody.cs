using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Aiko
{
    // Aiko's body: a NavMeshAgent with eyes, feet and hands (Aiko.md §2.1).
    //
    // What it has is as important as what it hasn't. It has no reference to the player at
    // all — that single missing reference is the difference between a stalker and an
    // investigator. It walks where the plan sends it, sees through its SightSensor, and
    // learns it has touched someone only by the contact itself.
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class AikoBody : MonoBehaviour
    {
        public enum Pace { Sneak, Walk, Hurry, Run }
        public enum Mood { Calm, Alert, Hunt, Kind }

        public NavMeshAgent Agent { get; private set; }
        public SightSensor Sight { get; private set; }
        public Transform Head { get; private set; }

        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        public bool Silent { get; set; }
        public Pace CurrentPace { get; private set; } = Pace.Walk;
        public Mood CurrentMood { get; private set; } = Mood.Calm;

        public event System.Action<Collider> Touched;

        AikoConfig config;
        Light eyeLight;
        Renderer eyeRenderer;
        AudioSource feet;
        Transform hand;
        Item carried;
        float stepTimer;
        float headYaw;
        Vector3 lookTarget;
        bool turning;
        float burstUntil;
        float burstSpeed;
        Vector3 burstFrom;
        float burstMax;

        public static AikoBody Build(Transform parent, AikoConfig config, Vector3 at)
        {
            var root = new GameObject("AIKO_Body");
            root.transform.SetParent(parent, false);
            root.transform.position = at;
            (Renderer eyeRenderer, Light gaze) = BuildLook(root.transform, config);

            var body = root.AddComponent<AikoBody>();
            body.config = config;
            body.Agent = root.GetComponent<NavMeshAgent>();
            body.Agent.radius = 0.4f;
            body.Agent.height = 2.3f;
            body.Agent.acceleration = 14f;
            body.Agent.angularSpeed = 360f;
            body.Agent.stoppingDistance = 0.2f;
            body.Agent.speed = config.walkSpeed;
            body.Agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

            body.Head = gaze.transform;
            body.eyeRenderer = eyeRenderer;
            body.eyeLight = gaze;
            body.Sight = gaze.gameObject.AddComponent<SightSensor>();
            body.Sight.range = config.sightRange;
            body.Sight.fieldOfView = config.sightFov;
            body.Sight.ignoreRoot = root.transform;

            // Touch: a trigger around her. The only way she ever "knows" where you are for
            // certain is to walk into you.
            var touch = root.AddComponent<CapsuleCollider>();
            touch.isTrigger = true;
            touch.radius = config.catchRadius;
            touch.height = 2.2f;
            touch.center = Vector3.up * 1.1f;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            body.feet = root.AddComponent<AudioSource>();
            body.feet.spatialBlend = 1f;
            body.feet.rolloffMode = AudioRolloffMode.Linear;
            body.feet.minDistance = 2f;
            body.feet.maxDistance = 26f;
            body.feet.dopplerLevel = 0f;
            body.feet.playOnAwake = false;

            body.hand = new GameObject("Hand").transform;
            body.hand.SetParent(root.transform, false);
            body.hand.localPosition = new Vector3(0.35f, 1.1f, 0.35f);

            body.SetMood(Mood.Calm);
            root.AddComponent<AikoFloorCone>();   // her gaze, painted on the floor
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 5f, NavMesh.AllAreas)) body.Agent.Warp(hit.position);
            return body;
        }

        // How she looks, and nothing else: the figure, the badge, the visor and the spot light
        // of her gaze, under `root`. The game gives it a body (Build); the replay dresses a
        // puppet in it.
        public static (Renderer eye, Light gaze) BuildLook(Transform root, AikoConfig config)
        {
            // A tall, narrow, charcoal figure: a store manager seen from the far end of an aisle.
            var charcoal = new Color(0.16f, 0.16f, 0.18f);
            GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Torso";
            torso.transform.SetParent(root, false);
            torso.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            torso.transform.localScale = new Vector3(0.62f, 1.05f, 0.45f);
            torso.GetComponent<Renderer>().sharedMaterial = AikoProps.Lit(charcoal, 0.55f);
            Destroy(torso.GetComponent<Collider>());

            GameObject headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headGo.name = "Head";
            headGo.transform.SetParent(root, false);
            headGo.transform.localPosition = new Vector3(0f, 2.18f, 0f);
            headGo.transform.localScale = new Vector3(0.38f, 0.42f, 0.38f);
            headGo.GetComponent<Renderer>().sharedMaterial = AikoProps.Lit(new Color(0.22f, 0.22f, 0.24f), 0.7f);
            Destroy(headGo.GetComponent<Collider>());

            // The badge: a lanyard card, the only friendly thing about her.
            GameObject badge = AikoProps.Box("Badge", Vector3.zero, new Vector3(0.12f, 0.16f, 0.02f), new Color(0.95f, 0.95f, 0.9f), root, collider: false);
            badge.transform.localPosition = new Vector3(0.12f, 1.45f, 0.24f);

            // The eye: a visor that glows, and a spot light that shows exactly where she is
            // looking. Being able to see her gaze is part of being able to beat her.
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Cube);
            eye.name = "Eye";
            eye.transform.SetParent(headGo.transform, false);
            eye.transform.localPosition = new Vector3(0f, 0.05f, 0.42f);
            eye.transform.localScale = new Vector3(0.7f, 0.12f, 0.1f);
            Destroy(eye.GetComponent<Collider>());

            var gazeGo = new GameObject("Gaze");
            gazeGo.transform.SetParent(root, false);
            gazeGo.transform.localPosition = new Vector3(0f, 2.2f, 0.2f);
            Light gaze = gazeGo.AddComponent<Light>();
            gaze.type = LightType.Spot;
            gaze.range = config.sightRange * 0.7f;
            gaze.spotAngle = Mathf.Min(90f, config.sightFov * 0.6f);
            gaze.intensity = 6f;
            gaze.shadows = LightShadows.None;
            return (eye.GetComponent<Renderer>(), gaze);
        }

        // Her eye and her light, by mood.
        public static Color MoodColour(Mood mood)
        {
            switch (mood)
            {
                case Mood.Alert: return new Color(1f, 0.72f, 0.2f);
                case Mood.Hunt: return new Color(1f, 0.12f, 0.08f);
                case Mood.Kind: return new Color(0.55f, 0.9f, 0.65f);
                default: return new Color(0.85f, 0.9f, 1f);
            }
        }

        // ---- movement -----------------------------------------------------------------

        public void MoveTo(Vector3 destination, Pace pace)
        {
            CurrentPace = pace;
            turning = false;
            if (!Agent.isOnNavMesh) return;
            Agent.isStopped = false;
            Agent.speed = Speed(pace);
            if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                destination = hit.position;
            Agent.SetDestination(destination);
        }

        float Speed(Pace pace)
        {
            switch (pace)
            {
                case Pace.Sneak: return config.sneakSpeed;
                case Pace.Hurry: return config.hurrySpeed;
                case Pace.Run: return config.runSpeed;
                default: return config.walkSpeed;
            }
        }

        public void Stop()
        {
            if (!Agent.isOnNavMesh) return;
            Agent.ResetPath();
            Agent.isStopped = true;
        }

        public bool Arrived(float radius)
        {
            if (!Agent.isOnNavMesh) return true;
            if (Agent.pathPending) return false;
            return Agent.remainingDistance <= Mathf.Max(radius, Agent.stoppingDistance);
        }

        public bool PathFailed => Agent.isOnNavMesh && !Agent.pathPending && Agent.pathStatus == NavMeshPathStatus.PathInvalid;

        public void Warp(Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 5f, NavMesh.AllAreas)) Agent.Warp(hit.position);
        }

        public void TurnToward(Vector3 point)
        {
            lookTarget = point;
            turning = true;
        }

        // Head sweep in degrees off the body's forward; drives the sight cone for sweeps.
        public void SweepHead(float degrees) => headYaw = degrees;

        // The blink channel's move: a burst of speed straight at a point for a window of
        // time — honest movement, only faster than anyone would choose to be seen moving.
        public void BurstToward(Vector3 point, float speed, float seconds, float maxDistance)
        {
            burstFrom = transform.position;
            burstSpeed = speed;
            burstMax = maxDistance;
            burstUntil = Time.time + seconds;
            MoveTo(point, Pace.Run);
            Agent.speed = speed;
            Agent.acceleration = 200f;
        }

        // ---- hands --------------------------------------------------------------------

        public void Carry(Item item)
        {
            if (item == null) return;
            carried = item;
            item.SetCarried(true, hand);
        }

        public void PutDown(Item item)
        {
            if (item == null) return;
            item.SetCarried(false, null);
            item.transform.position = transform.position + transform.forward * 0.6f + Vector3.up * 0.3f;
            if (carried == item) carried = null;
        }

        // ---- presentation --------------------------------------------------------------

        public void SetMood(Mood mood)
        {
            CurrentMood = mood;
            Color c = MoodColour(mood);
            if (eyeLight != null) eyeLight.color = c;
            if (eyeRenderer != null) eyeRenderer.sharedMaterial = AikoProps.Emissive(c, mood == Mood.Hunt ? 6f : 3f);
        }

        void Update()
        {
            // Burst windows end on time or distance, whichever comes first.
            if (burstUntil > 0f && (Time.time > burstUntil || Vector3.Distance(transform.position, burstFrom) > burstMax))
            {
                burstUntil = -1f;
                Agent.speed = Speed(CurrentPace);
                Agent.acceleration = 14f;
                Stop();
            }

            if (turning)
            {
                Vector3 d = lookTarget - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 240f * Time.deltaTime);
            }

            Head.localRotation = Quaternion.Euler(8f, headYaw, 0f);
            Footsteps();
            OpenDoorsInTheWay();
        }

        // She doesn't walk through doors: she opens them, and you hear it. Locked ones are
        // carved out of the NavMesh, so her path never asks her to.
        void OpenDoorsInTheWay()
        {
            if (Agent.velocity.sqrMagnitude < 0.05f) return;
            HingeDoor door = HingeDoor.ClosedAhead(transform.position, Agent.velocity, 1.8f);
            if (door != null && !door.Locked) door.OpenFor(transform.position, NoiseAuthor.Aiko);
        }

        void Footsteps()
        {
            if (Silent || Agent.velocity.sqrMagnitude < 0.2f) { stepTimer = 0f; return; }
            float interval = CurrentPace == Pace.Run ? 0.3f : CurrentPace == Pace.Hurry ? 0.4f : CurrentPace == Pace.Sneak ? 0.8f : 0.55f;
            stepTimer += Time.deltaTime;
            if (stepTimer < interval) return;
            stepTimer = 0f;
            float volume = CurrentPace == Pace.Sneak ? 0.25f : CurrentPace == Pace.Run ? 1f : 0.6f;
            feet.PlayOneShot(ProceduralAudio.AikoStep(), volume * SoundSettings.Get(SoundKind.Aiko));
            NoiseBus.Emit(transform.position, volume * 0.8f, NoiseKind.AikoStep, NoiseAuthor.Aiko);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other is CharacterController) Touched?.Invoke(other);
        }
    }
}
