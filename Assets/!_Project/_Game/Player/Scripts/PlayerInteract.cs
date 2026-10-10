using UnityEngine;

public interface IInteractable
{
    void Interact(PlayerInteract player);
    string GetPrompt();
}

// Optional add-on for interactables that want to react to being looked at.
public interface IHoverable
{
    void OnHoverEnter();
    void OnHoverExit();
}

// Optional add-on for interactables completed by holding E rather than tapping it.
public interface IHoldInteractable
{
    bool CanHold(PlayerInteract player);
    float HoldDuration { get; }
    void OnHoldProgress(float normalised);
    void OnHoldComplete(PlayerInteract player);
    void OnHoldCancelled();
}

public class PlayerInteract : MonoBehaviour
{
    [Header("Interaction")]
    public Transform rayOrigin;
    public float interactRange = 2.5f;

    [Tooltip("Customers are talked to across a counter, so they reach further than shelves do.")]
    public float customerInteractRange = 5f;

    public LayerMask interactLayer;

    [Header("Audio")]
    [Tooltip("Played whenever the player takes an item into their hands.")]
    public AudioClip pickupSound;
    [Range(0f, 1f)] public float pickupVolume = 0.8f;

    [Header("Input")]
    public KeyCode interactKey = KeyCode.E;
    public KeyCode dropKey = KeyCode.Q;

    [Header("Dropping and throwing")]
    [Tooltip("A Q shorter than this just puts the item down; longer winds up a throw.")]
    public float tapThreshold = 0.15f;

    [Tooltip("Holding Q this long winds the throw up to full power.")]
    public float throwChargeTime = 1.1f;

    [Tooltip("Speed of the weakest throw, the moment a tap becomes a hold.")]
    public float minThrowSpeed = 3f;

    [Tooltip("Speed of a fully wound-up throw. The charge is capped here.")]
    public float maxThrowSpeed = 13f;

    [Tooltip("Tumble put on a thrown item, scaled by the charge.")]
    public float throwSpin = 3f;

    // 0 while the key is untouched, ramping to 1 at full power — for a charge meter.
    public float ThrowCharge01 { get; private set; }

    [HideInInspector] public CarrySlot carrySlot;
    [HideInInspector] public PlayerTools tools;

    IInteractable currentTarget;
    IHoldInteractable holdTarget;
    float holdTimer;

    // Programmatic hands, for the simulated players and the eval harness's agent driver.
    // They go through exactly the same interaction code a person's keypresses do.
    [System.NonSerialized] public IInteractable forcedTarget;
    [System.NonSerialized] public bool forcedHold;
    bool forcedTap;
    bool forcedDrop;
    float forcedDropSpeed;

    public void TapInteract() => forcedTap = true;

    // Put down (speed 0) or throw what's in hand.
    public void DropNow(float throwSpeed = 0f)
    {
        forcedDrop = true;
        forcedDropSpeed = throwSpeed;
    }
    public bool IsHolding => holdTarget != null;

    public IInteractable CurrentTarget => currentTarget;

    void Awake()
    {
        carrySlot = GetComponent<CarrySlot>();
        tools = GetComponent<PlayerTools>();
        if (rayOrigin == null)
            rayOrigin = Camera.main.transform;
    }

    void Update()
    {
        if (GamePause.Paused) return;   // the Esc menu is open
        HandleSlotSwitching();

        // A target can be destroyed while we're looking at it (e.g. a customer despawning).
        if (!IsAlive(currentTarget))
            currentTarget = null;

        CheckForInteractable();

        if (HandleHoldInteraction())
            return;   // a hold is in progress; don't also fire tap interactions

        // E uses whatever is under the crosshair; with nothing there it falls through to
        // the thing in your hand, which is how the torch is switched on and off.
        if (Input.GetKeyDown(interactKey) || forcedTap)
        {
            forcedTap = false;
            if (currentTarget != null) currentTarget.Interact(this);
            else UseHeldItem();
        }

        HandleDropAndThrow();
    }

    // Tap Q to put an item down, hold it to wind up a throw.
    void HandleDropAndThrow()
    {
        if (forcedDrop)
        {
            forcedDrop = false;
            DropHeld(forcedDropSpeed, forcedDropSpeed > 0f ? 0.3f : 0f);
            return;
        }

        if (Input.GetKeyDown(dropKey))
        {
            dropHeldSince = Time.time;
            ThrowCharge01 = 0f;
        }

        if (Input.GetKey(dropKey) && dropHeldSince >= 0f)
        {
            float winding = Time.time - dropHeldSince - tapThreshold;
            ThrowCharge01 = Mathf.Clamp01(winding / Mathf.Max(0.01f, throwChargeTime - tapThreshold));
        }

        if (Input.GetKeyUp(dropKey) && dropHeldSince >= 0f)
        {
            float held = Time.time - dropHeldSince;
            dropHeldSince = -1f;

            float speed = held < tapThreshold
                ? 0f
                : Mathf.Lerp(minThrowSpeed, maxThrowSpeed, ThrowCharge01);

            DropHeld(speed, ThrowCharge01);
            ThrowCharge01 = 0f;
        }
    }

