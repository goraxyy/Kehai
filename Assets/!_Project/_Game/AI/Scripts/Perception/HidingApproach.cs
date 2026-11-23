using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    public static class HidingApproach
    {
        const float FollowUpRadius = 5f;
        const int FollowUpStops = 2;

        // From the remembered entry, head toward the near edge of other authored interiors.
        // Coordinates break ties so the order is independent of scene enumeration.
        public static Vector3[] NearbySearchPoints(Vector3 remembered, HidingPlace entered)
        {
            return Object.FindObjectsByType<HidingPlace>()
                .Where(place => place != entered && place.Usable)
                .Select(place =>
                {
                    Vector3 near = place.interior.ClosestPoint(place.transform.InverseTransformPoint(remembered));
                    near.y = place.interior.min.y;
                    return place.transform.TransformPoint(near);
                })
                .Distinct()
                .Select(point => (point, distance: (point - remembered).sqrMagnitude))
                .Where(candidate => candidate.distance <= FollowUpRadius * FollowUpRadius)
                .OrderBy(candidate => candidate.distance)
                .ThenBy(candidate => candidate.point.x)
                .ThenBy(candidate => candidate.point.y)
                .ThenBy(candidate => candidate.point.z)
                .Take(FollowUpStops)
                .Select(candidate => candidate.point)
                .ToArray();
        }

        // Sample around the remembered entry, then compare reachable path ends. A closet
        // interior often has no NavMesh; a partial path ending at its door is useful too.
        // Its only input about you is `remembered`, where her eye last saw you (WitnessedHide):
        // the brain works this out once per seen entry. With no agent on a NavMesh, the point itself.
        public static Vector3 ClosestReachable(NavMeshAgent agent, Vector3 remembered)
        {
            if (agent == null || !agent.isOnNavMesh) return remembered;
            Vector3 best = agent.transform.position;
            float bestDistance = (best - remembered).sqrMagnitude;
            float bestTravel = float.PositiveInfinity;
            var path = new NavMeshPath();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            for (int ring = 0; ring < 7 && bestDistance > 0.25f; ring++)
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
