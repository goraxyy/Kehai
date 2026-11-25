using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using Kehai.Sandbox;
using Kehai.Store;
using UnityEngine;

// Phase 9's interactions, tried in the mini store with the real Karen (0.5.2, C9.8, C9.9, C9.11).
// Each row starts a fresh shift with no tricks allowed, puts the tester somewhere, gives her
// something to have sensed (a clank beside a place, a thrown item, a door left swinging) and
// watches what she does. Each has its seen and unseen pair:
//   fridge check:  you in the chiller she checks → caught, after its door opened;
//                  you in another chiller → nobody, and her belief there drops
//   kneel check:   you under the rack where she kneels → caught; past 1.5 m → nobody
//   decoy throw:   an item you threw lands → her belief moves there; one nobody threw → nothing
//   swinging door: a door you went through, seen moving → her belief goes past it; traces off → less
//   decoy from the hand: the real throw (PlayerInteract), out of her sight → what she hears at the
//                  landing and what she hears where you threw it from (the test players' counter)
public static partial class MiniStoreCheck
{
    // Looked up again after every restart: a fresh shift rebuilds what the old places stood in.
    // The racks stay in the store's rack group, moved into the mini stockroom, so the search is
    // every hiding place inside the mini store, not under its root.
    static HidingPlace[] Chillers() => HidingPlace.All
        .Where(p => p != null && p.Usable && !p.requiresCrouch && MiniStore.Contains(HideCheck.Centre(p)) &&
                    StoreMap.AreaAt(HideCheck.Centre(p)) == "Cold storage" && HideCheck.DoorOf(p) != null)
        .OrderBy(p => HideCheck.Centre(p).x).ThenBy(p => HideCheck.Centre(p).z).ToArray();

    static HidingPlace Rack() => HidingPlace.All
        .Where(p => p != null && p.Usable && p.requiresCrouch && MiniStore.Contains(HideCheck.Centre(p)) &&
                    StoreMap.AreaAt(HideCheck.Centre(p)) == "Stockroom")
        .OrderByDescending(p => Mathf.Max(p.interior.size.x * p.transform.lossyScale.x, p.interior.size.z * p.transform.lossyScale.z))
        .ThenBy(p => HideCheck.Centre(p).x).FirstOrDefault();

    static IEnumerator Interactions()
    {
        var before = MiniStoreSettings.Allowed;
        MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.None;
        int chillers = Chillers().Length;
        bool rack = Rack() != null;
        Expect(chillers >= 2, $"two chillers you can hide in, with doors she can open ({chillers})");
        Expect(rack, "a stockroom rack you can crouch under");
        if (chillers >= 2)
        {
            yield return FridgeCheck(0, 0, true);
            yield return FridgeCheck(0, 1, false);
        }
        if (rack)
        {
            yield return KneelCheck(true);
            yield return KneelCheck(false);
        }
        if (chillers >= 2)
        {
            yield return Decoy(1, true);
            yield return Decoy(1, false);
            yield return DecoyFromHand();
            yield return SwingingDoor(1, true);
            yield return SwingingDoor(1, false);
        }
        MiniStoreSettings.Allowed = before;
        MiniStoreSettings.Traces = true;
        MiniStoreSettings.Hearing = true;
        MiniStoreSettings.Building = true;
        KarenBrain last = UnityEngine.Object.FindAnyObjectByType<KarenBrain>();
        if (last != null) { MiniStoreSettings.ApplyTo(last.config); MiniStoreSettings.ApplySenses(last); }
    }

    static KarenBrain her;

