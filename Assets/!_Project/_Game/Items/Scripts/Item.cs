using Kehai.Aiko;
using UnityEngine;

// The section of the store a thing belongs to — not the product itself. A shelf slot
// accepts anything from its own section, and which SKU is actually stacked there is
// Item.productId, resolved through ProductCatalog.
//
// The numbers are written out and must never be reshuffled: every ShelfSlot and item in
// the scene stores its type as an int, so renumbering these silently restocks the whole
// store with the wrong goods. New sections go on the end.
public enum ItemType
{
    Cereal = 0,        // cereal and breakfast, including coffee and tea
    SoftDrinks = 1,
    Bakery = 2,
    Dairy = 3,
    Snacks = 4,        // crisps, nuts, crackers

    Mop = 5,           // a tool rather than stock, so it never matches a shelf slot
    Stock = 6,         // the restocking crate — same, it's carried but never shelved
    TrashBag = 7,      // carried out to the container, never shelved
    Flashlight = 8,    // a tool as well — carried and dropped, never shelved

    Produce = 9,
    Canned = 10,       // tins and jars
    Noodles = 11,      // noodles, pasta and rice
    PersonalCare = 12,
    Household = 13,    // cleaning and paper goods
    Frozen = 14,
    PetFood = 15,
    Confectionery = 16
}

[RequireComponent(typeof(Rigidbody))]
public class Item : MonoBehaviour
{
    [Tooltip("Which section of the store this belongs to. Shelf slots match on this.")]
    public ItemType type;

    [Tooltip("Which product this actually is, as a ProductCatalog id — \"drink_pipisi\" " +
             "rather than just SoftDrinks. Left empty it is an unbranded box of whatever " +
             "the section sells, which is what the placeholder item prefab is.")]
    public string productId;

    // What the player is told they are holding: the SKU when there is one, the section
    // otherwise.
    public string DisplayName => ProductCatalog.Label(type, productId);

    // Half the height of the placeholder box (Item_def), whose origin is its middle: how far
    // it stands off whatever it's on.
    public const float SnapHeight = 0.2f;

    // From this item's origin down to the bottom of its mesh. Measured the first time it's
    // asked for, not in Awake, so it's right in the editor too. ProductLook sets it when it
    // swaps the model.
    public float RestHeight
    {
        get
        {
            if (float.IsNaN(restHeight)) restHeight = MeasureRest();
            return restHeight;
        }
        set => restHeight = value;
    }
    [System.NonSerialized] float restHeight = float.NaN;

    // Makes this a particular product: its section, its id, and — once the product has been
    // imported — its own model in place of the placeholder box.
    public void SetProduct(ItemType section, string id)
    {
        type = section;
        productId = id;
        ProductLook.Apply(this);
    }

    [Header("Impact Sound")]
    [Tooltip("Played when this lands on the floor, a shelf, or another item.")]
    public AudioClip impactSound;

    [Tooltip("Slower contacts than this are a nudge, not a knock, and stay silent.")]
    public float impactMinSpeed = 1.2f;

    [Tooltip("Contacts at or above this speed play at full volume.")]
    public float impactLoudSpeed = 6f;

    [Tooltip("One clatter per landing: a bouncing item makes several contacts in a row.")]
    public float impactCooldown = 0.12f;

    [Header("Hold Offset")]
    public Vector3 holdPositionOffset = Vector3.zero;
    public Vector3 holdRotationOffset = Vector3.zero;

    // Whoever last let go of it — a thing Aiko knocked off a shelf is her noise, a thing
    // the employee threw is theirs.
    [System.NonSerialized] public NoiseAuthor lastAuthor = NoiseAuthor.World;

    [HideInInspector] public bool isCarried;

    // Set on the product prefabs, which wait kinematic with their colliders off until a shelf
    // slot hands one over. Stock on a shelf isn't a GameObject at all (ShelfSlot).
    [HideInInspector] public bool isOnShelf;

    Rigidbody rb;
    Collider[] colliders;
    float nextImpactTime;

    // Every item that exists, stowed ones too (the replay recorder follows items off their shelves).
    static readonly System.Collections.Generic.List<Item> all = new System.Collections.Generic.List<Item>();
    public static System.Collections.Generic.IReadOnlyList<Item> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => all.Clear();

    void OnDestroy() => all.Remove(this);

    void Awake()
    {
        all.Add(this);
        rb = GetComponent<Rigidbody>();

        // Every collider, not just the one on the root. The mop keeps two more on its
        // children, and leaving those live while carried let them swing across the
        // crosshair as the player turned or strafed, re-targeting the mop in your hands.
        colliders = GetComponentsInChildren<Collider>(true);
    }

    // The placeholder box's half height when there's no mesh to measure.
    float MeasureRest()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return SnapHeight;
        Bounds b = filter.sharedMesh.bounds;
        return (b.extents.y - b.center.y) * transform.localScale.y;
    }

    // No Update(): with thousands of items in a level, re-applying a transform every frame
    // for every item costs far more than applying it once when the state actually changes.
    // The Set* methods below do that, and OnValidate keeps the in-editor live tweaking.

    void SetCollidersEnabled(bool on)
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = on;
    }

    // Dropped, thrown, or knocked off a shelf — anything that actually strikes something.
    // Held and shelved items are kinematic with their collider off, so they never get here.
    void OnCollisionEnter(Collision collision)
    {
        if (impactSound == null || isCarried || isOnShelf) return;
        if (Time.time < nextImpactTime) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < impactMinSpeed) return;

        nextImpactTime = Time.time + impactCooldown;

        Vector3 where = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        float loudness = Mathf.InverseLerp(impactMinSpeed, impactLoudSpeed, speed);
        OneShotAudio.PlayAt(impactSound, where, Mathf.Lerp(0.3f, 1f, loudness));
        NoiseBus.Emit(where, Mathf.Lerp(0.3f, 0.8f, loudness), NoiseKind.Impact, lastAuthor);
    }

    public void ApplyCarriedTransform()
    {
        transform.localPosition = holdPositionOffset;
        transform.localRotation = Quaternion.Euler(holdRotationOffset);
    }

#if UNITY_EDITOR
    // Tweaking the offsets in the Inspector during play still updates immediately,
    // but costs nothing at runtime.
    void OnValidate()
    {
        if (!Application.isPlaying) return;

        if (isCarried) ApplyCarriedTransform();
    }
#endif

    // Called when player actively holds item or when ejected
    public void SetCarried(bool carried, Transform parent)
    {
        gameObject.SetActive(true);
        isCarried = carried;
        isOnShelf = false;

        if (rb != null)
        {
            rb.isKinematic = carried;
            rb.useGravity = !carried;
        }

        // Colliders disabled while held so it neither pushes the player nor re-targets itself
        SetCollidersEnabled(!carried);

        if (carried && parent != null)
        {
            transform.SetParent(parent);
            ApplyCarriedTransform();
        }
        else
        {
            transform.SetParent(null);
        }
    }

    // Called when item goes into a non-active inventory slot
    public void SetStowed(Transform stashParent)
    {
        isCarried = false;
        isOnShelf = false;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        SetCollidersEnabled(false);

        transform.SetParent(stashParent);
        transform.localPosition = Vector3.zero;
        gameObject.SetActive(false);
    }
}