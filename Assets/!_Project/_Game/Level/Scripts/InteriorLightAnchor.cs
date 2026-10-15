using UnityEngine;

// A fixture belongs to the room in front of it. Wall geometry may receive both adjoining
// rooms' lights, but a cabinet back touching that wall must not inherit the next room.
public sealed class InteriorLightAnchor : MonoBehaviour
{
    public Vector3 localPoint;
    [Tooltip("Facade and rooftop fixtures receive outdoor light even above an indoor room.")]
    public bool outdoors;
    public Vector3 Position => transform.TransformPoint(localPoint);
}