    // A fresh shift (her memory cleared), no tricks, started; `her` is the new Karen.
    static IEnumerator Fresh()
    {
        her = null;
        UnityEngine.Object.FindAnyObjectByType<TricksSandbox>()?.RestartNow();
        yield return Wait(2f);
        for (float t = 0f; t < 20f && (her == null || her.Body == null || her.Ctx == null || her.Ctx.Belief == null); t += Time.deltaTime)
        {
            her = UnityEngine.Object.FindAnyObjectByType<KarenBrain>();
            yield return null;
        }
        if (her == null) yield break;
        if (her.Ctx.Log != null)
            her.Ctx.Log.Written += r =>
            {
                if (r != null && (r.Kind == "SEARCH" || r.Kind == "PLAN" || r.Kind == "CHECK" || r.Kind == "SENSE" || r.Kind == "CAUGHT"))
                    Debug.Log($"MiniStoreCheck: her: {r.Kind} {r.Text}");
            };
        MiniStoreSettings.ApplyTo(her.config);
        if (her.Shift != null && !her.Shift.IsShiftActive) her.Shift.StartShift();
        her.enabled = true;
        yield return Wait(0.5f);
        MiniStoreSettings.ApplySenses(her);   // after the shift starts: its start sets her senses to the rung's
    }

    static GameObject Player() => GameObject.FindGameObjectWithTag("Player");

    static void PutPlayer(Vector3 feet)
    {
        GameObject player = Player();
        var controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = feet + Vector3.up * 0.05f;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
    }

    static Vector3 Floor(HidingPlace p) => new Vector3(HideCheck.Centre(p).x, p.transform.TransformPoint(p.interior.min).y, HideCheck.Centre(p).z);

    // Out in front of a place's door, where a clank from it would be heard.
    static Vector3 InFront(HidingPlace p)
    {
        Vector3 door = HideCheck.DoorOf(p).DoorPoint;
        Vector3 outward = door - HideCheck.Centre(p);
        outward.y = 0f;
        return new Vector3(door.x, Floor(p).y, door.z) + outward.normalized * 1f;
    }

    static float MassNear(Vector3 at, float radius)
    {
        StoreMap map = her.Ctx.Map;
        float mass = 0f;
        for (int cell = 0; cell < map.CellCount; cell++)
            if (FairnessGuard.Flat(map.CellPosition[cell], at) <= radius) mass += her.Ctx.Belief[cell];
        return mass;
    }

    static void Clank(Vector3 at) =>
        her.Ctx.Belief.Apply(new Observation(SenseChannel.Hearing, at, 0.9f, 1.2f, "check: a clank from here"));

    static bool Concealed()
    {
        PlayerPresence you = PlayerPresence.Current;
        if (you == null) return false;
        HidingPlace place = HidingPlace.At(you, out float coverage);
        return place != null && place.Conceals(you, coverage);
    }

    // Wait for the check to end: she looked, she caught you, or the time is up.
    static IEnumerator Watch(CheckHiding check, float seconds, Action<bool> doorOpenBeforeCatch, IHidingDoor door)
    {
        bool openSeen = door == null;
        float report = 0f;
        for (float t = 0f; t < seconds && !check.Looked && !her.KilledThisShift; t += Time.deltaTime)
        {
            if (door != null && door.OpenFraction >= CheckHiding.OpenEnough) openSeen = true;
            if (t >= report)
            {
                report += 3f;
                var agent = her.Body.Agent;
                Debug.Log($"MiniStoreCheck: {t:0} s: plan {her.CurrentPlan?.Name}, at {her.Body.Position}, " +
                          $"path {(agent != null && agent.isOnNavMesh ? agent.pathStatus + " " + agent.remainingDistance.ToString("0.0") + " m to " + agent.destination : "off the NavMesh")}");
            }
            yield return null;
        }
        doorOpenBeforeCatch(openSeen);
    }

