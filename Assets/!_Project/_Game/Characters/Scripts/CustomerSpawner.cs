using System.Collections;
using UnityEngine;

// Brings shoppers in while a shift runs. At most MaxCustomers in the store at once, counted
// across every spawner, and paced so the doors never fill up: the first arrives a little
// after the doors open, each next one waits longer the fuller the store is, and nobody comes
// in while someone is still standing in the entrance.
public class CustomerSpawner : MonoBehaviour
{
    // The ceiling, whatever the scene sets maxActiveCustomers to.
    public const int MaxCustomers = 5;

    [Header("References")]
    public GameObject customerPrefab;
    public Transform entrancePoint;   // left door
    public Transform exitPoint;       // right door
    public Transform[] shelfPoints;   // pool of browsing spots around the store
    public Transform cashierPoint;    // first cashier desk

    [Header("Spawning")]
    [Tooltip("Seconds between arrivals in an empty store; each shopper already inside adds half again.")]
    public Vector2 spawnIntervalRange = new Vector2(8f, 16f);
    [Tooltip("The first shopper arrives this long after the doors open.")]
    public Vector2 firstArrivalRange = new Vector2(6f, 12f);
    public int maxActiveCustomers = MaxCustomers;
    [Tooltip("Nobody new comes in while someone is this close to the entrance.")]
    public float entranceClearRadius = 2.5f;

    [Tooltip("Driven by ShiftManager — customers only arrive while a shift is running.")]
    public bool spawningEnabled;

    // Shoppers in the store, from every spawner.
    public static int InStore { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => InStore = 0;

    public int Cap => Mathf.Clamp(maxActiveCustomers, 0, MaxCustomers);

    void Start()
    {
        StartCoroutine(SpawnLoop());
    }

    void OnDestroy()
    {
        // The scene is going (a restart, an eval reset): its shoppers go with it.
        InStore = 0;
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            // Wait for the doors to open, then for the first arrival.
            while (!spawningEnabled) yield return null;
            yield return new WaitForSeconds(Random.Range(firstArrivalRange.x, firstArrivalRange.y));

            while (spawningEnabled)
            {
                if (InStore < Cap && EntranceClear())
                {
                    SpawnCustomer();
                    yield return new WaitForSeconds(NextDelay(InStore));
                }
                else yield return new WaitForSeconds(1f);
            }
        }
    }

    // Longer the fuller the store is: an empty shop fills gradually, a busy one barely grows.
    public float NextDelay(int inStore) =>
        Random.Range(spawnIntervalRange.x, spawnIntervalRange.y) * (1f + 0.5f * Mathf.Max(0, inStore));

    bool EntranceClear()
    {
        if (entrancePoint == null) return true;
        foreach (Collider c in Physics.OverlapSphere(entrancePoint.position, entranceClearRadius, ~0, QueryTriggerInteraction.Ignore))
            if (c.GetComponentInParent<CustomerNPC>() != null) return false;
        return true;
    }

    void SpawnCustomer()
    {
        if (customerPrefab == null || entrancePoint == null)
        {
            Debug.LogWarning("CustomerSpawner is missing customerPrefab or entrancePoint.");
            return;
        }

        GameObject instance = Instantiate(customerPrefab, entrancePoint.position, entrancePoint.rotation);
        CustomerNPC npc = instance.GetComponent<CustomerNPC>();

        if (npc == null)
        {
            Debug.LogWarning("customerPrefab has no CustomerNPC component.");
            Destroy(instance);
            return;
        }

        npc.Init(shelfPoints, cashierPoint, exitPoint, OnCustomerDespawned);
        InStore++;
    }

    void OnCustomerDespawned()
    {
        InStore = Mathf.Max(0, InStore - 1);
    }
}
