using UnityEngine;

// Marks a facing built by ShelfGrid and remembers what it was built for, so the shop can tell
// at load whether a baked facing still matches the planogram.
public class ShelfFacing : MonoBehaviour
{
    public string productId;
}
