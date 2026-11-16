using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    public static class HidingApproach
    {
        // Sample around the remembered entry, then compare reachable path ends. A closet
        // interior often has no NavMesh; a partial path ending at its door is useful too.
        public static Vector3 ClosestReachable(NavMeshAgent agent, Vector3 remembered)
        {
            if (agent == null || !agent.isOnNavMesh) return remembered;
            Vector3 best = agent.transform.position;
            float bestDistance = (best - remembered).sqrMagnitude;
            float bestTravel = float.PositiveInfinity;
            var path = new NavMeshPath();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            for (int ring = 0; ring < 7; ring++)
            for (int direction = 0; direction < (ring == 0 ? 1 : 16); direction++)
            {
                float angle = direction * Mathf.PI / 8f;
                Vector3 probe = remembered + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 1f, filter)) continue;
                if (!agent.CalculatePath(hit.position, path) || path.corners.Length == 0) continue;
                Vector3 end = path.corners[path.corners.Length - 1];
                float distance = (end - remembered).sqrMagnitude;
                float travel = 0f;
                for (int i = 1; i < path.corners.Length; i++) travel += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                if (distance > bestDistance + 0.01f || (Mathf.Abs(distance - bestDistance) <= 0.01f && travel >= bestTravel)) continue;
                best = end; bestDistance = distance; bestTravel = travel;
            }
            return best;
        }
    }
}
