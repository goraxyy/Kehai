using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // The understudy (Karen 0.3.0; build plan C4.2, B4.2): Kenji, "the new hire", who follows you
    // to learn the job. A mobile sensor with a name badge, unfailingly polite.
    //
    //   - He keeps 10–20 m behind you. While he can see you he stands still and watches, and every
    //     sighting reaches her (as testimony). When he loses you, he follows where he last saw you,
    //     or where she thinks you are.
    //   - Walk up to him (2.5 m) and press E: "Karen needs you on the tills" sends him out of the
    //     front doors for good; "Never mind" leaves him at it.
    public sealed class Understudy : MonoBehaviour, IInteractable, IHoverable
    {
        public const float Near = 10f, Far = 20f;     // how far behind you he keeps
        public const float TalkRange = 2.5f;
        public const string SendLine = GameNames.Antagonist + " needs you on the tills";
        public const string StayLine = "Never mind";

        // Kenji, while he's in the store (one at a time).
        public static Understudy Current { get; private set; }
        public static event System.Action<float> SentAway;   // seconds he was in, for her Ledger
        public bool Leaving { get; private set; }
        public bool Talking { get; private set; }
        public float HiredAt { get; private set; }
        public SightSensor Eye => eye;

        SightSensor eye;
        NavMeshAgent agent;
        SpeechBubble bubble;
        float retarget, nextLine;
        static readonly string[] lines =
        {
            "I'm learning so much!", "Is this how you always do it?", GameNames.Antagonist + " says you're very efficient.",
            "Should I write that down?", "Sorry — right behind you!", "Oh, is this where you take your breaks?"
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; SentAway = null; }

        public static Understudy Spawn(GameObject customerPrefab, Vector3 at)
        {
            if (customerPrefab == null) return null;
            GameObject go = Instantiate(customerPrefab, at, Quaternion.identity);
            go.name = "New hire (Kenji)";

            // Not a shopper: strip the shopping brain, keep the body and its agent.
            foreach (MonoBehaviour b in go.GetComponents<MonoBehaviour>())
                if (b is CustomerNPC || b is CustomerRequest) Destroy(b);

            var u = go.AddComponent<Understudy>();
            u.agent = go.GetComponent<NavMeshAgent>();
            var eyeGo = new GameObject("Eye");
            eyeGo.transform.SetParent(go.transform, false);
            eyeGo.transform.localPosition = Vector3.up * 1.6f;
            u.eye = eyeGo.AddComponent<SightSensor>();
            u.eye.range = 18f;
            u.eye.fieldOfView = 160f;
            u.eye.ignoreRoot = go.transform;
            u.bubble = SpeechBubble.Create(go.transform, 2.1f, 0.3f);
            u.HiredAt = Time.time;
            Current = u;
            return u;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        static Transform Player => PlayerInteract.Local != null ? PlayerInteract.Local.transform : null;

        void Update()
        {
            if (Leaving || Talking || eye == null) return;
            eye.transform.rotation = transform.rotation;
            eye.Tick(Time.deltaTime);
            if (eye.Awareness >= eye.glimpseAt)
                InfrastructureFeed.Report(new Observation(SenseChannel.Testimony, eye.LastSeenPosition, 0.9f, 1.5f, "understudy"));

            retarget -= Time.deltaTime;
            if (retarget <= 0f && agent != null && agent.isOnNavMesh)
            {
                retarget = 0.5f;
                Steer();
            }

            if (Time.time > nextLine && eye.SeesNow && Vector3.Distance(transform.position, eye.LastSeenPosition) < Far)
            {
                nextLine = Time.time + Random.Range(14f, 26f);
                bubble.Show(lines[Random.Range(0, lines.Length)]);
                StartCoroutine(HideLater(3f));
            }
        }

        // Where he goes: nowhere while he sees you (unless you're past 20 m: then up to 15 m);
        // after where he last saw you, or her guess, when he doesn't, stopping 12 m short.
        void Steer()
        {
            if (eye.SeesNow)
            {
                Vector3 you = eye.LastSeenPosition;
                float d = Vector3.Distance(transform.position, you);
                FaceTowards(you);
                if (d <= Far) { agent.isStopped = true; return; }
                agent.isStopped = false;
                agent.stoppingDistance = (Near + Far) * 0.5f;
                agent.SetDestination(you);
                return;
            }
            Vector3 goal = eye.Time_SinceSeen() < 8f ? eye.LastSeenPosition
                         : KarenBrain.Instance != null && KarenBrain.Instance.Belief != null ? KarenBrain.Instance.Belief.PeakPosition
                         : transform.position;
            agent.isStopped = false;
            agent.stoppingDistance = Near + 2f;
            agent.SetDestination(goal);
        }

        void FaceTowards(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 0.5f);
        }

        IEnumerator HideLater(float s)
        {
            yield return new WaitForSeconds(s);
            if (bubble != null && !Talking) bubble.Hide();
        }

        // ---- the talk -------------------------------------------------------------------------

        bool InReach(PlayerInteract player) =>
            player != null && Vector3.Distance(transform.position, player.transform.position) <= TalkRange;

        public void Interact(PlayerInteract player)
        {
            if (Leaving || Talking || !InReach(player)) return;
            StartCoroutine(Talk());
        }

        public string GetPrompt() => Leaving ? string.Empty : Talking ? string.Empty : "Talk to Kenji";

        public void OnHoverEnter() { }
        public void OnHoverExit() { }

        IEnumerator Talk()
        {
            Talking = true;
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
            yield return null;   // the E that opened the talk mustn't also answer it

            string[] options = { SendLine, StayLine };
            int selected = 0;
            bool confirmed = false;
            while (!confirmed && !Leaving)
            {
                Transform you = Player;
                if (you == null || Vector3.Distance(transform.position, you.position) > TalkRange * 2.5f) break;
                FaceTowards(you.position);
                if (Pressed(KeyCode.UpArrow)) selected = 0;
                if (Pressed(KeyCode.DownArrow)) selected = 1;
                if (Pressed(KeyCode.E) || Pressed(KeyCode.Return)) confirmed = true;
                bubble.ShowChoices("Hi! Did you need me?", options, selected);
                yield return null;
            }

            Talking = false;
            if (confirmed && selected == 0) { SendAway(); yield break; }
            bubble.Show(confirmed ? "Okay! I'll keep watching how you do it." : "Oh — okay!");
            StartCoroutine(HideLater(2.5f));
            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
        }

        static bool Pressed(KeyCode key) => !GamePause.Paused && Input.GetKeyDown(key);

        // "Karen needs you on the tills": he walks out of the front doors and is gone.
        public void SendAway()
        {
            if (Leaving) return;
            Leaving = true;
            Talking = false;
            SentAway?.Invoke(Time.time - HiredAt);
            if (bubble != null) { bubble.Show("Oh! On my way!"); StartCoroutine(HideLater(2f)); }
            Vector3 exit = transform.position;
            AutoDoubleDoor door = FindAnyObjectByType<AutoDoubleDoor>();
            if (door != null) exit = door.transform.position + door.transform.forward * 4f;
            if (agent != null && agent.isOnNavMesh) { agent.isStopped = false; agent.stoppingDistance = 0.5f; agent.SetDestination(exit); }
            StartCoroutine(GoneWhenOut(exit));
        }

        IEnumerator GoneWhenOut(Vector3 exit)
        {
            float until = Time.time + 40f;
            while (Time.time < until && Vector3.Distance(transform.position, exit) > 1.5f) yield return null;
            Destroy(gameObject);
        }
    }
}
