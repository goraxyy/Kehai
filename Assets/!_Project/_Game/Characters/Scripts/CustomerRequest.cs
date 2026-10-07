using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Some shoppers can't find what they came for. A customer may stop, light up, and wait to be
// asked what's wrong. Talk to them and they ask for the next thing on their list — by name, by
// aisle, by department, or because the shelf they found was empty (CustomerQuestion has the
// lines); agree to help and they follow you until you walk them to the right shelf, which is
// marked with a beacon. Asked for an aisle or a department, they're happy once you've got them
// into it, and find the shelf themselves from there.
//
// The whole thing runs as one coroutine owned by CustomerNPC's routine, so while a request
// is live the customer's normal shopping is simply paused.
[RequireComponent(typeof(CustomerNPC))]
public class CustomerRequest : MonoBehaviour
{
    public enum Stage { None, Asking, Talking, Escorting, Returning }

    [Header("Chance")]
    [Range(0f, 1f)]
    [Tooltip("Odds of asking for help with the next thing on the list.")]
    public float askChance = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Odds of asking when the shelf they walked to is empty.")]
    public float missingAskChance = 0.85f;

    [Tooltip("Give up and carry on shopping after this long unattended. 0 waits forever.")]
    public float patienceSeconds = 120f;

    [Header("Talking")]
    [Tooltip("How far the player can stand and still hold the conversation.")]
    public float talkRange = 5f;

    [Tooltip("Height of the bottom edge of the bubble above the floor. It sits down by " +
             "the customer's shins and grows upward from there.")]
    public float bubbleHeight = 0.25f;

    [Tooltip("How far in front of the customer's legs the bubble hangs.")]
    public float bubbleForward = 0.4f;

    [Tooltip("{0} is the product. Both a named line - \"Pipisi Zero\" - and a loose one " +
             "- \"a tin of Tunatastic\" - drop into these, so they all read as object " +
             "phrases rather than \"the {0}\".")]
    public string[] questionTemplates =
    {
        "Excuse me - I'm looking for {0}.",
        "Sorry, I can't find {0} anywhere.",
        "Hi! Do you still have {0}?",
        "Excuse me - where do you keep {0}?",
        "I've been round twice and I still can't see {0}."
    };

    [Range(0f, 1f)]
    [Tooltip("How often they ask for a whole section - \"where's the milk?\" - instead of " +
             "naming the product they came in for.")]
    public float sectionQuestionChance = 0.2f;

    public string acceptLabel = "Follow me";
    public string declineLabel = "Sorry, I'm busy";
    [Tooltip("{0} is the product, so they can thank you for the right thing.")]
    public string thanksLine = "Oh, {0}. There it is - thanks!";
    public string declineLine = "...right. Thanks anyway.";
    public string followingLine = "Right behind you.";
    public string strandedLine = "Hey - where did you go?";

    [Header("Escort")]
    [Tooltip("The player has to stay inside this circle or the customer turns back.")]
    public float followRadius = 7f;

    [Tooltip("How far behind the player the customer trails.")]
    public float followStandoff = 1.8f;

    [Tooltip("Size of the spot in front of the shelf the customer has to walk into.")]
    public float arriveRadius = 1.6f;

    [Header("Markers")]
    public Color radiusColour = new Color(1f, 0.84f, 0.1f, 0.30f);
    public Color targetColour = new Color(0.35f, 1f, 0.45f, 0.35f);

    [Tooltip("The cone over the shelf they are looking for.")]
    public Color shelfConeColour = new Color(0.25f, 1f, 0.40f, 0.95f);
    public float shelfConeSize = 0.35f;

    [Tooltip("The cone over a customer who is waiting to be spoken to.")]
    public Color customerConeColour = new Color(1f, 0.84f, 0.1f, 0.95f);
    public float customerConeSize = 0.28f;
    public float customerConeHeight = 2.25f;

    // How many customers are waiting on an answer right now — the directions task reads this.
    public static int PendingCount { get; private set; }

    // Statics outlive a play-mode restart when domain reloading is off, and a count left
    // over from the last run would block clocking out forever. Start every run at zero.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCounters() => PendingCount = 0;

    public Stage CurrentStage { get; private set; }

    // Where they want to go. The player sees it as the green cone over the shelf; an agent
    // in the eval harness reads it here.
    public Vector3 Destination => destination;
    public string Wanted => wanted;
    public ShelfUnit DestinationShelf => destinationShelf;

    // The product behind the question, and how it was asked. Null for the old free-form ask.
    public ProductDef WantedProduct => wantedProduct;
    public CustomerQuestion.Kind Kind => kind;
    public string WantedAisle => wantedProduct != null ? wantedZone.Sign : null;

