using UnityEngine;

// Retail fixtures hold individual ShelfSlots. Storage fixtures hold labelled ProductBoxes.
// Empty storage cabinets are intentional hiding spaces, never replenishment locations.
public sealed class ColdFixture : MonoBehaviour
{
    public bool retail;
    public bool hidingSpace;
    public bool frozen;
    public string[] products = System.Array.Empty<string>();
    public Vector3 boxSize = new Vector3(1.4f,.38f,.8f);
    public Vector3[] boxCentres = { new Vector3(0,.66f,0),new Vector3(0,1.16f,0),new Vector3(0,1.66f,0) };
}