    static IEnumerator FridgeCheck(int checksAt, int youAt, bool expectCatch)
    {
        string row = expectCatch ? "fridge check, you inside" : "fridge check, you in the next one";
        yield return Fresh();
        if (her == null) { Expect(false, row + ": no Karen"); yield break; }
        HidingPlace[] all = Chillers();
        if (all.Length <= Mathf.Max(checksAt, youAt)) { Expect(false, row + ": the chillers after the restart"); yield break; }
        HidingPlace checks = all[checksAt], youIn = all[youAt];
        IHidingDoor yours = HideCheck.DoorOf(youIn);
        yours.SetOpenForCheck(false);
        yield return Wait(1f);
        PutPlayer(Floor(youIn));
        yield return Wait(0.3f);
        bool hidden = Concealed();
        Clank(InFront(checks));
        float beside = MassNear(HideCheck.Centre(checks), HideCheck.Near);
        CheckHiding check = her.ForceHideCheck(checks, true, out string why);
        Expect(check != null, $"{row}: she sets out ({why})");
        if (check == null) yield break;
        bool openFirst = false;
        yield return Watch(check, 40f, o => openFirst = o, HideCheck.DoorOf(checks));
        yield return Snap("fridge_" + (expectCatch ? "inside" : "next"), her.Body.Position);
        float after = her.KilledThisShift ? beside : MassNear(HideCheck.Centre(checks), HideCheck.Near);
        Debug.Log($"MiniStoreCheck: {row}: hidden {hidden}, looked {check.Looked}, caught {her.KilledThisShift}, door open first {openFirst}, belief beside {beside:P0} → {after:P0}");
        Expect(hidden, $"{row}: the tester is hidden in {youIn.name}");
        if (expectCatch)
            Expect(her.KilledThisShift && check.Found && openFirst, $"{row}: she opens {checks.name}, looks, and catches you (caught {her.KilledThisShift}, door open first {openFirst})");
        else
            Expect(check.Looked && !her.KilledThisShift && after < beside * 0.5f, $"{row}: she opens {checks.name}, nobody, and her belief beside it drops ({beside:P0} → {after:P0})");
    }

    static IEnumerator KneelCheck(bool expectCatch)
    {
        string row = expectCatch ? "kneel check, you under where she kneels" : "kneel check, you past her reach";
        yield return Fresh();
        if (her == null) { Expect(false, row + ": no Karen"); yield break; }
        HidingPlace rack = Rack();
        if (rack == null) { Expect(false, row + ": the rack after the restart"); yield break; }
        // Along the rack's length (its longer side): one end and the other.
        Vector3 size = Vector3.Scale(rack.interior.size, rack.transform.lossyScale);
        bool alongX = size.x >= size.z;
        float length = alongX ? size.x : size.z;
        Vector3 axis = alongX ? rack.transform.right : rack.transform.forward;
        Vector3 centre = Floor(rack);
        Vector3 endA = centre - axis * (length * 0.5f - 0.9f), endB = centre + axis * (length * 0.5f - 0.9f);
        Vector3 clankAt = expectCatch ? endA : endB;
        // On her map's cell beside the rack nearest that end: a clank from under it, heard in the aisle.
        List<int> beside = HideCheck.CellsBeside(her.Ctx.Map, rack);
        if (beside.Count == 0) { Expect(false, row + ": her map has no cells beside the rack"); yield break; }
        // Beside its long side, not past its end: a noise from under it is heard in the aisle.
        List<int> alongside = beside.Where(c => Mathf.Abs(Vector3.Dot(her.Ctx.Map.CellPosition[c] - centre, axis)) <= length * 0.5f - 0.5f).ToList();
        if (alongside.Count == 0) alongside = beside;
        Vector3 aisle = her.Ctx.Map.CellPosition[alongside.OrderBy(c => (her.Ctx.Map.CellPosition[c] - clankAt).sqrMagnitude).First()];
        // Within: under the rack right across from that cell. Past her reach: under the other end.
        Vector3 you = endA;
        if (expectCatch)
        {
            // On the rack's middle line, level with the clank, kept 0.9 m inside its ends.
            float along = Mathf.Clamp(Vector3.Dot(aisle - centre, axis), -(length * 0.5f - 0.9f), length * 0.5f - 0.9f);
            you = centre + axis * along;
        }
        var bot = new SandboxBot(Player().GetComponent<PlayerMotor>());
        PutPlayer(you);
        yield return bot.SetCrouch(true);
        yield return Wait(0.3f);
        bool hidden = Concealed();
        Clank(aisle);
        Debug.Log($"MiniStoreCheck: {row}: clank at {aisle}, {FairnessGuard.Flat(aisle, you):0.0} m from you; reason {HideCheck.Reason(her.Ctx, rack):P0}");
        CheckHiding check = her.ForceHideCheck(rack, true, out string why);
        Expect(check != null, $"{row}: she sets out ({why})");
        if (check == null) { bot.Crouch = false; yield break; }
        yield return Watch(check, 40f, _ => { }, null);
        Vector3 edge = rack.transform.TransformPoint(rack.interior.ClosestPoint(rack.transform.InverseTransformPoint(her.Body.Position)));
        float reach = FairnessGuard.Flat(edge, Player().transform.position);   // as her eye measures it
        yield return Snap("kneel_" + (expectCatch ? "within" : "beyond"), her.Body.Position);
        Debug.Log($"MiniStoreCheck: {row}: rack {length:0.0} m, hidden {hidden}, kneels {check.Kneels}, looked {check.Looked}, caught {her.KilledThisShift}, the rack's edge where she kneels to you {reach:0.00} m");
        Expect(hidden, $"{row}: the tester is crouched under {rack.name}");
        if (expectCatch)
            Expect(her.KilledThisShift && check.Kneels, $"{row}: she kneels and catches you ({reach:0.0} m)");
        else
            Expect(length < 2f * HideCheck.Reach + 1f ? !her.KilledThisShift : check.Looked && !her.KilledThisShift && reach > HideCheck.Reach,
                   $"{row}: she kneels {reach:0.0} m from you and catches nobody");
        bot.Crouch = false;
        yield return Wait(0.5f);
    }

