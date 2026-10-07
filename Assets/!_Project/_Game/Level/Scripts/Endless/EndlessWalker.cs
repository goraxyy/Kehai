using UnityEngine;

// A first-person walker for the endless maze's own scene: mouse to look, WASD to walk, shift to
// hurry, Esc to free the cursor and a click to take it back. The game's player belongs to the
// store and everything in it, so this stands in until the two meet.
[RequireComponent(typeof(CharacterController))]
public class EndlessWalker : MonoBehaviour
{
    public Transform view;
    public float walkSpeed = 4f;
    public float hurrySpeed = 7f;
    public float lookSpeed = 2f;

    CharacterController body;
    float pitch;
    float fall;

    void Awake()
    {
        body = GetComponent<CharacterController>();
        if (view == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) view = cam.transform;
        }
    }

    void OnEnable() => Grab(true);
    void OnDisable() => Grab(false);

    static void Grab(bool on)
    {
        Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !on;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) Grab(false);
        if (Input.GetMouseButtonDown(0)) Grab(true);

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            transform.Rotate(0f, Input.GetAxis("Mouse X") * lookSpeed, 0f);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSpeed, -85f, 85f);
            if (view != null) view.localEulerAngles = new Vector3(pitch, 0f, 0f);
        }

        Vector3 move = transform.right * Input.GetAxis("Horizontal") + transform.forward * Input.GetAxis("Vertical");
        move = Vector3.ClampMagnitude(move, 1f) * (Input.GetKey(KeyCode.LeftShift) ? hurrySpeed : walkSpeed);
        fall = body.isGrounded ? -1f : fall - 9.81f * Time.deltaTime;
        body.Move((move + Vector3.up * fall) * Time.deltaTime);
    }
}
