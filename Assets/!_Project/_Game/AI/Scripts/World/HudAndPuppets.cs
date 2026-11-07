using System.Collections;
using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // ---- mimicry (§8.5) -----------------------------------------------------------------

    // Karen takes a shopper over. It stops shopping and never queues; it walks at your pace
    // one aisle over and turns to face you whenever you look at it. Everyone else in the
    // store is a real customer — that is what makes it work. The tell: a possessed shopper
    // has no place in the till queue, and a player paying attention can prove it.
    public sealed class Possession : MonoBehaviour
    {
        public float duration = 90f;
        CustomerNPC npc;
        CustomerMemory memory;
        NavMeshAgent agent;
        SightSensor eye;
        float until, retarget;
        public bool Active { get; private set; }

        public static Possession Take(CustomerNPC customer, float seconds)
        {
            if (customer == null) return null;
            var p = customer.gameObject.GetOrAdd<Possession>();
            p.Begin(seconds);
            return p;
        }

        void Begin(float seconds)
        {
            npc = GetComponent<CustomerNPC>();
            memory = GetComponent<CustomerMemory>();
            agent = GetComponent<NavMeshAgent>();

            npc.StopAllCoroutines();          // the shopping routine ends here
            npc.ReleaseQueueSpot();
            if (memory != null) memory.possessed = true;

            var eyeGo = new GameObject("PossessedEye");
            eyeGo.transform.SetParent(transform, false);
            eyeGo.transform.localPosition = Vector3.up * 1.6f;
            eye = eyeGo.AddComponent<SightSensor>();
            eye.range = 16f;
            eye.fieldOfView = 150f;
            eye.ignoreRoot = transform;

            until = Time.time + seconds;
            Active = true;
            if (agent != null) agent.speed = 3.6f;
        }

        void Update()
        {
            if (!Active) return;
            eye.transform.rotation = transform.rotation;
            eye.Tick(Time.deltaTime);

            // It faces you when you look at it.
            if (eye.IsWatched && eye.SeesNow) npc.FaceTowards(eye.LastSeenPosition);

            if (eye.Awareness >= eye.glimpseAt)
                InfrastructureFeed.Report(new Observation(SenseChannel.Testimony, eye.LastSeenPosition, 0.95f, 1.5f,
                                                          $"mimic({name})"));

            retarget -= Time.deltaTime;
            if (retarget <= 0f && agent != null && agent.isOnNavMesh)
            {
                retarget = 1.2f;
                agent.SetDestination(Shadow());
            }

            if (Time.time > until) Release();
        }

        // One aisle over: a point about five metres to the side of where Karen believes you
        // are, on the far side of whatever shelving is between.
        Vector3 Shadow()
        {
            KarenBrain brain = KarenBrain.Instance;
            Vector3 centre = brain != null && brain.Belief != null ? brain.Belief.PeakPosition : transform.position;
            Vector3 side = Vector3.Cross(Vector3.up, (centre - transform.position).normalized);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            Vector3 wanted = centre + side * 5f;
            return NavMesh.SamplePosition(wanted, out NavMeshHit hit, 4f, NavMesh.AllAreas) ? hit.position : centre;
        }

        public void Release()
        {
            Active = false;
            if (memory != null) memory.possessed = false;
            if (eye != null) Destroy(eye.gameObject);
            npc.LeaveStore();
            Destroy(this);
        }
    }

    // The understudy (§8.5): from shift 8, a "new hire" who follows you to learn the job.
    // A mobile sensor with a name badge, unfailingly polite.
    public sealed class Understudy : MonoBehaviour
    {
        // 0.3.0, the API the trick and the talk build on (build plan C4.2; B4.2 gives him his
        // own file, his 10–20 m follow and the talk):
        //   Current   — Kenji, while he's in the store (one at a time);
        //   Leaving   — he's been sent away and is walking out;
        //   SendAway  — "Karen needs you on the tills": he leaves by the front doors;
        //   SentAway  — raised once with how many seconds he was in, for her Ledger.
        public static Understudy Current { get; private set; }
        public static event System.Action<float> SentAway;
        public bool Leaving { get; private set; }
        public float HiredAt { get; private set; }
        public SightSensor Eye => eye;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; SentAway = null; }

        void OnDestroy() { if (Current == this) Current = null; }

        public void SendAway()
        {
            if (Leaving) return;
            Leaving = true;
            SentAway?.Invoke(Time.time - HiredAt);
            Vector3 exit = transform.position;
            AutoDoubleDoor door = FindAnyObjectByType<AutoDoubleDoor>();
            if (door != null) exit = door.transform.position + door.transform.forward * 4f;
            if (agent != null && agent.isOnNavMesh) { agent.stoppingDistance = 0.5f; agent.SetDestination(exit); }
            StartCoroutine(GoneWhenOut(exit));
        }

        IEnumerator GoneWhenOut(Vector3 exit)
        {
            float until = Time.time + 40f;
            while (Time.time < until && Vector3.Distance(transform.position, exit) > 1.5f) yield return null;
            Destroy(gameObject);
        }

        SightSensor eye;
        NavMeshAgent agent;
        SpeechBubble bubble;
        float retarget, nextLine;
        static readonly string[] lines =
        {
            "I'm learning so much!", "Is this how you always do it?", GameNames.Antagonist + " says you're very efficient.",
            "Should I write that down?", "Sorry — right behind you!", "Oh, is this where you take your breaks?"
        };

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

        void Update()
        {
            if (Leaving) return;
            eye.transform.rotation = transform.rotation;
            eye.Tick(Time.deltaTime);
            if (eye.Awareness >= eye.glimpseAt)
                InfrastructureFeed.Report(new Observation(SenseChannel.Testimony, eye.LastSeenPosition, 0.9f, 1.5f, "understudy"));

            retarget -= Time.deltaTime;
            if (retarget <= 0f && agent != null && agent.isOnNavMesh)
            {
                retarget = 0.8f;
                Vector3 goal = eye.Time_SinceSeen() < 6f ? eye.LastSeenPosition
                             : KarenBrain.Instance != null ? KarenBrain.Instance.Belief.PeakPosition : transform.position;
                agent.stoppingDistance = 2.2f;
                agent.SetDestination(goal);
            }

            if (Time.time > nextLine && eye.SeesNow && Vector3.Distance(transform.position, eye.LastSeenPosition) < 5f)
            {
                nextLine = Time.time + Random.Range(12f, 25f);
                bubble.Show(lines[Random.Range(0, lines.Length)]);
                StartCoroutine(HideLater(3f));
            }
        }

        IEnumerator HideLater(float s)
        {
            yield return new WaitForSeconds(s);
            if (bubble != null) bubble.Hide();
        }
    }
}