    // A thrown item lands 8 m from her in her room: by your hand, or nobody's.
    static IEnumerator Decoy(int youAt, bool yours)
    {
        string row = yours ? "decoy throw" : "decoy, an item nobody threw";
        yield return Fresh();
        if (her == null) { Expect(false, row + ": no Karen"); yield break; }
        HidingPlace youIn = Chillers().ElementAtOrDefault(youAt);
        if (youIn == null) { Expect(false, row + ": the chiller after the restart"); yield break; }
        PutPlayer(Floor(youIn));
        yield return Wait(0.3f);
        StoreMap map = her.Ctx.Map;
        int here = map.CellAt(her.Body.Position);
        int land = Enumerable.Range(0, map.CellCount)
            .Where(c => here >= 0 && map.CellRoom[c] == map.CellRoom[here] && map.IsKarens(c))
            .OrderBy(c => Mathf.Abs(FairnessGuard.Flat(map.CellPosition[c], her.Body.Position) - 8f)).DefaultIfEmpty(-1).First();
        if (land < 0) { Expect(false, row + ": no spot 8 m away"); yield break; }
        Vector3 at = map.CellPosition[land];
        float before = MassNear(at, 2.5f);
        GameObject tin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tin.name = "Check decoy";
        tin.transform.localScale = Vector3.one * 0.15f;
        tin.transform.position = at + Vector3.up * 2.5f;
        var body = tin.AddComponent<Rigidbody>();
        body.mass = 0.4f;
        var item = tin.AddComponent<Item>();
        item.lastAuthor = yours ? NoiseAuthor.Player : NoiseAuthor.World;
        body.linearVelocity = Vector3.down * 4f;
        yield return Wait(1.5f);
        float after = MassNear(at, 2.5f);
        Debug.Log($"MiniStoreCheck: {row}: landed {FairnessGuard.Flat(at, her.Body.Position):0.0} m from her, belief there {before:P0} → {after:P0}");
        if (yours) Expect(after > before + 0.05f, $"{row}: she hears it land and her belief goes there ({before:P0} → {after:P0})");
        else Expect(after < before + 0.05f, $"{row}: nothing for her ({before:P0} → {after:P0})");
        UnityEngine.Object.Destroy(tin);
    }

