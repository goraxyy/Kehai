using UnityEngine;

namespace Kehai.Karen
{
    // A line of sight through the store, for planning where to look from (0.3.0: where to stand
    // a camera). Shelving and walls block it; people and loose stock don't, so it says nothing
    // about who is standing where.
    public static class LineOfSight
    {
        static readonly RaycastHit[] hits = new RaycastHit[8];

        public static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float distance = d.magnitude;
            if (distance < 0.01f) return true;
            int count = Physics.RaycastNonAlloc(from, d / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = hits[i].collider;
                if (col is CharacterController) continue;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                return false;
            }
            return true;
        }
    }
}
