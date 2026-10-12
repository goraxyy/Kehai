using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// The back rooms, stocked with boxes of every product (ProductBox): brown cardboard with a
// band of the product's colour, its name on a label and its own packaging art beside it.
//
//   - The storage room: shelf-stable products on the racks, one section to a rack, with the
//     section's name on the rack's lowest board. Every board but the bottom row is filled (the
//     floor under the first board stays clear). Each product of a section gets a box on a board
//     the player can reach, a product with spare room gets a second one beside it, and the
//     board above repeats the box below it.
//   - The cold room (the room of fridges, which the scene calls "Fridges"): products that need
//     the cold (ProductDef.Storage), in white insulated boxes labelled on both sides. Frozen
//     food sits on the chest freezers, chilled food on the upright fridges.
//
// Every box has its spot, marked like the stock crate's: it glows when looked at, a yellow
// ghost shows the spot while the box is away, and E on the spot (or dropping the box) sends it
// back. Built when the store loads, from the rooms the scene has; nothing is baked into it.
public static class StorageBoxes
{
    public const string RootName = "Storage Boxes";
    const string RacksName = "Storageracks", RackPrefix = "StorageRack";
    const string ColdRoomName = "Fridges", FreezerPrefix = "Prop_Fridge_03";
    static readonly string[] FridgePrefixes = { "Prop_Fridge_02", "Prop_Fridge_04" };

    public static readonly Vector3 RackBox = new Vector3(1.8f, 0.85f, 0.9f);   // along, up, deep
    const float MinRackLength = 3.8f;   // room for two boxes
    const float LowestBoard = 0.3f;     // lower than this is the floor row, kept clear
    const float ReachTop = 3.4f;        // boards above this are out of the player's reach
    const float FrontInset = 0.15f;     // how far a box stands back from the rack's front edge
    const float BoxGap = 0.2f;

    static readonly Color Cardboard = new Color(0.62f, 0.45f, 0.28f);
    static readonly Color Insulated = new Color(0.9f, 0.94f, 0.97f);
    static readonly Color Paper = new Color(0.97f, 0.96f, 0.93f);
    static readonly Color SignColour = new Color(0.04f, 0.04f, 0.04f);
    static readonly Color Ink = new Color(0.08f, 0.08f, 0.08f);
    static readonly Color ColdInk = new Color(0.1f, 0.35f, 0.75f);
    static readonly Color Ghost = new Color(1f, 0.85f, 0.1f, 0.25f);

    public readonly struct Spot
    {
        public readonly Vector3 Position;      // the middle of the box
        public readonly Quaternion Rotation;   // +Z out of the front
        public readonly Vector3 Size;
        public readonly bool Reachable;
        public readonly int Column;            // boxes stacked over one another share it
        public readonly bool TwoFaced;

        public Spot(Vector3 position, Quaternion rotation, Vector3 size, bool reachable, int column, bool twoFaced)
        {
            Position = position; Rotation = rotation; Size = size;
            Reachable = reachable; Column = column; TwoFaced = twoFaced;
        }
    }

    // A rack's spots (in it, from the lowest board up) and where its section's name goes.
    public sealed class Rack
    {
        public readonly List<Spot> Spots = new List<Spot>();
        public Vector3 SignAt;
        public Quaternion SignFacing;
        public float SignWidth;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Build(SceneManager.GetActiveScene());
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode) => Build(scene);

    public static void Build(Scene scene)
    {
        if (!Application.isPlaying || !scene.IsValid() || !scene.isLoaded) return;
        foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == RootName) return;
        Transform racks = FindIn(scene, RacksName), cold = FindIn(scene, ColdRoomName);
        if (racks == null && cold == null) return;

        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var parts = new Parts();

