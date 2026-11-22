using System;
using System.Linq;
using Kehai.Karen;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// One pre-shift snapshot. All public results distinguish observed failures from missing evidence.
public sealed class PlacementProbe
{
    public const float MaximumHeight = 1.6f;
    const float EdgeTolerance = .12f;
    static readonly Vector3[] FourSides = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
    static readonly float[] Steps = { .55f, .8f, 1.05f, 1.35f, 1.65f, 2f, 2.3f };
    static readonly float[] Offsets = { 0f, -.32f, .32f };

    public sealed class Result
    {
        public float? Height;
        public Vector3? Standing;
        public string Approach, Prompt, PromptStatus, Evidence;

        public string HeightStatus => !Height.HasValue ? "unmeasured" :
            Height.Value > MaximumHeight ? "above eye level" : "within limit";

        public bool MissingEvidence => !Height.HasValue || Approach is null or "unmeasured" ||
            PromptStatus == "unmeasured";
        public bool HasFinding => MissingEvidence || HeightStatus == "above eye level" ||
            Approach == "unreachable" || PromptStatus == "missing";
    }

    readonly PlayerInteract player;
    readonly CharacterController body;
    readonly NavMeshQueryFilter filter;
    readonly NavMeshPath path = new NavMeshPath();
    readonly Collider[] overlaps = new Collider[32];
    readonly RaycastHit[] occluders = new RaycastHit[32];
    readonly float eyeHeight, radius, halfHeight, maxLookAngle;
    readonly Vector3 spawn, playerOriginAboveFeet;

    public bool HasNavigation { get; }

    public PlacementProbe(PlayerInteract player, NavMeshSurface surface)
    {
        this.player = player;
        if (player == null || surface == null || surface.navMeshData == null) return;
        body = player.GetComponentInParent<CharacterController>();
        var motor = player.GetComponentInParent<PlayerMotor>();
        if (body == null || motor == null) return;
        eyeHeight = motor.standCameraY;
        maxLookAngle = motor.maxLookAngle;
        Vector3 scale = body.transform.lossyScale;
        radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        halfHeight = Mathf.Max(radius, body.height * Mathf.Abs(scale.y) * .5f);
        Vector3 feet = body.transform.TransformPoint(body.center) - Vector3.up * halfHeight;
        playerOriginAboveFeet = player.transform.position - feet;
        filter = new NavMeshQueryFilter { agentTypeID = surface.agentTypeID, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(feet, out var hit, 1.5f, filter)) return;
        spawn = hit.position;
        HasNavigation = true;
    }

    public Result Check(MonoBehaviour component)
    {
        var target = (IInteractable)component;
        var result = new Result();
        var rack = component as ToolSnapPoint;
        bool handle = component is AutoDoorHandle;
        bool wasActive = component.gameObject.activeSelf;
        bool toolHome = rack != null && rack.tool != null && rack.IsToolHome;
        bool conditional = handle || toolHome || component is DoorBlock block && !block.IsBlocking ||
            component is CustomerNPC customer && !customer.IsWaitingToBeServed ||
            component is Understudy understudy && (understudy.Talking || understudy.Leaving);
        ReadPrompt(result, () => toolHome ? rack.ReturnPrompt : target.GetPrompt(), conditional,
            mayBeSilent: conditional && !handle && !toolHome);
        if (handle) result.Evidence = "Hand control while powered off. Geometry sampled with the hand control enabled.";
        if (toolHome) result.Evidence = "Recall with tool away. Geometry sampled with the home tool removed from ray tests.";

        // Startup hides these interaction volumes. Enable only their geometry for the probe,
        // then restore it before the next frame. Prompt selection remains state-dependent.
        var zone = toolHome ? rack.GetComponent<Collider>() : null;
        bool zoneEnabled = zone != null && zone.enabled;
        var homeColliders = toolHome ? rack.tool.GetComponentsInChildren<Collider>().Where(c => c.enabled).ToArray() : Array.Empty<Collider>();
        bool changed = handle && !wasActive || toolHome;
        try
        {
            if (handle && !wasActive) component.gameObject.SetActive(true);
            if (zone != null) zone.enabled = true;
            foreach (Collider c in homeColliders) c.enabled = false;
            if (changed) Physics.SyncTransforms();

            var colliders = component.GetComponentsInChildren<Collider>()
                .Where(c => c.enabled && ReferenceEquals(c.GetComponentInParent<IInteractable>(), target)).ToArray();
            if (colliders.Length == 0)
            {
                result.Evidence = Add(result.Evidence, "No enabled interaction collider");
                return result;
            }
            var bounds = colliders[0].bounds;
            foreach (var collider in colliders) bounds.Encapsulate(collider.bounds);
            Vector3 centre = bounds.center;
            Vector3[] sides = SidesOf(component, bounds, ref centre);
            float range = component is CustomerNPC && player != null ? player.customerInteractRange :
                player != null ? player.interactRange : 0f;
            CheckApproach(result, centre, sides, range, target, colliders);
            return result;
        }
        finally
        {
            if (handle && !wasActive) component.gameObject.SetActive(false);
            if (zone != null) zone.enabled = zoneEnabled;
            foreach (Collider c in homeColliders) c.enabled = true;
            if (changed) Physics.SyncTransforms();
        }
    }

