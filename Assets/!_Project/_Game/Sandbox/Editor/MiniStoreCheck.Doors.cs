using System.Collections;
using Kehai;
using Kehai.Karen;
using Kehai.Sandbox;
using UnityEngine;

// The mini store's doorways (28 November): every door fills its doorway, with no light round it,
// and every hinged door opens away from whoever opens it, from either side, and turns round when
// used mid-swing.
public static partial class MiniStoreCheck
{
    static IEnumerator Doorways()
    {
        GameObject root = MiniStore.Root;
        Transform doors = root != null ? root.transform.Find("Doors") : null;
        if (doors == null) { Expect(false, "the mini store has its doors"); yield break; }

        // Frames: just past each edge of a shut door, and just over its top, there is wall.
        int edges = 0, open = 0;
        foreach (Transform door in doors)
        {
            Bounds b = MiniStore.Footprint(door.gameObject);
            if (b.size == Vector3.zero) continue;
            Vector3 along = b.size.x >= b.size.z ? Vector3.right : Vector3.forward;
            float half = Vector3.Dot(b.extents, along);
            foreach (float side in new[] { -1f, 1f })
            {
                edges++;
                Vector3 probe = new Vector3(b.center.x, 1.2f, b.center.z) + along * side * (half + 0.03f);
                if (!WallAt(probe, door)) { open++; Debug.Log($"MiniStoreCheck: open edge beside {door.name} at {probe}"); }
            }
            edges++;
            Vector3 over = new Vector3(b.center.x, b.max.y + 0.03f, b.center.z);
            if (b.max.y < MiniStore.LintelHeight && !WallAt(over, door)) { open++; Debug.Log($"MiniStoreCheck: open top over {door.name}"); }
        }
        Expect(edges > 0 && open == 0, $"every door fills its doorway: wall at each side and over the top ({edges - open} of {edges})");

        // Swings: from each side the leaf moves away from you; a second E mid-swing turns it round.
        int doorsTried = 0, away = 0, turned = 0;
        foreach (HingeDoor door in root.GetComponentsInChildren<HingeDoor>())
        {
            if (door.Panel == null || door.Locked || door.IsOpen || door.IsMoving) continue;
            doorsTried++;
            Vector3 c = door.DoorCentre;
            Vector3 normal = Thinnest(door.Panel);
            bool bothAway = true;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 from = c + normal * side * 0.8f;
                from.y = c.y;
                door.Use(from, NoiseAuthor.Player);
                yield return Wait(0.25f);
                Vector3 moved = door.Panel.bounds.center - c;
                if (Vector3.Dot(moved, normal * side) >= -0.02f) bothAway = false;
                bool wasMoving = door.IsMoving;
                door.Use(from, NoiseAuthor.Player);
                for (float t = 0f; t < 4f && door.IsMoving; t += Time.deltaTime) yield return null;
                if (wasMoving && !door.IsOpen) turned++;
                if (door.IsOpen) { door.Use(from, NoiseAuthor.Player); for (float t = 0f; t < 4f && door.IsMoving; t += Time.deltaTime) yield return null; }
                yield return Wait(0.1f);
            }
            if (bothAway) away++;
        }
        Expect(doorsTried > 0 && away == doorsTried, $"every hinged door opens away from you, from either side ({away} of {doorsTried})");
        Expect(turned == doorsTried * 2, $"E mid-swing turns a door round ({turned} of {doorsTried * 2})");
    }

    // Something solid that isn't part of the door itself.
    static bool WallAt(Vector3 at, Transform door)
    {
        foreach (Collider c in Physics.OverlapBox(at, new Vector3(0.01f, 0.01f, 0.01f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(door)) return true;
        return false;
    }

    // The leaf's face direction: its thinnest axis in the world.
    static Vector3 Thinnest(BoxCollider panel)
    {
        Vector3 s = Vector3.Scale(panel.size, panel.transform.lossyScale);
        s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        Vector3 axis = s.x <= s.z ? panel.transform.right : panel.transform.forward;
        axis.y = 0f;
        return axis.normalized;
    }
}