    // Nothing under the crosshair, so the item in hand gets the key instead.
    void UseHeldItem()
    {
        if (carrySlot == null || !carrySlot.IsCarrying) return;

        Flashlight torch = carrySlot.currentItem.GetComponent<Flashlight>();
        if (torch != null) torch.Toggle();
    }

    void HandleSlotSwitching()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) carrySlot.SetActiveSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) carrySlot.SetActiveSlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) carrySlot.SetActiveSlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) carrySlot.SetActiveSlot(3);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f) carrySlot.SetActiveSlot((carrySlot.activeSlot + 3) % 4);
        if (scroll < 0f) carrySlot.SetActiveSlot((carrySlot.activeSlot + 1) % 4);
    }

    void CheckForInteractable()
    {
        if (forcedTarget != null && IsAlive(forcedTarget))
        {
            SetTarget(forcedTarget);
            return;
        }

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        // Normal reach first: things with colliders (an item on the floor, a shopper, a tool)
        // and shelf slots, which are data and aimed at by ShelfAim. Whichever is nearer.
        ShelfSlot slot = ShelfAim.Find(ray, interactRange, out float slotDistance);
        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactLayer) &&
            (slot == null || hit.distance <= slotDistance))
        {
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            if (interactable != null)
            {
                SetTarget(interactable);
                return;
            }
        }
        if (slot != null)
        {
            SetTarget(slot);
            return;
        }

        // Then a longer probe that only customers answer, so they can be served from
        // the far side of a till without shelves becoming reachable from a distance too.
        if (customerInteractRange > interactRange &&
            Physics.Raycast(ray, out RaycastHit farHit, customerInteractRange, interactLayer))
        {
            CustomerNPC customer = farHit.collider.GetComponentInParent<CustomerNPC>();
            if (customer != null)
            {
                SetTarget(customer);
                return;
            }
        }

        SetTarget(null);
    }

    // Q: put down whatever is in hand — a bulky tool first, otherwise the active inventory item.
    float dropHeldSince = -1f;

    void DropHeld(float throwSpeed, float charge)
    {
        if (tools != null && tools.IsHolding)
        {
            // The bin belongs somewhere specific.
            var toolHome = tools.HeldTool.GetComponent<IHomeReturnable>();
            tools.Release();
            toolHome?.ReturnHome();
            return;
        }

        // Everything else gets tossed out in front of you. Tools that belong somewhere
        // are recalled by looking at their snap point and pressing E, not by dropping.
        if (carrySlot == null || !carrySlot.IsCarrying) return;

        Item dropped = carrySlot.Drop();
        if (dropped == null) return;

        dropped.lastAuthor = Kehai.Karen.NoiseAuthor.Player;
        GameEvents.RaisePlayerDroppedItem(dropped, dropped.transform.position);
        Kehai.Karen.NoiseBus.Emit(dropped.transform.position, 0.7f, Kehai.Karen.NoiseKind.DroppedItem, Kehai.Karen.NoiseAuthor.Player);

        var body = dropped.GetComponent<Rigidbody>();
        if (body == null || throwSpeed <= 0f) return;

        // Start it clear of the player so it doesn't immediately collide with them.
        body.position = rayOrigin.position + rayOrigin.forward * 0.6f;

        // Set the velocity rather than adding a force: AddForce waits for the next
        // physics step, and the item has only just stopped being kinematic.
        body.linearVelocity = rayOrigin.forward * throwSpeed;
        body.angularVelocity = rayOrigin.right * throwSpin * charge;
    }

    // Returns true while a hold-to-use action (mopping) is running.
    bool HandleHoldInteraction()
    {
        var candidate = currentTarget as IHoldInteractable;
        bool eligible = candidate != null && candidate.CanHold(this) && (Input.GetKey(KeyCode.E) || forcedHold);

        if (!eligible)
        {
            if (holdTarget != null)
            {
                holdTarget.OnHoldCancelled();
                holdTarget = null;
                holdTimer = 0f;
            }
            return false;
        }

        if (!ReferenceEquals(holdTarget, candidate))
        {
            holdTarget?.OnHoldCancelled();
            holdTarget = candidate;
            holdTimer = 0f;
        }

        holdTimer += Time.deltaTime;
        float duration = Mathf.Max(0.01f, holdTarget.HoldDuration);
        holdTarget.OnHoldProgress(Mathf.Clamp01(holdTimer / duration));

        if (holdTimer >= duration)
        {
            IHoldInteractable finished = holdTarget;
            holdTarget = null;
            holdTimer = 0f;
            finished.OnHoldComplete(this);
        }

        return true;
    }

    void SetTarget(IInteractable target)
    {
        if (ReferenceEquals(currentTarget, target)) return;

        if (IsAlive(currentTarget))
            (currentTarget as IHoverable)?.OnHoverExit();

        currentTarget = target;

        (currentTarget as IHoverable)?.OnHoverEnter();
    }

    // Guards against calling into a MonoBehaviour that Unity has already destroyed.
    static bool IsAlive(IInteractable interactable)
    {
        if (interactable == null) return false;
        if (interactable is Object unityObject) return unityObject != null;
        return true;
    }
}