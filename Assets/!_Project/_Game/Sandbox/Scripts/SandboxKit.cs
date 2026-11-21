using System.Collections.Generic;
using Kehai.Karen;
using Unity.AI.Navigation;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The parts every sandbox is built from (0.5.2, Phase 11, C11.1): a floor, walls with a
    // doorway, a door, a stock fridge you can hide in, a stock rack you can crouch under, tall
    // and short shelves, ceiling lamps and the NavMesh. Plain boxes at real sizes, built in code
    // at load: a sandbox needs no scene file and no art (the rules keep both out of git).
    //
    // The hiding spaces are authored exactly as the store's are (HidingPlace.Author, 0.5.1), so
    // her eye treats them the same: you're hidden at 80% of your body inside, and under the rack
    // only when crouched.
    public static class SandboxKit
    {
        public const float WallHeight = 3f;
        public const float WallThickness = 0.2f;

        // The fridge: room inside for the player's 1 m-wide body (the staff lockers' opening is
        // 1.08 m for the same reason), and a door that swings out.
        public static readonly Vector3 FridgeInside = new Vector3(1.2f, 2.1f, 1.1f);
        public const float FridgeWall = 0.06f;

        // The rack: the lowest board's underside at 0.95 m clears a crouched body (0.8 m,
        // PlayerMotor.crouchControllerHeight) and stops a standing one.
        public const float RackLength = 2.4f, RackDepth = 1.1f, RackHeight = 2.3f;
        public const float RackFirstBoard = 0.95f, RackBoard = 0.04f;
        static readonly float[] RackBoards = { RackFirstBoard, 1.6f, 2.25f };

        // Tall shelves stop every eye line; short ones hide a crouched body but not a standing
        // person's eyes (1.6 m, PlayerMotor.standCameraY).
        public static readonly Vector3 TallShelf = new Vector3(2.4f, 2.2f, 0.9f);
        public static readonly Vector3 ShortShelf = new Vector3(2.0f, 1.2f, 0.8f);

        static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        public static readonly Color Floor = new Color(0.42f, 0.42f, 0.4f);
        public static readonly Color Wall = new Color(0.62f, 0.62f, 0.6f);
        public static readonly Color Shelf = new Color(0.33f, 0.36f, 0.4f);
        public static readonly Color Fridge = new Color(0.78f, 0.82f, 0.86f);
        public static readonly Color Rack = new Color(0.5f, 0.38f, 0.25f);
        public static readonly Color Stock = new Color(0.72f, 0.55f, 0.3f);

        public static Material Paint(Color colour)
        {
            if (materials.TryGetValue(colour, out Material m) && m != null) return m;
            m = KarenProps.Lit(colour, 0.2f);
            materials[colour] = m;
            return m;
        }

        // A solid box: a collider her eye and the NavMesh both see.
        public static GameObject Box(Transform parent, string name, Vector3 centre, Vector3 size, Color colour)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = centre;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = Paint(colour);
            return box;
        }

        public static Transform Group(Transform parent, string name, Vector3 at, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        // A floor `floorSize` (x, z) centred on `floorCentre`, and a walled room inside it with one
        // doorway in its south wall. Returns the doorway's centre on the floor.
        public static Vector3 Room(Transform parent, Vector3 floorCentre, Vector2 floorSize, Vector3 roomCentre, Vector2 roomSize, float doorWidth)
        {
            Box(parent, "Floor", floorCentre + Vector3.down * 0.05f, new Vector3(floorSize.x, 0.1f, floorSize.y), Floor);
            float h = WallHeight, t = WallThickness;
            float hx = roomSize.x * 0.5f, hz = roomSize.y * 0.5f;
            Transform walls = Group(parent, "Walls", roomCentre);
            Box(walls, "Wall north", new Vector3(0f, h * 0.5f, hz), new Vector3(roomSize.x + t, h, t), Wall);
            Box(walls, "Wall east", new Vector3(hx, h * 0.5f, 0f), new Vector3(t, h, roomSize.y + t), Wall);
            Box(walls, "Wall west", new Vector3(-hx, h * 0.5f, 0f), new Vector3(t, h, roomSize.y + t), Wall);
            float side = (roomSize.x - doorWidth) * 0.5f;
            Box(walls, "Wall south, west of the door", new Vector3(-(doorWidth * 0.5f + side * 0.5f), h * 0.5f, -hz), new Vector3(side + t, h, t), Wall);
            Box(walls, "Wall south, east of the door", new Vector3(doorWidth * 0.5f + side * 0.5f, h * 0.5f, -hz), new Vector3(side + t, h, t), Wall);
            Box(walls, "Over the door", new Vector3(0f, (h + 2.2f) * 0.5f, -hz), new Vector3(doorWidth, h - 2.2f, t), Wall);
            return roomCentre + new Vector3(0f, 0f, -hz);
        }

        // A door leaf hinged at the doorway's west edge, swinging into the room.
        public static SandboxDoor Door(Transform parent, Vector3 doorway, float width, string name = "Door")
        {
            Transform hinge = Group(parent, name, doorway + new Vector3(-width * 0.5f, 0f, 0f));
            GameObject leaf = Box(hinge, "Leaf", new Vector3(width * 0.5f, 1.05f, 0f), new Vector3(width - 0.04f, 2.1f, 0.05f), Shelf);
            var door = hinge.gameObject.AddComponent<SandboxDoor>();
            door.leaf = leaf.transform;
            door.openAngle = -95f;
            return door;
        }

        // A stock fridge you can stand inside: back, sides, top and base, stock on the back
        // wall, and a door. `front` is the middle of its opening on the floor; it faces `yaw`.
        public static HidingPlace StockFridge(Transform parent, Vector3 front, float yaw, string name = "Stock fridge")
        {
            Transform f = Group(parent, name, front, yaw);
            Vector3 inside = FridgeInside;
            float w = FridgeWall;
            // Local frame: the opening is at z = 0, the inside runs back to z = +depth.
            float d = inside.z;
            Box(f, "Back", new Vector3(0f, inside.y * 0.5f, d + w * 0.5f), new Vector3(inside.x + 2f * w, inside.y, w), Fridge);
            Box(f, "Side left", new Vector3(-(inside.x + w) * 0.5f, inside.y * 0.5f, d * 0.5f), new Vector3(w, inside.y, d + w), Fridge);
            Box(f, "Side right", new Vector3((inside.x + w) * 0.5f, inside.y * 0.5f, d * 0.5f), new Vector3(w, inside.y, d + w), Fridge);
            Box(f, "Top", new Vector3(0f, inside.y + w * 0.5f, d * 0.5f), new Vector3(inside.x + 2f * w, w, d + w), Fridge);
            // Stock on two shelves against the back: something to look at, not something in the way.
            for (int i = 0; i < 2; i++)
                Box(f, "Stock " + (i + 1), new Vector3(0f, 1.1f + i * 0.5f, d - 0.1f), new Vector3(inside.x - 0.1f, 0.25f, 0.16f), Stock);
            // The door, hinged on the left of the opening, swinging out.
            Transform hinge = Group(f, "Fridge door", new Vector3(-inside.x * 0.5f, 0f, -0.03f));
            GameObject leaf = Box(hinge, "Leaf", new Vector3(inside.x * 0.5f, inside.y * 0.5f, 0f), new Vector3(inside.x, inside.y - 0.02f, 0.05f), Fridge);
            var door = hinge.gameObject.AddComponent<SandboxDoor>();
            door.leaf = leaf.transform;
            door.openAngle = 100f;
            // The hiding space: the empty inside, from the floor up.
            var space = new GameObject(name + " inside");
            space.transform.SetParent(f, false);
            return HidingPlace.Author(space, new Bounds(new Vector3(0f, inside.y * 0.5f, d * 0.5f), new Vector3(inside.x, inside.y, d)));
        }

        // A stock rack: four posts, boards with boxes of stock, and room under the lowest board
        // to crouch in. `centre` is its middle on the floor; its length runs along local x.
        public static HidingPlace StockRack(Transform parent, Vector3 centre, float yaw, string name = "Stock rack")
        {
            Transform r = Group(parent, name, centre, yaw);
            float hx = RackLength * 0.5f - 0.03f, hz = RackDepth * 0.5f - 0.03f;
            foreach (Vector2 corner in new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(-hx, hz), new Vector2(hx, hz) })
                Box(r, "Post", new Vector3(corner.x, RackHeight * 0.5f, corner.y), new Vector3(0.05f, RackHeight, 0.05f), Rack);
            for (int i = 0; i < RackBoards.Length; i++)
            {
                float y = RackBoards[i];
                Box(r, "Board " + (i + 1), new Vector3(0f, y + RackBoard * 0.5f, 0f), new Vector3(RackLength, RackBoard, RackDepth), Rack);
                Box(r, "Stock on board " + (i + 1), new Vector3(0f, y + RackBoard + 0.15f, 0f), new Vector3(RackLength - 0.3f, 0.3f, RackDepth - 0.3f), Stock);
            }
            var space = new GameObject(name + " underneath");
            space.transform.SetParent(r, false);
            // From just above the floor to just under the lowest board, the rack's full length.
            float top = RackFirstBoard - 0.02f;
            return HidingPlace.Author(space, new Bounds(new Vector3(0f, top * 0.5f, 0f), new Vector3(RackLength, top, RackDepth)), crouch: true);
        }

        public static GameObject ShelfUnit(Transform parent, string name, Vector3 centre, float yaw, Vector3 size)
        {
            Transform s = Group(parent, name, centre, yaw);
            return Box(s, "Body", new Vector3(0f, size.y * 0.5f, 0f), size, Shelf);
        }

        // Ceiling lamps go under a root named "CeilingLights": that's where LightProbe, the light
        // sense her eye uses, looks for them.
        public static Transform CeilingLights(Transform parent) => Group(parent, "CeilingLights", Vector3.zero);

        public static Light Lamp(Transform ceilingLights, Vector3 at, float intensity = 2.2f, float range = 9f)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(ceilingLights, false);
            go.transform.localPosition = at;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = intensity;
            light.range = range;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft;
            return light;
        }

        // The NavMesh, built from the sandbox's own colliders. StoreRebake leaves out people and
        // stand-ins, as it does for the store's runtime layouts.
        public static NavMeshSurface Bake(GameObject root)
        {
            var surface = root.GetComponent<NavMeshSurface>();
            if (surface == null) surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            Physics.SyncTransforms();
            using (new StoreRebake(surface)) surface.BuildNavMesh();
            return surface;
        }
    }
}