        if (racks != null)
        {
            List<Rack> list = Racks(racks);
            List<ProductDef> products = ProductsKept(StorageKind.Ambient);
            ItemType?[] sections = SectionsFor(list, products);
            for (int r = 0; r < list.Count; r++)
            {
                if (sections[r] == null) continue;
                List<ProductDef> own = products.FindAll(p => p.Category == sections[r].Value);
                string[] ids = Assign(list[r].Spots, own, RackShare(list, sections, r));
                for (int i = 0; i < ids.Length; i++)
                    if (ids[i] != null) Make(ProductCatalog.Get(ids[i]), list[r].Spots[i], false, root.transform, parts);
                Sign(list[r], ProductCatalog.SectionName(sections[r].Value), root.transform, parts);
            }
        }
        if (cold != null)
        {
            List<ProductDef> products = ProductsKept(StorageKind.Cold);
            List<ProductDef> frozen = products.FindAll(p => p.Category == ItemType.Frozen);
            List<ProductDef> chilled = products.FindAll(p => p.Category != ItemType.Frozen);
            List<Spot> freezers = ColdSpots(cold, new[] { FreezerPrefix });
            List<Spot> fridges = ColdSpots(cold, FridgePrefixes);
            if (freezers.Count == 0 || fridges.Count == 0)
            {
                // Only one kind of unit: they share it, frozen first.
                List<Spot> all = freezers.Count > 0 ? freezers : fridges;
                int split = products.Count > 0 ? Mathf.Clamp(all.Count * frozen.Count / products.Count, frozen.Count > 0 ? 1 : 0, all.Count) : 0;
                freezers = all.GetRange(0, split);
                fridges = all.GetRange(split, all.Count - split);
            }
            Stock(freezers, frozen, root.transform, parts);
            Stock(fridges, chilled, root.transform, parts);
        }
    }

    static void Stock(List<Spot> spots, List<ProductDef> products, Transform root, Parts parts)
    {
        string[] ids = Assign(spots, products, (0, 1));
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] != null) Make(ProductCatalog.Get(ids[i]), spots[i], true, root, parts);
    }

    public static List<ProductDef> ProductsKept(StorageKind kind)
    {
        var list = new List<ProductDef>();
        foreach (ProductDef p in ProductCatalog.All)
            if (p.Storage == kind && ProductCatalog.IsStockSection(p.Category)) list.Add(p);
        return list;
    }

    // Which section each rack holds, racks taken in order: every section enough racks for all
    // its products to have a box in reach, then the spare racks to the sections with the most
    // products to a rack. A rack left over (more racks than needed) stays empty.
    public static ItemType?[] SectionsFor(IReadOnlyList<Rack> racks, IReadOnlyList<ProductDef> products)
    {
        var order = new List<ItemType>();
        var count = new Dictionary<ItemType, int>();
        foreach (ProductDef p in products)
        {
            if (!count.ContainsKey(p.Category)) { order.Add(p.Category); count[p.Category] = 0; }
            count[p.Category]++;
        }
        var result = new ItemType?[racks.Count];
        if (order.Count == 0 || racks.Count == 0) return result;

        int perRack = 0;
        foreach (Rack r in racks) foreach (Spot s in r.Spots) if (s.Reachable) perRack++;
        perRack = Mathf.Max(1, perRack / racks.Count);

        var given = new Dictionary<ItemType, int>();
        int needed = 0;
        foreach (ItemType section in order)
        {
            given[section] = Mathf.Max(1, Mathf.CeilToInt(count[section] / (float)perRack));
            needed += given[section];
        }
        // Too few racks: one each, as far as they go.
        if (needed > racks.Count) foreach (ItemType section in order) given[section] = 1;
        int spare = racks.Count - Sum(given);
        while (spare > 0)
        {
            ItemType most = order[0];
            foreach (ItemType section in order)
                if (count[section] / (float)given[section] > count[most] / (float)given[most]) most = section;
            given[most]++;
            spare--;
        }

        int rack = 0;
        foreach (ItemType section in order)
            for (int i = 0; i < given[section] && rack < racks.Count; i++) result[rack++] = section;
        return result;
    }

    static int Sum(Dictionary<ItemType, int> d) { int s = 0; foreach (int v in d.Values) s += v; return s; }

    // Which of its section's racks this one is, and how many there are: a section's products
    // run across all of its racks.
    static (int index, int of) RackShare(List<Rack> racks, ItemType?[] sections, int rack)
    {
        int index = 0, of = 0;
        for (int i = 0; i < racks.Count; i++)
            if (sections[i] == sections[rack]) { if (i < rack) index++; of++; }
        return (index, of);
    }

    // Which product each spot holds, for this share of a group's spots: the reachable spots take
    // the products in order, a product with spare room twice in a row; a spot out of reach
    // repeats the reachable box under it.
    public static string[] Assign(IReadOnlyList<Spot> spots, IReadOnlyList<ProductDef> products, (int index, int of) share)
    {
        var ids = new string[spots.Count];
        if (products.Count == 0 || spots.Count == 0) return ids;
        var reachable = new List<int>();
        for (int i = 0; i < spots.Count; i++) if (spots[i].Reachable) reachable.Add(i);
        if (reachable.Count == 0) for (int i = 0; i < spots.Count; i++) reachable.Add(i);

        // Spread the products over all the group's racks as if they were one long rack.
        int total = reachable.Count * share.of, offset = reachable.Count * share.index;
        var byColumn = new Dictionary<int, string>();
        for (int k = 0; k < reachable.Count; k++)
        {
            int at = offset + k;
            int index = total >= products.Count ? at * products.Count / total : at % products.Count;
            int spot = reachable[k];
            ids[spot] = products[index].Id;
            byColumn[spots[spot].Column] = ids[spot];   // the highest reachable box wins
        }
        for (int i = 0; i < spots.Count; i++)
            if (ids[i] == null)
                ids[i] = byColumn.TryGetValue(spots[i].Column, out string id) ? id : products[i % products.Count].Id;
        return ids;
    }

    // ---- the storage room ---------------------------------------------------------------------

    // Every rack long enough for two boxes, front rows first, and in each its boards with room
    // above them except the floor row. Racks open on the side away from their local +X (both
    // ways the scene turns them face their aisle that way).
    public static List<Rack> Racks(Transform racks)
    {
        var list = new List<Transform>();
        foreach (Transform t in racks) if (t.name.StartsWith(RackPrefix) && t.gameObject.activeInHierarchy) list.Add(t);
        list.Sort((a, b) =>
            Mathf.Abs(a.position.z - b.position.z) > 0.5f ? b.position.z.CompareTo(a.position.z) : a.position.x.CompareTo(b.position.x));

        var result = new List<Rack>();
        foreach (Transform t in list)
        {
            MeshFilter filter = t.GetComponentInChildren<MeshFilter>();
            Collider collider = t.GetComponentInChildren<Collider>();
            if (filter == null || filter.sharedMesh == null || collider == null) continue;

            Transform frame = filter.transform;
            Bounds local = filter.sharedMesh.bounds;
            bool alongZ = local.size.z >= local.size.x;
            Vector3 size = Vector3.Scale(local.size, Abs(frame.lossyScale));
            float length = alongZ ? size.z : size.x, depth = alongZ ? size.x : size.z;
            if (length < MinRackLength) continue;

            Vector3 along = frame.TransformDirection(alongZ ? Vector3.forward : Vector3.right).normalized;
            Vector3 front = -frame.TransformDirection(alongZ ? Vector3.right : Vector3.forward).normalized;
            Vector3 middle = frame.TransformPoint(local.center);
            float floor = middle.y - size.y * 0.5f, top = middle.y + size.y * 0.5f;

            int count = Mathf.Max(1, Mathf.FloorToInt((length + BoxGap) / (RackBox.x + BoxGap)));
            float spacing = length / count;
            Quaternion facing = Quaternion.LookRotation(front, Vector3.up);
            Vector3 probe = middle + front * (depth * 0.25f);

            var rack = new Rack();
            float lowest = float.MaxValue;
            foreach ((float board, float room) in Boards(collider, probe, floor, top))
            {
                if (board - floor < LowestBoard || room < 0.5f) continue;
                lowest = Mathf.Min(lowest, board);
                var box = new Vector3(RackBox.x, Mathf.Min(RackBox.y, room - 0.15f), RackBox.z);
                bool reach = board - floor <= ReachTop;
                for (int i = 0; i < count; i++)
                {
                    Vector3 at = middle + along * (-length * 0.5f + spacing * (i + 0.5f))
                               + front * (depth * 0.5f - FrontInset - box.z * 0.5f);
                    at.y = board + box.y * 0.5f + 0.005f;
                    rack.Spots.Add(new Spot(at, facing, box, reach, i, false));
                }
            }
            if (rack.Spots.Count == 0) continue;
            // The section's name runs along the front edge of the lowest board, under its boxes.
            Vector3 sign = middle + front * (depth * 0.5f + 0.02f);
            sign.y = lowest - 0.11f;
            rack.SignAt = sign;
            rack.SignFacing = facing;
            rack.SignWidth = Mathf.Min(2.4f, length * 0.5f);
            result.Add(rack);
        }
        return result;
    }

    // The tops of a rack's boards under a point, lowest first, and how much room each has
    // above it: rays down find each board's top in turn (a ray leaving a board inside it
    // passes out through its underside), and a ray up from each finds the board above.
    static List<(float board, float room)> Boards(Collider collider, Vector3 at, float floor, float top)
    {
        var tops = new List<float>();
        float y = top + 0.5f;
        for (int guard = 0; guard < 12 && y > floor; guard++)
        {
            if (!collider.Raycast(new Ray(new Vector3(at.x, y, at.z), Vector3.down), out RaycastHit hit, y - floor + 0.01f)) break;
            tops.Add(hit.point.y);
            y = hit.point.y - 0.02f;
        }
        var boards = new List<(float, float)>();
        foreach (float board in tops)
        {
            Vector3 from = new Vector3(at.x, board + 0.01f, at.z);
            float room = collider.Raycast(new Ray(from, Vector3.up), out RaycastHit above, top - board + 0.5f)
                ? above.point.y - board : top - board;
            boards.Add((board, room));
        }
        boards.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return boards;
    }

    // ---- the cold room ------------------------------------------------------------------------

    // A box on top of each unit whose name starts with one of `prefixes`, along its length.
    public static List<Spot> ColdSpots(Transform room, string[] prefixes)
    {
        var units = new List<Transform>();
        foreach (Transform t in room)
        {
            if (!t.gameObject.activeInHierarchy) continue;
            foreach (string prefix in prefixes) if (t.name.StartsWith(prefix)) { units.Add(t); break; }
        }
        units.Sort((a, b) =>
            Mathf.Abs(a.position.z - b.position.z) > 0.5f ? b.position.z.CompareTo(a.position.z) : a.position.x.CompareTo(b.position.x));

        var spots = new List<Spot>();
        foreach (Transform unit in units)
        {
            Renderer[] renderers = unit.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            bool alongX = b.size.x >= b.size.z;
            float length = alongX ? b.size.x : b.size.z, width = alongX ? b.size.z : b.size.x;
            var box = new Vector3(Mathf.Min(1.4f, length * 0.8f), 0.5f, Mathf.Min(0.7f, width * 0.72f));
            var at = new Vector3(b.center.x, b.max.y + box.y * 0.5f + 0.005f, b.center.z);
            Quaternion facing = Quaternion.LookRotation(alongX ? Vector3.forward : Vector3.right, Vector3.up);
            spots.Add(new Spot(at, facing, box, at.y - b.min.y <= ReachTop, spots.Count, true));
        }
        return spots;
    }

    // ---- a box ---------------------------------------------------------------------------------

    // The pieces every box shares: meshes, materials, and what the stock crate and its spot use
    // for their glow, their ghost and their knock.
    sealed class Parts
    {
        public readonly Mesh Cube, Quad;
        public readonly Material Cardboard, Insulated, Paper, Sign;
        public readonly Material Outline, Ghost;
        public readonly AudioClip Impact;
        public readonly int Layer;
        readonly Dictionary<Color, Material> colours = new Dictionary<Color, Material>();

        public Parts()
        {
            Cube = Primitive(PrimitiveType.Cube);
            Quad = Primitive(PrimitiveType.Quad);
            Cardboard = Lit(StorageBoxes.Cardboard, 0.08f);
            Insulated = Lit(StorageBoxes.Insulated, 0.35f);
            Paper = Lit(StorageBoxes.Paper, 0.05f);
            Sign = Lit(SignColour, 0.25f);
            int layer = LayerMask.NameToLayer("Interactable");
            Layer = layer >= 0 ? layer : 0;
            StockCrate crate = Object.FindAnyObjectByType<StockCrate>(FindObjectsInactive.Include);
            if (crate != null)
            {
                OutlineHighlight outline = crate.GetComponent<OutlineHighlight>();
                Outline = outline != null ? outline.outlineMaterial : null;
                Item item = crate.GetComponent<Item>();
                Impact = item != null ? item.impactSound : null;
                Layer = crate.gameObject.layer;
            }
            foreach (ToolSnapPoint snap in Object.FindObjectsByType<ToolSnapPoint>(FindObjectsInactive.Include))
            {
                if (snap.emptyMarker == null) continue;
                Renderer marker = snap.emptyMarker.GetComponentInChildren<Renderer>(true);
                if (marker != null && marker.sharedMaterial != null) { Ghost = marker.sharedMaterial; break; }
            }
            if (Ghost == null) Ghost = Lit(StorageBoxes.Ghost, 0f);
        }

        public Material Colour(Color c)
        {
            if (!colours.TryGetValue(c, out Material m)) colours[c] = m = Lit(c, 0.2f);
            return m;
        }

        static Mesh Primitive(PrimitiveType type)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return mesh;
        }

        static Material Lit(Color c, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }
    }

    static GameObject Make(ProductDef product, Spot spot, bool cold, Transform parent, Parts parts)
    {
        if (product == null) return null;
        var go = new GameObject("Box " + product.Id);
        go.SetActive(false);
        go.layer = parts.Layer;
        go.transform.SetPositionAndRotation(spot.Position, spot.Rotation);
        go.transform.SetParent(parent, true);
        Vector3 size = spot.Size;

        Part(go.transform, "Body", parts.Cube, cold ? parts.Insulated : parts.Cardboard, Vector3.zero, Quaternion.identity, size);
        Part(go.transform, "Band", parts.Cube, parts.Colour(product.Primary),
             new Vector3(0f, size.y * 0.34f, 0f), Quaternion.identity, new Vector3(size.x + 0.01f, size.y * 0.12f, size.z + 0.01f));
        Face(go.transform, product, size, 1f, parts);
        if (spot.TwoFaced) Face(go.transform, product, size, -1f, parts);

        var collider = go.AddComponent<BoxCollider>();
        collider.size = size;
        var body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.mass = cold ? 4f : 6f;
        var item = go.AddComponent<Item>();
        item.type = ItemType.Stock;
        item.impactSound = parts.Impact;
        if (parts.Outline != null)
        {
            var outline = go.AddComponent<OutlineHighlight>();
            outline.outlineMaterial = parts.Outline;
            outline.shape = OutlineHighlight.OutlineShape.BoundingBox;
        }
        go.AddComponent<PickupInteractable>();
        var home = go.AddComponent<ItemHome>();
        home.settleOnReturn = true;
        var box = go.AddComponent<ProductBox>();
        box.productId = product.Id;
        box.cold = cold;
        box.home = spot.Position;
        home.snapPoint = SnapPoint(item, spot, parent, parts).transform;
        go.SetActive(true);

        Words(go.transform, product, size, 1f, cold);
        if (spot.TwoFaced) Words(go.transform, product, size, -1f, cold);
        return go;
    }

    // The box's spot, like the stock crate's: a zone that glows when looked at and sends the box
    // back on E, and a yellow ghost of the box while it's away.
    static ToolSnapPoint SnapPoint(Item box, Spot spot, Transform parent, Parts parts)
    {
        var go = new GameObject("Spot " + box.name.Replace("Box ", ""));
        go.SetActive(false);
        go.layer = parts.Layer;
        go.transform.SetPositionAndRotation(spot.Position, spot.Rotation);
        go.transform.SetParent(parent, true);

        var zone = go.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = spot.Size;
        var outline = go.AddComponent<OutlineHighlight>();
        outline.outlineMaterial = parts.Outline;
        outline.shape = OutlineHighlight.OutlineShape.BoundingBox;

        var ghost = new GameObject("EmptyMarker");
        ghost.layer = parts.Layer;
        ghost.transform.SetParent(go.transform, false);
        ghost.transform.localScale = spot.Size * 0.98f;
        ghost.AddComponent<MeshFilter>().sharedMesh = parts.Cube;
        ghost.AddComponent<MeshRenderer>().sharedMaterial = parts.Ghost;
        ghost.SetActive(false);

        var snap = go.AddComponent<ToolSnapPoint>();
        snap.tool = box;
        snap.emptyMarker = ghost;
        go.SetActive(true);
        return snap;
    }

    static void Part(Transform box, string name, Mesh mesh, Material material, Vector3 at, Quaternion rotation, Vector3 scale)
    {
        var go = new GameObject(name);
        go.layer = box.gameObject.layer;
        go.transform.SetParent(box, false);
        go.transform.SetLocalPositionAndRotation(at, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // One face's printing: a paper label for the name on the left, the product's own packaging
    // art on the right (once its model is imported). `side` +1 is the front (+Z), -1 the back.
    static void Face(Transform box, ProductDef product, Vector3 size, float side, Parts parts)
    {
        float z = side * (size.z * 0.5f + 0.004f);
        Quaternion outward = side > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;   // a quad faces -Z
        float labelW = size.x * 0.55f, labelH = size.y * 0.5f;
        Part(box, "Label", parts.Quad, parts.Paper, new Vector3(-side * size.x * 0.17f, -size.y * 0.1f, z), outward, new Vector3(labelW, labelH, 1f));
        Material art = ArtOf(product);
        if (art != null)
        {
            float artSize = Mathf.Min(size.x * 0.28f, size.y * 0.55f);
            Part(box, "Art", parts.Quad, art, new Vector3(side * size.x * 0.3f, -size.y * 0.1f, z), outward, new Vector3(artSize, artSize, 1f));
        }
    }

    static Material ArtOf(ProductDef product)
    {
        ProductLook.Look? look = ProductLook.For(product.Id);
        if (look == null) return null;
        foreach (Material m in look.Value.Materials)
            if (m != null && m.name.StartsWith("L_")) return m;
        return null;
    }

    // The product's name, its section, and for the cold room how it's kept.
    static void Words(Transform box, ProductDef product, Vector3 size, float side, bool cold)
    {
        string kept = product.Category == ItemType.Frozen ? "FROZEN" : "KEEP COLD";
        string text = $"<b>{ProductBox.ShortName(product)}</b>\n<size=55%>{ProductCatalog.SectionName(product.Category).ToUpperInvariant()}" +
                      (cold ? $"  <color=#{ColorUtility.ToHtmlStringRGB(ColdInk)}>{kept}</color>" : "") + "</size>";
        Text(box, "Name", text, Ink, new Vector3(-side * size.x * 0.17f, -size.y * 0.1f, side * (size.z * 0.5f + 0.008f)),
             side > 0f ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity, new Vector2(size.x * 0.55f * 0.9f, size.y * 0.5f * 0.86f));
    }

    // A black strip with the rack's section on it, along the front of its lowest board.
    static void Sign(Rack rack, string section, Transform parent, Parts parts)
    {
        var go = new GameObject("Rack sign " + section);
        go.transform.SetPositionAndRotation(rack.SignAt, rack.SignFacing);
        go.transform.SetParent(parent, true);
        Part(go.transform, "Plate", parts.Cube, parts.Sign, Vector3.zero, Quaternion.identity, new Vector3(rack.SignWidth, 0.2f, 0.02f));
        Text(go.transform, "Section", $"<b>{section.ToUpperInvariant()}</b>", Color.white, new Vector3(0f, 0f, 0.012f),
             Quaternion.Euler(0f, 180f, 0f), new Vector2(rack.SignWidth * 0.94f, 0.17f));
    }

    static void Text(Transform parent, string name, string words, Color colour, Vector3 at, Quaternion rotation, Vector2 size)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(at, rotation);
        var text = go.AddComponent<TextMeshPro>();
        text.rectTransform.sizeDelta = size;
        text.text = words;
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.05f;
        text.fontSizeMax = 3f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = colour;
        text.textWrappingMode = TextWrappingModes.Normal;
    }

    // ---- helpers -------------------------------------------------------------------------------

    static Transform FindIn(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = Find(root.transform, name);
            if (found != null) return found;
        }
        return null;
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform child in t)
        {
            Transform found = Find(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
