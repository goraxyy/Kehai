using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Karen's body: a NavMeshAgent with eyes, feet and hands (Karen.md §2.1).
    //
    // What it has is as important as what it hasn't. It has no reference to the player at
    // all — that single missing reference is the difference between a stalker and an
    // investigator. It walks where the plan sends it, sees through its SightSensor, and
    // learns it has touched someone only by the contact itself.
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class KarenBody : MonoBehaviour
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

        KarenConfig config;
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

        public static KarenBody Build(Transform parent, KarenConfig config, Vector3 at)
        {
            var root = new GameObject("KAREN_Body");
            root.transform.SetParent(parent, false);
            root.transform.position = at;
            (Renderer eyeRenderer, Light gaze) = BuildLook(root.transform, config);

            var body = root.AddComponent<KarenBody>();
            body.config = config;
            body.Agent = root.GetComponent<NavMeshAgent>();
            body.Agent.radius = config.bodyRadius;
            body.Agent.height = config.bodyHeight;
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
            touch.height = Mathf.Max(config.bodyHeight, config.catchRadius * 2f);
            touch.center = Vector3.up * config.bodyHeight * 0.5f;
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
            root.AddComponent<KarenFloorCone>();   // her gaze, painted on the floor
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 5f, NavMesh.AllAreas)) body.Agent.Warp(hit.position);
            return body;
        }

        // How she looks, and nothing else: a black capsule the size of a person (the player's own
        // size, from KarenBootstrap), and the spot light of her gaze, under `root`. A silhouette
        // with nothing to read in it but where she is and where she's looking. The game gives it
        // a body (Build); the replay dresses a puppet in it. There is no separate eye: her mood
        // shows in her light.
        public static (Renderer eye, Light gaze) BuildLook(Transform root, KarenConfig config)
        {
            float height = Mathf.Max(1f, config.bodyHeight);
            float radius = Mathf.Clamp(config.bodyRadius, 0.15f, height * 0.5f);

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "Body";
            capsule.transform.SetParent(root, false);
            capsule.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            capsule.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            capsule.GetComponent<Renderer>().sharedMaterial = KarenProps.Lit(new Color(0.01f, 0.01f, 0.012f), 0.1f);
            Destroy(capsule.GetComponent<Collider>());

            // Her gaze: a spot light that shows exactly where she is looking. Being able to see
            // her gaze is part of being able to beat her.
            var gazeGo = new GameObject("Gaze");
            gazeGo.transform.SetParent(root, false);
            gazeGo.transform.localPosition = new Vector3(0f, height - 0.15f, radius * 0.5f);
            Light gaze = gazeGo.AddComponent<Light>();
            gaze.type = LightType.Spot;
            gaze.range = config.sightRange * 0.7f;
            gaze.spotAngle = Mathf.Min(90f, config.sightFov * 0.6f);
            gaze.intensity = 6f;
            gaze.shadows = LightShadows.None;
            return (null, gaze);
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
            if (eyeRenderer != null) eyeRenderer.sharedMaterial = KarenProps.Emissive(c, mood == Mood.Hunt ? 6f : 3f);
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

            KeepPersonalSpace();
            Head.localRotation = Quaternion.Euler(8f, headYaw, 0f);
            Footsteps();
            OpenDoorsInTheWay();
        }

        // Her touch kills (KarenBrain.OnTouched), so walking up to you is something she only
        // does running — a chase, a lunge. Following, stalking, searching, she stops short of
        // where she sees you, or saw you a moment ago, and watches; she moves again when you do.
        void KeepPersonalSpace()
        {
            if (CurrentPace == Pace.Run || burstUntil > 0f || !Agent.isOnNavMesh || Agent.isStopped) return;
            if (Sight == null || !(Sight.SeesNow || Sight.Time_SinceSeen() < 3f)) return;
            Vector3 d = Sight.LastSeenPosition - transform.position;
            d.y = 0f;
            if (d.magnitude >= config.personalSpace) return;
            Agent.isStopped = true;
            TurnToward(Sight.LastSeenPosition);
        }

        // She doesn't walk through doors: she opens them, and you hear it. Locked ones are
        // carved out of the NavMesh, so her path never asks her to.
        void OpenDoorsInTheWay()
        {
            if (Agent.velocity.sqrMagnitude < 0.05f) return;
            HingeDoor door = HingeDoor.ClosedAhead(transform.position, Agent.velocity, 1.8f);
            if (door != null && !door.Locked) door.OpenFor(transform.position, NoiseAuthor.Karen);
        }

        void Footsteps()
        {
            if (Silent || Agent.velocity.sqrMagnitude < 0.2f) { stepTimer = 0f; return; }
            float interval = CurrentPace == Pace.Run ? 0.3f : CurrentPace == Pace.Hurry ? 0.4f : CurrentPace == Pace.Sneak ? 0.8f : 0.55f;
            stepTimer += Time.deltaTime;
            if (stepTimer < interval) return;
            stepTimer = 0f;
            float volume = CurrentPace == Pace.Sneak ? 0.25f : CurrentPace == Pace.Run ? 1f : 0.6f;
            feet.PlayOneShot(ProceduralAudio.KarenStep(), volume * SoundSettings.Get(SoundKind.Karen));
            NoiseBus.Emit(transform.position, volume * 0.8f, NoiseKind.KarenStep, NoiseAuthor.Karen);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other is CharacterController) Touched?.Invoke(other);
        }
    }
}