    public Result CheckSlot(ShelfSlot slot)
    {
        var result = new Result();
        ReadPrompt(result, slot.GetPrompt, slot.IsBlocked, mayBeSilent: slot.IsBlocked);
        if (slot.frame == null) return result;
        Vector3 centre = slot.frame.TransformPoint(slot.local + Vector3.up * slot.height * .5f);
        CheckApproach(result, centre, new[] { slot.Outward }, player != null ? player.interactRange : 0f, slot);
        return result;
    }

    public Result CheckNearbyDoor(HingeDoor door)
    {
        var result = new Result { PromptStatus = "missing", Evidence = "Nearby E shortcut" };
        if (door.Panel == null) return result;
        var bounds = door.Panel.bounds;
        CheckApproach(result, bounds.center, BothFaces(bounds), door.interactRange, null, new[] { door.Panel }, door.transform.position);
        return result;
    }

    static Vector3[] SidesOf(MonoBehaviour component, Bounds bounds, ref Vector3 centre)
    {
        switch (component)
        {
            case ElectricBox box:
                BreakerPanel.FaceOf(box, out _, out Vector3 facing, out _, out _);
                return new[] { facing };
            case FogBox or CameraBox or PaBox or ClosingMic or RoomLightSwitch or BreakerSwitch:
                return new[] { component.transform.forward };
            case MusicBox or Puncher or CoffeeMachine:
                if (component.transform.Find("Staff Visual") != null) return new[] { -component.transform.forward };
                break;
            case AutoDoorHandle:
            case FurnitureDoor { roomDoor: not null }:
                return BothFaces(bounds);
            case FurnitureDoor { panel: not null } furniture:
                centre = furniture.panel.bounds.center;
                return new[] { -furniture.panel.transform.forward };
        }
        return FourSides;
    }

    static Vector3[] BothFaces(Bounds panel) => panel.size.x >= panel.size.z ?
        new[] { Vector3.forward, Vector3.back } : new[] { Vector3.right, Vector3.left };

    public static void ReadPrompt(Result result, Func<string> prompt, bool conditional, bool mayBeSilent = false)
    {
        try
        {
            result.Prompt = prompt();
            bool blank = string.IsNullOrWhiteSpace(result.Prompt);
            result.PromptStatus = blank && !mayBeSilent ? "missing" : conditional ? "conditional" : "present";
        }
        catch (Exception error)
        {
            result.PromptStatus = "unmeasured";
            result.Evidence = Add(result.Evidence, $"Prompt lookup failed ({error.GetType().Name})");
        }
    }

    void CheckApproach(Result result, Vector3 centre, Vector3[] sides, float range,
        IInteractable target, Collider[] colliders = null, Vector3? nearbyDoor = null)
    {
        if (!HasNavigation) return;

        // The furthest any sampled spot got, so an unreachable row says what stopped it.
        int furthest = -1;
        string stopped = null;
        void Stopped(int stage, string why)
        {
            if (stage > furthest) { furthest = stage; stopped = why; }
        }

        foreach (Vector3 side in sides)
        {
            Vector3 forward = Vector3.ProjectOnPlane(side, Vector3.up).normalized;
            if (forward.sqrMagnitude < .5f) continue;
            Vector3 lateral = Vector3.Cross(Vector3.up, forward);
            foreach (float step in Steps)
            foreach (float offset in Offsets)
            {
                Vector3 horizontal = centre + forward * step + lateral * offset;
                if (!TryFloor(horizontal, out Vector3 feet)) { Stopped(0, "no floor in front"); continue; }
                result.Height ??= centre.y - feet.y;
                if (!CanStand(feet)) { Stopped(1, "no room to stand in front"); continue; }
                Vector3 eye = feet + Vector3.up * eyeHeight;
                if (nearbyDoor is Vector3 hinge && Vector3.Distance(feet + playerOriginAboveFeet, hinge) > range) { Stopped(2, "too far from the door"); continue; }
                if (!CanAimAt(eye, centre, range, target, colliders, out string aim, out bool frontCleared)) { Stopped(3, $"aim from {step:0.##} m: {aim}"); continue; }
                if (!NavMesh.CalculatePath(spawn, feet, filter, path) || path.status != NavMeshPathStatus.PathComplete) { Stopped(4, "no route from the spawn"); continue; }

                result.Approach = "reachable";
                result.Standing = feet;
                result.Height = centre.y - feet.y;
                if (frontCleared) result.Evidence = Add(result.Evidence, "Reachable after taking the stock in front on the same shelf and face");
                return;
            }
        }
        result.Approach = "unreachable";
        result.Evidence = Add(result.Evidence, "No sampled front position with floor, standing clearance, aim and a complete NavMesh route from spawn" +
            (stopped != null ? $" (furthest: {stopped})" : ""));
    }

