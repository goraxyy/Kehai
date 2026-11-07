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
}