    // The real throw, from your hand, where she can't see you: the test players' decoy counter
    // (SimulatedPlayer.ThrowDecoy) throws the same way. Her belief where it lands must beat her
    // belief where you threw it from (until 0.5.2 the hand said "something dropped": 54% vs 4%).
    static IEnumerator DecoyFromHand()
    {
        const string row = "decoy thrown from your hand";
        yield return Fresh();
        if (her == null) { Expect(false, row + ": no Karen"); yield break; }
        StoreMap map = her.Ctx.Map;
        int hers = map.CellAt(her.Body.Position);
        Vector3 from = default, way = default;
        bool found = false;
        foreach (int c in Enumerable.Range(0, map.CellCount)
                     .Where(c => hers >= 0 && map.IsKarens(c) && map.CellRoom[c] != map.CellRoom[hers] && MiniStore.Contains(map.CellPosition[c]))
                     .OrderBy(c => Mathf.Abs(FairnessGuard.Flat(map.CellPosition[c], her.Body.Position) - 7f)))
        {
            Vector3 spot = map.CellPosition[c];
            if (her.Body.Sight.CanSee(spot + Vector3.up * 1.2f)) continue;
            Vector3 hand = spot + Vector3.up * 1.4f;
            if (!SimulatedPlayerAim(spot, hand, out way)) continue;
            from = spot;
            found = true;
            break;
        }
        if (!found) { Expect(false, row + ": no spot out of her sight with room to throw"); yield break; }
        GameObject player = Player();
        PutPlayer(from);
        player.transform.rotation = Quaternion.LookRotation(way);
        GameObject tin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tin.name = "Check decoy (hand)";
        tin.transform.localScale = Vector3.one * 0.15f;
        tin.AddComponent<Rigidbody>().mass = 0.4f;
        var item = tin.AddComponent<Item>();
        item.type = ItemType.Stock;
        CarrySlot slot = player.GetComponent<CarrySlot>();
        PlayerInteract hands = player.GetComponent<PlayerInteract>();
        if (slot == null || hands == null || !slot.TryPickup(item)) { Expect(false, row + ": the tester can't hold it"); UnityEngine.Object.Destroy(tin); yield break; }
        yield return Wait(0.3f);
        float atYouBefore = MassNear(from, 1.5f);
        hands.DropNow(Kehai.Eval.SimulatedPlayer.DecoyThrowSpeed);
        yield return Wait(0.3f);
        float atYouSoon = MassNear(from, 1.5f);
        yield return Wait(1.2f);
        Vector3 landed = tin.transform.position;
        float atYou = MassNear(from, 1.5f), atLanding = MassNear(landed, 1.5f);
        bool threw = !slot.Contains(item) && FairnessGuard.Flat(landed, from) > 2.5f;
        Debug.Log($"MiniStoreCheck: {row}: thrown {FairnessGuard.Flat(landed, from):0.0} m, she stood {FairnessGuard.Flat(from, her.Body.Position):0.0} m off; " +
                  $"her belief where you threw from {atYouBefore:P0} → {atYouSoon:P0} (before it lands) → {atYou:P0}, where it landed {atLanding:P0}");
        Expect(threw, $"{row}: it left the hand and flew ({FairnessGuard.Flat(landed, from):0.0} m)");
        Expect(atLanding > 0.03f, $"{row}: she hears it land ({atLanding:P0} of her belief there)");
        Expect(atLanding > atYou, $"{row}: it lures her: more of her belief where it landed than where you threw it from ({atLanding:P0} vs {atYou:P0})");
        UnityEngine.Object.Destroy(tin);
    }

