using UnityEngine;

// A fixture belongs to the room in front of it. Wall geometry may receive both adjoining
// rooms' lights, but a cabinet back touching that wall must not inherit the next room.
public sealed class InteriorLightAnchor : MonoBehaviour
{
    public Vector3 localPoint;
    public Vector3 Position => transform.TransformPoint(localPoint);
}