    // Whether the last request ended with them where they wanted to be.
    public bool Helped { get; private set; }

    // 0 = "Follow me", 1 = decline. Consumed by the conversation loop on its next frame.
    [System.NonSerialized] public int externalChoice = -1;

    CustomerNPC npc;
    NavMeshAgent agent;
    SpeechBubble bubble;
    GuideMarker askMarker;
    GuideMarker radiusRing;
    GuideMarker targetRing;
    GuideMarker beacon;

    Vector3 homePosition;
    Vector3 destination;
    ShelfUnit destinationShelf;
    ProductDef wantedProduct;
    StoreLayout.Zone wantedZone;
    CustomerQuestion.Kind kind;
    string question;
    string wanted;
    string thanks;
    string[] options;
    bool talkRequested;
    bool counted;
    float originalStoppingDistance;

    void Awake()
    {
        npc = GetComponent<CustomerNPC>();
        agent = npc.agent != null ? npc.agent : GetComponent<NavMeshAgent>();
    }

    // --- entry point, driven by CustomerNPC.RunRoutine ----------------------

    // The free-form ask: a random shelf somewhere else in the store. Used when the shop has
    // no planogram to shop from.
    public IEnumerator Run()
    {
        Helped = false;
        if (!ShouldAsk(askChance)) yield break;
        if (!PickDestination()) yield break;

        yield return Converse();
    }

    // Asks for one product off the customer's list, to be walked to `target`, a facing that
    // stocks it. `missing` is the empty-shelf case.
    public IEnumerator RunFor(ProductDef product, ShelfSlot target, bool missing)
    {
        Helped = false;
        if (product == null || target == null) yield break;
        if (!ShouldAsk(missing ? missingAskChance : askChance)) yield break;
        if (!AimAt(target)) yield break;

        wantedProduct = product;
        wantedZone = StoreLayout.ZoneAt(target.transform.position);
        kind = CustomerQuestion.Choose(Random.value, missing, wantedZone);
        // Nobody stands in aisle 3 asking where aisle 3 is.
        if (kind != CustomerQuestion.Kind.Missing && wantedZone.Contains(transform.position))
            kind = CustomerQuestion.Kind.Product;
        wanted = CustomerQuestion.Wanted(kind, product, wantedZone);
        question = CustomerQuestion.Ask(kind, product, wantedZone, Random.Range(0, 1000));
        thanks = CustomerQuestion.Thanks(kind, product, wantedZone);

        yield return Converse();
    }

    IEnumerator Converse()
    {
        Begin();

        // 1. Stand still and wait to be spoken to.
        yield return WaitToBeAsked();
        if (!talkRequested) { Finish(strandedLine, 0f); yield break; }

        // 2. The conversation: two options, arrows to move, E to pick.
        ClearAskMarker();
        SetStage(Stage.Talking);
        yield return null;                 // don't let the E that opened this also confirm it

        int selected = 0;
        bool confirmed = false;
        bool abandoned = false;

        while (!confirmed && !abandoned)
        {
            Transform talker = npc.PlayerTransform;
            if (talker == null) { abandoned = true; break; }

            npc.FaceTowards(talker.position);

            // Arrows only — W/S are movement keys and would pick an option while walking.
            if (Pressed(KeyCode.UpArrow)) selected = 0;
            if (Pressed(KeyCode.DownArrow)) selected = 1;
            if (Pressed(KeyCode.E) || Pressed(KeyCode.Return)) confirmed = true;

            // An agent answering without a keyboard (eval harness, simulated players).
            if (externalChoice >= 0)
            {
                selected = externalChoice;
                confirmed = true;
                externalChoice = -1;
            }
            if (!PlayerWithin(talkRange * 1.6f)) abandoned = true;

            bubble.ShowChoices(question, options, selected);
            yield return null;
        }

        if (abandoned || selected != 0)
        {
            Finish(declineLine, 1.2f);
            yield break;
        }

        // 3. The escort. The customer trails the player while they stay inside the ring,
        //    and turns back toward where it asked the moment they leave it. Nothing is
        //    reset by wandering off — walk back into the circle and it picks up again.
        yield return Escort();
        Helped = AtDestination();
        GameEvents.RaiseDirectionsGiven(npc);

        string parting = !string.IsNullOrEmpty(thanks) ? thanks
                       : thanksLine.Contains("{0}") ? string.Format(thanksLine, wanted) : thanksLine;
        Finish(parting, 1.6f);
    }