    // The test players' aim (SimulatedPlayer.DecoyAim): the clearest way that lands it furthest off.
    static bool SimulatedPlayerAim(Vector3 at, Vector3 hand, out Vector3 way) =>
        Kehai.Eval.SimulatedPlayer.DecoyAim(at, at, d => Physics.Raycast(hand, d, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 6f, out way);

    // A staff door she can see: you go through it as she looks, with her traces on or off.
    static IEnumerator SwingingDoor(int youAt, bool traces)
    {
        string row = traces ? "swinging door" : "swinging door, traces off";
        // Only her eye on the door: the building's door sensor and the creak would tell her the
        // door was used either way, so both rows have them off and differ only in the trace.
        MiniStoreSettings.Traces = traces;
        MiniStoreSettings.Hearing = false;
        MiniStoreSettings.Building = false;
        yield return Fresh();
        if (her == null) { Expect(false, row + ": no Karen"); yield break; }
        HidingPlace youIn = Chillers().ElementAtOrDefault(youAt);
        if (youIn == null) { Expect(false, row + ": the chiller after the restart"); yield break; }
        PutPlayer(Floor(youIn));
        HingeDoor door = null;
        Vector3 normal = Vector3.zero, centre = Vector3.zero, near = Vector3.zero;
        her.enabled = false;
        foreach (HingeDoor d in UnityEngine.Object.FindObjectsByType<HingeDoor>()
                     .Where(d => MiniStore.Contains(d.transform.position) && !d.Locked && d.Panel != null && !d.IsOpen)
                     .OrderBy(d => d.DoorCentre.x).ThenBy(d => d.DoorCentre.z))
        {
            Vector3 size = d.Panel.bounds.size;
            Vector3 axis = size.x < size.z ? Vector3.right : Vector3.forward;
            foreach (Vector3 side in new[] { axis, -axis })
            foreach (float back in new[] { 4.5f, 3.5f, 5.5f })
            {
                Vector3 spot = HidingApproach.ClosestReachable(her.Body.Agent, d.DoorCentre + side * back);
                if (Vector3.Dot(spot - d.DoorCentre, side) < 2f) continue;   // still on this side, and back from it
                her.Body.Warp(spot);
                her.Body.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(d.DoorCentre - spot, Vector3.up));
                Physics.SyncTransforms();
                if (!her.Body.Sight.CanSee(Sensorium.DoorwayFacing(d.DoorCentre, her.Body.Sight.Eye))) continue;
                door = d; normal = side; centre = d.DoorCentre; near = spot;
                break;
            }
            if (door != null) break;
        }
        her.enabled = true;
        if (door == null) { Expect(false, row + ": no door she can stand back from and see"); yield break; }
        yield return Wait(0.6f);
        // The far side of the door, within 8 m: what she can see through the doorway her eye
        // clears; the trace's spread lands in the room past it.
        float FarSide()
        {
            StoreMap map = her.Ctx.Map;
            float mass = 0f;
            for (int cell = 0; cell < map.CellCount; cell++)
            {
                Vector3 to = map.CellPosition[cell] - centre;
                if (Vector3.Dot(to, normal) < -0.5f && FairnessGuard.Flat(map.CellPosition[cell], centre) <= 8f) mass += her.Ctx.Belief[cell];
            }
            return mass;
        }
        float before = FarSide();
        door.Use(centre - normal * 0.8f, NoiseAuthor.Player);
        yield return Wait(0.4f);
        float after = FarSide();
        bool sees = her.Body.Sight.CanSee(Sensorium.DoorwayFacing(centre, her.Body.Sight.Eye));
        yield return Snap("door_" + (traces ? "traces" : "no_traces"), near);
        Debug.Log($"MiniStoreCheck: {row}: {door.name}, she sees its leaf {sees}, belief past it (8 m) {before:P0} → {after:P0}");
        SwingMass[traces ? 1 : 0] = after - before;
        if (!traces)
            // Small by today's numbers (the door trace's trust 0.5, spread 4 m), but only the trace does it.
            Expect(SwingMass[1] > 0.02f && SwingMass[0] < 0.01f, $"seen swinging, the door moves her belief past it; with traces off it doesn't ({SwingMass[1]:P0} vs {SwingMass[0]:P0})");
        else
            Expect(sees, $"{row}: she can see {door.name}'s leaf from {FairnessGuard.Flat(near, centre):0.0} m");
    }
    static readonly float[] SwingMass = new float[2];

    // One picture from above and behind her, looking where she is.
    static IEnumerator Snap(string tag, Vector3 at)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        string dir = OutDir();
        Camera main = Camera.main;
        if (dir == null || main == null) yield break;
        var go = new GameObject("MiniStoreCheck camera");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        // A corner above and behind her with nothing between it and her (a wall or a rack's top
        // would fill the picture).
        Vector3 look = at + Vector3.up * 0.8f;
        Vector3 from = at + new Vector3(-2.5f, 3.2f, -2.5f);
        foreach (float h in new[] { 2.6f, 1.9f })
        foreach (Vector3 d in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
        {
            Vector3 c = at + d.normalized * 2.8f + Vector3.up * h;
            if (Physics.CheckSphere(c, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.Linecast(c, look, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) && !KarenBody.IsHers(hit.collider)) continue;
            from = c;
            goto chosen;
        }
        chosen:
        go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from));
        yield return null;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = null;
        File.WriteAllBytes(Path.Combine(dir, $"mini_interaction_{tag}.png"), tex.EncodeToPNG());
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(tex);
        UnityEngine.Object.Destroy(go);
    }
}
