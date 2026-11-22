using System.Collections.Generic;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

public static class PhaseOneFixtures
{
    // Idempotent; call after the layout and its navigation exist. No imported assets required.
    public static void Install(Material outline)
    {
        var byRoom = new Dictionary<string, List<Light>>();
        foreach (Light light in LightProbe.CeilingLights)
        {
            if (light == null) continue;
            string room = RoomLightSwitch.RoomOf(light.transform.position);
            if (!byRoom.TryGetValue(room, out var lamps)) byRoom[room] = lamps = new List<Light>();
            lamps.Add(light);
        }
        var installed = new HashSet<string>();
        foreach (var sw in RoomLightSwitch.All) if (sw != null) installed.Add(sw.Room);
        Physics.SyncTransforms();
        foreach (HingeDoor door in HingeDoor.All)
        {
            if (door == null) continue;
            BoxCollider panel = door.Panel ?? door.FindPanel();
            if (panel == null) continue;
            Bounds bounds = panel.bounds;
            Vector3 along = bounds.size.x > bounds.size.z ? Vector3.right : Vector3.forward;
            Vector3 normal = Vector3.Cross(along, Vector3.up);
            float half = Mathf.Max(bounds.extents.x, bounds.extents.z);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 facing = normal * side;
                Vector3 probe = bounds.center + facing * 2f;
                string room = RoomLightSwitch.RoomOf(probe);
                if (installed.Contains(room) || !byRoom.TryGetValue(room, out var lamps)) continue;
                Vector3 at = bounds.center + along * (half + .25f) + facing * .15f;
                at.y = bounds.min.y;
                at = KarenFixtures.WallBoxAt(at, facing) + facing * .06f;
                var go = KarenProps.Box("Light switch - " + room, at, new Vector3(.14f, .2f, .1f), new Color(.82f,.8f,.7f));
                go.transform.rotation = Quaternion.LookRotation(facing);
                go.layer = LayerMask.NameToLayer("Interactable");
                var sw = go.AddComponent<RoomLightSwitch>();
                sw.Configure(room, lamps);
                sw.GetComponent<OutlineHighlight>().outlineMaterial = outline;
                installed.Add(room);
            }
        }
        foreach (ColdFixture freezer in Object.FindObjectsByType<ColdFixture>())
        {
            if (!freezer.frozen || RoomLightSwitch.RoomOf(freezer.transform.position).IndexOf("Cold", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            foreach (FurnitureDoor door in freezer.GetComponentsInChildren<FurnitureDoor>())
            {
                if (door.roomDoor != null || door.GetComponent<FreezerAlarm>() != null) continue;
                door.gameObject.AddComponent<FreezerAlarm>().Configure(door);
            }
        }
    }
    public static void ResetForShift()
    {
        FlashSaleState.End();
        foreach (var sw in RoomLightSwitch.All) if (sw != null) sw.Restore();
        foreach (var alarm in FreezerAlarm.All) if (alarm != null) alarm.Close();
        CustomerComplaint.ResetForShift();
    }
}