    IEnumerator WaitToBeAsked()
    {
        float waited = 0f;

        while (!talkRequested)
        {
            if (PlayerWithin(talkRange))
                npc.FaceTowards(npc.PlayerTransform.position);

            waited += Time.deltaTime;
            if (patienceSeconds > 0f && waited >= patienceSeconds) yield break;

            yield return null;
        }
    }

    IEnumerator Escort()
    {
        SetStage(Stage.Escorting);

        originalStoppingDistance = agent.stoppingDistance;
        agent.stoppingDistance = followStandoff;
        agent.isStopped = !PowerSystem.PowerOn;   // don't walk off mid-blackout

        ShowEscortMarkers();

        while (!AtDestination())
        {
            Transform player = npc.PlayerTransform;
            if (player == null) break;

            bool inRange = Vector3.Distance(transform.position, player.position) <= followRadius;

            if (inRange)
            {
                if (CurrentStage != Stage.Escorting)
                {
                    SetStage(Stage.Escorting);
                    bubble.Show(followingLine);
                }
                agent.SetDestination(player.position);
            }
            else
            {
                // Out of range: head back to where the question was asked. Progress toward
                // the shelf isn't lost — the destination and its marker stay put.
                if (CurrentStage != Stage.Returning)
                {
                    SetStage(Stage.Returning);
                    bubble.Show(strandedLine);
                }
                agent.SetDestination(homePosition);
            }

            yield return null;
        }

        agent.stoppingDistance = originalStoppingDistance;
    }

    // The task is only done once the customer itself is standing in the spot in front
    // of the shelf — walking there alone doesn't count.
    bool AtDestination()
    {
        // Asked for an aisle or a department, anywhere inside it will do.
        if (wantedProduct != null && CustomerQuestion.ZoneIsEnough(kind) && wantedZone.Contains(transform.position))
            return true;

        Vector3 here = transform.position;
        Vector3 there = destination;
        here.y = there.y = 0f;
        return Vector3.Distance(here, there) <= arriveRadius;
    }

    // --- setup / teardown ---------------------------------------------------

    bool ShouldAsk(float chance)
    {
        if (chance <= 0f) return false;
        if (CurrentStage != Stage.None) return false;
        if (npc.PlayerTransform == null) return false;
        if (agent == null || !agent.isOnNavMesh) return false;
        return Random.value <= chance;
    }

    // The spot in front of a facing, if they can walk there.
    bool AimAt(ShelfSlot target)
    {
        if (!NavMesh.SamplePosition(target.transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas)) return false;
        var path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;

        destination = hit.position;
        destinationShelf = target.owner;
        return true;
    }