    bool TryFloor(Vector3 horizontal, out Vector3 feet)
    {
        feet = default;
        // The first floor-like surface could be a table. Check its height against the NavMesh
        // at this XZ so a tabletop won't masquerade as floor or get snapped to a nearby aisle.
        Vector3 from = horizontal + Vector3.up * .2f;
        if (!Physics.Raycast(from, Vector3.down, out var floor, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
            floor.normal.y < .5f ||
            !NavMesh.SamplePosition(floor.point, out var nav, .2f, filter)) return false;
        Vector3 delta = nav.position - floor.point;
        if (Mathf.Abs(delta.y) > EdgeTolerance || new Vector2(delta.x, delta.z).magnitude > EdgeTolerance) return false;
        feet = new Vector3(nav.position.x, floor.point.y, nav.position.z);
        return true;
    }

    bool CanStand(Vector3 feet)
    {
        Vector3 basePoint = feet + Vector3.up * .06f;
        int count = Physics.OverlapCapsuleNonAlloc(basePoint + Vector3.up * radius,
            basePoint + Vector3.up * (2f * halfHeight - radius), radius, overlaps,
            ~0, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++)
            if (!overlaps[i].transform.IsChildOf(body.transform) &&
                !Physics.GetIgnoreLayerCollision(body.gameObject.layer, overlaps[i].gameObject.layer)) return false;
        return true;
    }

    bool CanAimAt(Vector3 eye, Vector3 centre, float range, IInteractable target, Collider[] colliders,
        out string why, out bool frontCleared)
    {
        why = null;
        frontCleared = false;
        Vector3 delta = centre - eye;
        if (delta.sqrMagnitude < .0001f || Vector3.Angle(Vector3.ProjectOnPlane(delta, Vector3.up), delta) > maxLookAngle)
        {
            why = "too steep to look at";
            return false;
        }
        var ray = new Ray(eye, delta.normalized);
        if (target != null)
        {
            IInteractable aimed = player.FindAimedTarget(ray, includeEmptySlots: true);
            if (aimed is ShelfSlot nearer && target is ShelfSlot behind && nearer.InFrontOf(behind))
                frontCleared = ReferenceEquals(player.FindAimedTarget(ray, includeEmptySlots: true, frontClearedFor: behind), target);
            if (!frontCleared && !ReferenceEquals(aimed, target))
            {
                why = "the aim lands on " + Describe(aimed);
                return false;
            }
        }

        float contact = delta.magnitude;
        if (target is ShelfSlot slot)
        {
            if (!slot.RayHit(ray, out contact)) return false;
            contact = Mathf.Max(0f, contact - .05f); // The player's shelf ray allows this much at board edges.
        }
        else if (colliders != null)
        {
            contact = float.PositiveInfinity;
            foreach (var collider in colliders)
                if (collider.Raycast(ray, out var hit, range)) contact = Mathf.Min(contact, hit.distance);
        }
        if (contact > range)
        {
            why = $"out of reach ({contact:0.00} m)";
            return false;
        }

        int count = Physics.RaycastNonAlloc(ray, occluders, Mathf.Max(0f, contact - .01f),
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == occluders.Length)
        {
            why = "too many things in the way";
            return false;
        }
        for (int i = 0; i < count; i++)
            if (!occluders[i].collider.transform.IsChildOf(body.transform))
            {
                why = "blocked by " + occluders[i].collider.name;
                return false;
            }
        return true;
    }

    static string Describe(IInteractable aimed)
    {
        if (aimed == null) return "nothing";
        if (aimed is ShelfSlot other) return $"slot {other.Index} ({other.side} face)";
        return aimed is Component c ? $"{c.GetType().Name} {c.name}" : aimed.GetType().Name;
    }

    static string Add(string first, string second) => string.IsNullOrEmpty(first) ? second : first + ". " + second;
}
