using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class CustomerNPC : MonoBehaviour, IInteractable, IHoverable
{
    // Every shopper in the store — Aiko picks witnesses and puppets from this.
    static readonly List<CustomerNPC> all = new List<CustomerNPC>();
    public static IReadOnlyList<CustomerNPC> All => all;

    // Shoppers are plain soft orange: one material for all of them, made here rather than kept
    // as an asset. The face keeps its own.
    public static readonly Color BodyColour = new Color(1f, 0.68f, 0.42f);
    static Material body;

    [Header("Route (leave empty if CustomerSpawner will assign these)")]
    public Transform[] shelfPoints;
    public Transform cashierPoint;
    public Transform exitPoint;

    [Header("Shopping Behaviour")]
    [Tooltip("How many things are on a shopping list (or, with no planogram, shelves visited).")]
    public Vector2Int shelvesToVisitRange = new Vector2Int(2, 4);
    public Vector2 shelfStayDurationRange = new Vector2(3f, 8f);

    [Range(0f, 1f)]
    [Tooltip("Odds of grabbing something from the sweets by the till while queueing.")]
    public float impulseChance = 0.35f;

    [Header("Mess & Trash")]
    public GameObject dirtPrefab;
    [Range(0f, 1f)] public float dirtChance = 0.5f;
    [Range(0f, 1f)] public float trashcanVisitChance = 0.5f;
    public float trashcanUseSeconds = 3f;
    public float trashcanStandOffset = 1f;

    [Header("Taking Items")]
    public Transform carryPoint;                 // items hover here, in front of the customer
    public float shelfReachRadius = 2.5f;        // how far they can reach for a stocked slot
    public float carryStackSpacing = 0.42f;      // vertical gap between carried items
    public int maxCarriedItems = 3;

    [Header("Cashier")]
    public bool waitForPlayerToServe = true;              // wait at the desk until the player presses E
    public Vector2 cashierWaitDurationRange = new Vector2(4f, 10f); // used only when waitForPlayerToServe is false
    public float faceTurnSpeed = 6f;

    [Tooltip("Register beep when the player serves this customer.")]
    public AudioClip serveSound;
    [Range(0f, 1f)] public float serveVolume = 0.9f;

    [Header("Movement")]
    public NavMeshAgent agent;
    public float arriveDistance = 0.3f;
    public float stuckTimeout = 20f; // safety net so a blocked NPC doesn't wait forever at one destination

    // True while standing at the desk waiting to be served by the player.
    public bool IsWaitingToBeServed
    {
        get => isWaitingToBeServed;
        private set
        {
            if (isWaitingToBeServed == value) return;
            isWaitingToBeServed = value;
            WaitingCount = Mathf.Max(0, WaitingCount + (value ? 1 : -1));
            TaskManager.NotifyWorldChanged();
        }
    }

    // What the shopper is up to, for the map and the shift recording.
    public enum Activity { Shopping, UsingBin, HeadingToTill, Queueing, Leaving, LookingAround }
    public Activity CurrentActivity { get; private set; } = Activity.Shopping;

    // When they started waiting at the till (Time.time), or -1.
    public float QueueingSince { get; private set; } = -1f;

    // The player, found once at spawn. Read by CustomerRequest while talking and escorting.
    public Transform PlayerTransform => player;

    // How many customers are queued at the till right now — the shift can't be
    // closed while anyone is still waiting to be served.
    public static int WaitingCount { get; private set; }

    bool isWaitingToBeServed;

    // What this shopper came in for, in the order they'll walk to it.
    readonly List<ProductDef> shoppingList = new List<ProductDef>();
    public IReadOnlyList<ProductDef> ShoppingListItems => shoppingList;

    // Nothing shops in the dark: a blackout parks every customer where they stand and
    // stops their timers until the breakers go back on.
    bool isFrozen;

    Action onDespawn;
    OutlineHighlight outline;
    CustomerRequest request;
    Transform player;
    bool served;
    bool hovered;
    bool forcedHighlight;
    readonly List<Item> basket = new List<Item>();

    // Gives a shopper (or a copy of one, as replays make) its body colour.
    public static GameObject Dress(GameObject shopper)
    {
        if (shopper == null || !shopper.TryGetComponent(out MeshRenderer renderer)) return shopper;
        if (body == null)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            body = new Material(lit) { name = "Shopper", color = BodyColour };
            if (body.HasProperty("_BaseColor")) body.SetColor("_BaseColor", BodyColour);
            if (body.HasProperty("_Smoothness")) body.SetFloat("_Smoothness", 0.15f);
        }
        renderer.sharedMaterial = body;
        return shopper;
    }

    void Awake()
    {
        Dress(gameObject);

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        outline = GetComponent<OutlineHighlight>();
        request = GetComponent<CustomerRequest>();

        // Every shopper remembers the last time it saw the employee (Aiko.md §3.4).
        if (GetComponent<Kehai.Aiko.CustomerMemory>() == null)
            gameObject.AddComponent<Kehai.Aiko.CustomerMemory>();

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
            player = playerObject.transform;
    }

    void Start()
    {
        StartCoroutine(RunRoutine());
    }

    void OnEnable()
    {
        all.Add(this);
        PowerSystem.PowerChanged += OnPowerChanged;
        ApplyFreeze(!PowerSystem.PowerOn);
    }

    void OnDisable()
    {
        all.Remove(this);
        PowerSystem.PowerChanged -= OnPowerChanged;
    }

    void OnPowerChanged(bool powered) => ApplyFreeze(!powered);

    void ApplyFreeze(bool frozen)
    {
        isFrozen = frozen;
        if (agent != null && agent.isOnNavMesh) agent.isStopped = frozen;
    }

    // WaitForSeconds keeps counting through a blackout; this doesn't, so a shopper
    // picks its routine up exactly where it left off when the lights return.
    IEnumerator Wait(float seconds)
    {
        float left = seconds;
        while (left > 0f)
        {
            if (!isFrozen) left -= Time.deltaTime;
            yield return null;
        }
    }

    // Called by CustomerSpawner right after Instantiate to hand over this run's route.
    public void Init(Transform[] shelves, Transform cashier, Transform exit, Action despawnCallback)
    {
        shelfPoints = shelves;
        cashierPoint = cashier;
        exitPoint = exit;
        onDespawn = despawnCallback;
    }

    IEnumerator RunRoutine()
    {
        if (cashierPoint == null || exitPoint == null || shelfPoints == null || shelfPoints.Length == 0)
        {
            Debug.LogWarning("CustomerNPC is missing route points, despawning.");
            Despawn();
            yield break;
        }

        // A stocked shop is shopped from a list; without a planogram (a test scene, an old
        // layout) the shopper just browses shelves.
        if (Planogram.HasStock) yield return ShopFromList();
        else yield return BrowseRandomShelves();

        CurrentActivity = Activity.HeadingToTill;
        yield return MoveTo(cashierPoint.position);

        // The sweets by the till are there for exactly this.
        if (UnityEngine.Random.value < impulseChance) TakeImpulseBuy();

        CurrentActivity = Activity.Queueing;
        yield return WaitAtCashier();

        CurrentActivity = Activity.Leaving;
        yield return MoveTo(exitPoint.position);

        Despawn();
    }

    // Walks the list in store order: to the nearest facing of each product, takes one, and
    // moves on. An empty facing is when people ask; so, sometimes, is the next thing on the list.
    IEnumerator ShopFromList()
    {
        int count = UnityEngine.Random.Range(shelvesToVisitRange.x, shelvesToVisitRange.y + 1);
        shoppingList.Clear();
        shoppingList.AddRange(ShoppingList.Pick(count, new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue))));

        bool willUseBin = UnityEngine.Random.value <= trashcanVisitChance;
        int binAfter = willUseBin && shoppingList.Count > 0 ? UnityEngine.Random.Range(0, shoppingList.Count) : -1;

        for (int i = 0; i < shoppingList.Count; i++)
        {
            ProductDef want = shoppingList[i];
            ShelfSlot target = Planogram.Nearest(want.Id, transform.position, mustBeFilled: false);
            if (target == null) continue;

            yield return MoveTo(StandPoint(target));

            float browseTime = UnityEngine.Random.Range(shelfStayDurationRange.x, shelfStayDurationRange.y);
            yield return Wait(browseTime * 0.5f);

            bool got = TakeProduct(want.Id);
            if (!got && request != null)
            {
                // The facing is bare. Ask, and if they're shown another, take one there.
                ShelfSlot elsewhere = Planogram.Nearest(want.Id, transform.position, mustBeFilled: true, except: target);
                if (elsewhere != null)
                {
                    yield return request.RunFor(want, elsewhere, missing: true);
                    if (request.Helped)
                    {
                        yield return MoveTo(StandPoint(elsewhere));
                        TakeProduct(want.Id);
                    }
                }
            }
            else if (got && request != null && i + 1 < shoppingList.Count)
            {
                // Some shoppers can't find the next thing on their list and stop to ask. The
                // request owns the customer until it is resolved, so shopping waits here.
                // Only when it's out of sight, though: nobody asks for the shelf beside them.
                ProductDef next = shoppingList[i + 1];
                ShelfSlot nextTarget = Planogram.Nearest(next.Id, transform.position, mustBeFilled: true);
                if (nextTarget != null && (StandPoint(nextTarget) - transform.position).sqrMagnitude > AskBeyond * AskBeyond)
                    yield return request.RunFor(next, nextTarget, missing: false);
            }

            yield return Wait(browseTime * 0.5f);

            if (i == binAfter)
            {
                CurrentActivity = Activity.UsingBin;
                yield return VisitTrashcan();
                CurrentActivity = Activity.Shopping;
            }
        }
    }

    // The old way round the shop: a few random shelves, whatever is nearest on each.
    IEnumerator BrowseRandomShelves()
    {
        // Some shoppers get the urge to bin something partway round the store.
        List<Transform> route = PickRandomShelves();
        bool willUseBin = UnityEngine.Random.value <= trashcanVisitChance;
        int binAfterShelf = willUseBin && route.Count > 0
            ? UnityEngine.Random.Range(0, route.Count)
            : -1;

        for (int i = 0; i < route.Count; i++)
        {
            yield return MoveTo(route[i].position);

            // Browse for a moment, then take something off the shelf.
            float browseTime = UnityEngine.Random.Range(shelfStayDurationRange.x, shelfStayDurationRange.y);
            yield return Wait(browseTime * 0.5f);

            TakeItemFromNearbyShelf();

            if (request != null)
                yield return request.Run();

            yield return Wait(browseTime * 0.5f);

            if (i == binAfterShelf)
            {
                CurrentActivity = Activity.UsingBin;
                yield return VisitTrashcan();
                CurrentActivity = Activity.Shopping;
            }
        }
    }

    // Further than this and the next thing on the list is worth asking about.
    const float AskBeyond = 8f;

    // Where to stand to take something off a facing: the nearest walkable spot, which is the
    // aisle in front of it.
    static Vector3 StandPoint(ShelfSlot slot)
    {
        Vector3 p = slot.Position;
        return NavMesh.SamplePosition(p, out NavMeshHit hit, 2.5f, NavMesh.AllAreas) ? hit.position : p;
    }

    IEnumerator WaitAtCashier()
    {
        if (!waitForPlayerToServe)
        {
            yield return Wait(UnityEngine.Random.Range(cashierWaitDurationRange.x, cashierWaitDurationRange.y));
            yield break;
        }

        served = false;
        IsWaitingToBeServed = true;
        QueueingSince = Time.time;

        // Stand still and look toward the player while queueing to be served.
        while (!served)
        {
            FacePlayer();
            yield return null;
        }

        IsWaitingToBeServed = false;
        QueueingSince = -1f;
    }

    void FacePlayer()
    {
        if (player == null) return;
        FaceTowards(player.position);
    }

    // Turn to look at a point, smoothly and without tipping over.
    public void FaceTowards(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion target = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * faceTurnSpeed);
    }

    // Mid-shop, some customers wander to the nearest bin and use it.
    // The caller decides whether this happens; this just walks there and does it.
    IEnumerator VisitTrashcan()
    {
        Trashcan can = FindNearestTrashcan();
        if (can == null) yield break;

        // Stand in front of it rather than inside it.
        Vector3 approach = can.transform.position
                         + (transform.position - can.transform.position).normalized * trashcanStandOffset;
        if (!NavMesh.SamplePosition(approach, out NavMeshHit spot, 2f, NavMesh.AllAreas)) yield break;

        // The bin sits out the back — only go if there is a real walkable route to it,
        // otherwise the customer would stand against a wall and use it through the wall.
        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(spot.position, path) || path.status != NavMeshPathStatus.PathComplete)
            yield break;

        yield return MoveTo(spot.position);

        // Confirm we actually got there before counting it as a use. Distance alone isn't
        // enough — the bin sits against a wall, and a customer on the far side can easily be
        // within a couple of metres of it, so require clear line of sight as well.
        if (Vector3.Distance(transform.position, can.transform.position) > trashcanStandOffset + 1.5f)
            yield break;
        if (!HasLineOfSight(can.transform))
            yield break;

        yield return Wait(trashcanUseSeconds);

        if (can != null) can.RegisterUse();
    }

    // True when nothing solid sits between this customer and the target.
    bool HasLineOfSight(Transform target)
    {
        Vector3 from = transform.position + Vector3.up * 0.6f;
        Vector3 to = target.position + Vector3.up * 0.4f;
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < 0.01f) return true;

        foreach (RaycastHit hit in Physics.RaycastAll(from, direction / distance, distance))
        {
            if (hit.collider.isTrigger) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;   // ourselves
            if (hit.collider.transform.IsChildOf(target)) continue;      // the bin itself
            if (target.IsChildOf(hit.collider.transform)) continue;      // a parent of the bin
            return false;                                               // something is in the way
        }

        return true;
    }

    Trashcan FindNearestTrashcan()
    {
        var cans = Trashcan.All;
        Trashcan nearest = null;
        float nearestSqr = float.MaxValue;

        for (int i = 0; i < cans.Count; i++)
        {
            float sqr = (transform.position - cans[i].transform.position).sqrMagnitude;
            if (sqr < nearestSqr) { nearest = cans[i]; nearestSqr = sqr; }
        }

        return nearest;
    }

    // Customers are messy: dirtChance of the time, taking something leaves a patch behind.
    // That roll is the only thing gating it — there is no ceiling on spills underfoot.
    void DropDirt()
    {
        if (dirtPrefab == null) return;
        if (UnityEngine.Random.value > dirtChance) return;

        Vector3 position = transform.position;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
            position = hit.position;

        Instantiate(dirtPrefab, position + Vector3.up * 0.02f, Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f));
    }

    // Takes one item from the nearest stocked slot within reach. The item leaves the shelf
    // (so the slot needs restocking) and hovers in front of the customer from then on.
    void TakeItemFromNearbyShelf() => TakeFrom(FindNearestStockedSlot());

    // One of a particular product, from a facing of it within reach. False if there's none.
    bool TakeProduct(string productId) => TakeFrom(FindNearestStockedSlot(s => s.productId == productId));

    // Something from the sweets by the till.
    void TakeImpulseBuy() => TakeFrom(FindNearestStockedSlot(s => s.requiredType == ItemType.Confectionery));

    bool TakeFrom(ShelfSlot slot)
    {
        if (slot == null || basket.Count >= maxCarriedItems) return false;

        Item taken = slot.TakeItem();
        if (taken == null) return false;

        Transform holder = carryPoint != null ? carryPoint : transform;
        taken.SetCarried(true, holder);

        // Stacked one on another, each by its own height. Nothing re-applies this every frame
        // any more, so the offset sticks.
        float y = 0f;
        foreach (Item held in basket)
            if (held != null) y += Mathf.Min(held.RestHeight * 2f, carryStackSpacing) + 0.02f;
        taken.transform.localPosition = new Vector3(0f, y + taken.RestHeight - Item.SnapHeight, 0f);
        taken.transform.localRotation = Quaternion.identity;

        basket.Add(taken);

        DropDirt();
        return true;
    }

    // The closest stocked slot within reach, from the shop's grid of slots rather than all
    // 16,500 of them.
    ShelfSlot FindNearestStockedSlot(System.Predicate<ShelfSlot> wanted = null)
    {
        ShelfStock.Current.Near(transform.position, shelfReachRadius, nearby);
        ShelfSlot nearest = null;
        float nearestSqr = float.MaxValue;
        Vector3 position = transform.position;

        foreach (ShelfSlot slot in nearby)
        {
            if (!slot.isFilled) continue;
            if (wanted != null && !wanted(slot)) continue;

            float sqr = (position - slot.Position).sqrMagnitude;
            if (sqr >= nearestSqr) continue;

            // Being close isn't enough — a shelf on the far side of a wall is metres away
            // but not actually reachable.
            if (!CanReach(slot)) continue;

            nearest = slot;
            nearestSqr = sqr;
        }

        return nearest;
    }

    readonly List<ShelfSlot> nearby = new List<ShelfSlot>();

    // True when nothing but this slot's own shelf sits between the customer and the slot.
    bool CanReach(ShelfSlot slot)
    {
        Transform shelf = slot.owner != null ? slot.owner.transform : slot.frame.root;

        Vector3 from = transform.position + Vector3.up * 0.8f;
        Vector3 to = slot.Position + Vector3.up * (slot.height * 0.5f);
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < 0.05f) return true;

        foreach (RaycastHit hit in Physics.RaycastAll(from, direction / distance, distance))
        {
            if (hit.collider.isTrigger) continue;                          // triggers block nothing
            if (hit.collider.transform.IsChildOf(transform)) continue;     // ourselves
            if (hit.collider.transform.IsChildOf(shelf)) continue;         // the shelf we're reaching into
            return false;                                                  // a wall or another fixture
        }

        return true;
    }

    List<Transform> PickRandomShelves()
    {
        List<Transform> pool = new List<Transform>(shelfPoints);
        int count = Mathf.Min(UnityEngine.Random.Range(shelvesToVisitRange.x, shelvesToVisitRange.y + 1), pool.Count);

        List<Transform> picked = new List<Transform>(count);
        for (int i = 0; i < count; i++)
        {
            int index = UnityEngine.Random.Range(0, pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return picked;
    }

    IEnumerator MoveTo(Vector3 destination)
    {
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"{name} is not on the NavMesh, despawning.", this);
            Despawn();
            yield break;
        }

        // Route points are authored by hand (ShelfPoint sits in front of each shelf), so snap
        // to the nearest reachable spot rather than failing on a slightly off-mesh target.
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            destination = hit.position;

        agent.SetDestination(destination);

        float elapsed = 0f;
        while (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + arriveDistance)
        {
            // Standing still in a blackout isn't being stuck, so the timer holds too.
            if (!isFrozen)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= stuckTimeout) break;
            }

            yield return null;
        }
    }

    // --- Player interaction -------------------------------------------------

    public void Interact(PlayerInteract player)
    {
        // A customer asking for directions answers E before the till does.
        if (request != null && request.TryTalk()) return;

        if (!IsWaitingToBeServed) return;

        served = true;
        OneShotAudio.PlayAt(serveSound, transform.position, serveVolume);
        Kehai.Aiko.NoiseBus.Emit(transform.position, 0.4f, Kehai.Aiko.NoiseKind.Serve, Kehai.Aiko.NoiseAuthor.Player);
        GameEvents.RaiseCustomerServed(this);
    }

    public string GetPrompt()
    {
        if (request != null)
        {
            string asking = request.GetPrompt();
            if (!string.IsNullOrEmpty(asking)) return asking;
        }

        return IsWaitingToBeServed ? "Serve customer" : string.Empty;
    }

    public void OnHoverEnter()
    {
        hovered = true;
        ApplyHighlight();
    }

    public void OnHoverExit()
    {
        hovered = false;
        ApplyHighlight();
    }

    // Held on while the customer is waiting for help, so looking away doesn't clear it.
    public void SetForcedHighlight(bool on)
    {
        forcedHighlight = on;
        ApplyHighlight();
    }

    void ApplyHighlight()
    {
        if (outline != null)
            outline.SetHighlighted(hovered || forcedHighlight);
    }

    // ---- Aiko's hooks (Aiko.md §8.5) ------------------------------------------------

    // A possessed shopper never queues; take it out of the till count if it was in it.
    public void ReleaseQueueSpot() => IsWaitingToBeServed = false;

    // Stop what it was doing and walk out of the store.
    public void LeaveStore()
    {
        StopAllCoroutines();
        IsWaitingToBeServed = false;
        StartCoroutine(Leave());
    }

    IEnumerator Leave()
    {
        CurrentActivity = Activity.Leaving;
        QueueingSince = -1f;
        if (exitPoint != null) yield return MoveTo(exitPoint.position);
        Despawn();
    }

    // Stops dead for a moment — the tell before she takes a shopper over.
    public void Freeze(float seconds) => StartCoroutine(FreezeFor(seconds));

    IEnumerator FreezeFor(float seconds)
    {
        if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        yield return new WaitForSeconds(seconds);
        if (agent != null && agent.isOnNavMesh && !isFrozen) agent.isStopped = false;
    }

    // The witness: walk to a spot, look around, then carry on out of the store.
    public void SendToLook(Vector3 point, float seconds)
    {
        StopAllCoroutines();
        IsWaitingToBeServed = false;
        StartCoroutine(Look(point, seconds));
    }

    IEnumerator Look(Vector3 point, float seconds)
    {
        CurrentActivity = Activity.LookingAround;
        QueueingSince = -1f;
        yield return MoveTo(point);
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            transform.Rotate(Vector3.up, 60f * Time.deltaTime);
            yield return null;
        }
        if (exitPoint != null) yield return MoveTo(exitPoint.position);
        Despawn();
    }

    void Despawn()
    {
        onDespawn?.Invoke();
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        // Don't leave a destroyed customer counted as still waiting.
        IsWaitingToBeServed = false;
    }
}