    // Picks a shelf somewhere else in the store that actually stocks something, and
    // takes the product name from what that shelf's slots hold.
    bool PickDestination()
    {
        Transform[] points = npc.shelfPoints;
        if (points == null || points.Length == 0) return false;

        NavMeshPath path = new NavMeshPath();

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Transform point = points[Random.Range(0, points.Length)];
            if (point == null) continue;

            // Don't send them to the shelf they are already standing at.
            if (Vector3.Distance(point.position, transform.position) < arriveRadius * 3f) continue;

            ShelfSlot slot = NearestSlotTo(point.position, 4f);
            if (slot == null) continue;

            if (!NavMesh.SamplePosition(point.position, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            destination = hit.position;
            destinationShelf = slot.owner;
            wantedProduct = null;
            thanks = null;
            wanted = WantedFrom(slot);
            question = string.Format(
                questionTemplates.Length > 0 ? questionTemplates[Random.Range(0, questionTemplates.Length)] : "Where is {0}?",
                wanted);
            return true;
        }

        return false;
    }

    // What they came in for. Taken from the facing itself rather than invented, so the
    // shelf the beacon lands on really does stock the thing they asked about.
    string WantedFrom(ShelfSlot slot)
    {
        if (Random.value < sectionQuestionChance)
            return "the " + ProductCatalog.SectionName(slot.requiredType);

        return slot.Label;
    }

    static ShelfSlot NearestSlotTo(Vector3 position, float radius)
    {
        var slots = ShelfSlot.All;
        ShelfSlot nearest = null;
        float nearestSqr = radius * radius;

        for (int i = 0; i < slots.Count; i++)
        {
            float sqr = (position - slots[i].transform.position).sqrMagnitude;
            if (sqr >= nearestSqr) continue;
            nearest = slots[i];
            nearestSqr = sqr;
        }

        return nearest;
    }

    void Begin()
    {
        homePosition = transform.position;
        options = new[] { acceptLabel, declineLabel };
        talkRequested = false;

        StopWalking();
        npc.SetForcedHighlight(true);

        bubble = SpeechBubble.Create(transform, bubbleHeight, bubbleForward);

        // Nothing is said until the player asks — the cone overhead is the whole hint.
        askMarker = GuideMarker.CreateBeaconOver(
            "AskMarker", customerConeColour, customerConeSize, transform, customerConeHeight);

        SetStage(Stage.Asking);
    }

    void ShowEscortMarkers()
    {
        // The circle the player has to stay inside, drawn on the floor under the customer.
        radiusRing = GuideMarker.CreateRing("EscortRadius", radiusColour, followRadius, 0.94f);
        radiusRing.follow = transform;

        // The spot in front of the target shelf the customer has to reach.
        targetRing = GuideMarker.CreateRing("EscortTarget", targetColour, arriveRadius, 0f);
        targetRing.transform.position = destination + Vector3.up * 0.03f;
        targetRing.SetAnchor(destination + Vector3.up * 0.03f);

        // And a beacon over the shelf itself, high enough to clear the shelving.
        Vector3 above = destination + Vector3.up * 2.4f;
        if (destinationShelf != null)
        {
            Bounds bounds = ShelfBounds(destinationShelf);
            above = new Vector3(bounds.center.x, bounds.max.y + 0.85f, bounds.center.z);
        }

        beacon = GuideMarker.CreateBeacon("EscortBeacon", shelfConeColour, shelfConeSize);
        beacon.transform.position = above;
        beacon.SetAnchor(above);
    }

    static Bounds ShelfBounds(ShelfUnit shelf)
    {
        var renderers = shelf.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(shelf.transform.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    void Finish(string parting, float lingerSeconds)
    {
        if (bubble != null && !string.IsNullOrEmpty(parting) && lingerSeconds > 0f)
        {
            bubble.Show(parting);
            StartCoroutine(DestroyBubbleAfter(bubble, lingerSeconds));
            bubble = null;
        }

        Cleanup();
    }

    static IEnumerator DestroyBubbleAfter(SpeechBubble target, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (target != null) Destroy(target.gameObject);
    }

    void Cleanup()
    {
        SetStage(Stage.None);

        npc.SetForcedHighlight(false);

        ClearAskMarker();

        if (bubble != null) { Destroy(bubble.gameObject); bubble = null; }
        if (radiusRing != null) { Destroy(radiusRing.gameObject); radiusRing = null; }
        if (targetRing != null) { Destroy(targetRing.gameObject); targetRing = null; }
        if (beacon != null) { Destroy(beacon.gameObject); beacon = null; }

        if (agent != null && agent.isOnNavMesh)
        {
            if (originalStoppingDistance > 0f) agent.stoppingDistance = originalStoppingDistance;
            agent.isStopped = false;
        }
    }

    void ClearAskMarker()
    {
        if (askMarker == null) return;
        Destroy(askMarker.gameObject);
        askMarker = null;
    }

    void StopWalking()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        agent.ResetPath();
        agent.isStopped = true;
    }

    void SetStage(Stage stage)
    {
        if (CurrentStage == stage) return;
        CurrentStage = stage;

        // Anything other than None means the player still owes this customer an answer.
        bool pending = stage != Stage.None;
        if (pending != counted)
        {
            counted = pending;
            PendingCount = Mathf.Max(0, PendingCount + (pending ? 1 : -1));
            TaskManager.NotifyWorldChanged();
        }
    }

    // --- player-facing ------------------------------------------------------

    // Returns true when this swallowed the interaction, so CustomerNPC doesn't also
    // treat the same key press as serving them at the till.
    public bool TryTalk()
    {
        if (CurrentStage == Stage.Asking && PlayerWithin(talkRange))
        {
            talkRequested = true;
            return true;
        }

        // Mid-conversation the coroutine reads the key itself; just don't fall through.
        return CurrentStage == Stage.Talking;
    }

    public string GetPrompt()
    {
        if (CurrentStage == Stage.Asking) return "Talk";
        return string.Empty;
    }

    bool PlayerWithin(float range)
    {
        Transform player = npc.PlayerTransform;
        if (player == null) return false;
        return Vector3.Distance(transform.position, player.position) <= range;
    }

    static bool Pressed(KeyCode key) => !GamePause.Paused && Input.GetKeyDown(key);

    void OnDisable()
    {
        // Don't leave a despawned customer counted as still asking.
        if (counted)
        {
            counted = false;
            PendingCount = Mathf.Max(0, PendingCount - 1);
            TaskManager.NotifyWorldChanged();
        }
    }
}
