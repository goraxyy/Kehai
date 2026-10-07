using UnityEngine;

// The facing on the till counter: mints, the impulse buy every till in the world has. A shelf
// slot like any other, which StoreLayout makes from this: standing here, on the counter's top.
public class CounterFacing : MonoBehaviour
{
    [Tooltip("The slot's floor: x across the counter, y into it.")]
    public Vector2 size = new Vector2(0.3f, 0.3f);

    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 0.1f, 0f), new Vector3(size.x, 0.2f, size.y));
    }
}
