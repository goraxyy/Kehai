using Kehai;
using Kehai.Karen;
using UnityEngine;

// Anything other than the keyboard and mouse that wants to walk the player around: the
// simulated players Karen is tested against, and the eval harness's agent driver. They
// steer the same CharacterController a person does, so collisions, sprint rules and
// footstep noise are identical whoever is playing.
public interface IMotorInput
{
    Vector3 MoveWorld { get; }     // desired direction on the floor, length 0..1
    bool Sprint { get; }
    bool Crouch { get; }           // held state, not a toggle
    float YawDelta { get; }        // degrees this frame
    float PitchDelta { get; }
    bool Jump { get; }
}

[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 6.5f;
    public float crouchSpeed = 2.5f;
    public float gravity = -20f;
    public float jumpHeight = 1.2f;

    [Header("Look")]
    public Transform cameraRoot;
    public float mouseSensitivity = 4f;
    public float maxLookAngle = 80f;

    [Header("Crouch")]
    public float crouchControllerHeight = 0.8f;
    public float crouchCameraY = 0.35f;
    public float standCameraY = 1.6f;

    [Header("Stamina")]
    [Tooltip("Sprinting is disabled when this hits empty. Found automatically if unset.")]
    public BurnoutSystem burnout;

    [Header("Footsteps")]
    [Tooltip("Seconds between steps at each pace. Every step is a noise " + GameNames.Antagonist + " can hear.")]
    public float walkStepInterval = 0.5f;
    public float sprintStepInterval = 0.33f;
    public float crouchStepInterval = 0.65f;

    // Set by a bot or the eval harness to drive the player instead of the keyboard.
    [System.NonSerialized] public IMotorInput externalInput;

    // Frozen in place — Karen's lecture after a catch. Looking around still works.
    [System.NonSerialized] public bool movementLocked;

    CharacterController controller;
    Vector3 velocity;
    Vector3 planarVelocity;
    float pitch;
    bool isCrouching;
    float stepTimer;
    float yawThisFrame;

    public bool IsCrouching => isCrouching;
    public Vector3 PlanarVelocity => planarVelocity;
    public float PlanarSpeed => planarVelocity.magnitude;
    public bool IsMoving => planarVelocity.sqrMagnitude > 0.04f;
    public float YawThisFrame => yawThisFrame;
    public float Pitch => pitch;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (burnout == null) burnout = FindAnyObjectByType<BurnoutSystem>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (GamePause.Paused) return;   // the Esc menu is open
        HandleLook();
        HandleMovement();
        HandleCrouch();
        HandleFootsteps();
    }

    void HandleLook()
    {
        float yaw, pitchDelta;
        if (externalInput != null)
        {
            yaw = externalInput.YawDelta;
            pitchDelta = externalInput.PitchDelta;
        }
        else
        {
            yaw = Input.GetAxis("Mouse X") * mouseSensitivity;
            pitchDelta = Input.GetAxis("Mouse Y") * mouseSensitivity;
        }

        yawThisFrame = yaw;
        transform.Rotate(Vector3.up * yaw);

        pitch -= pitchDelta;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        if (cameraRoot != null)
            cameraRoot.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    void HandleMovement()
    {
        bool isGrounded = controller.isGrounded;

        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;

        Vector3 moveDir;
        if (movementLocked)
        {
            moveDir = Vector3.zero;
        }
        else if (externalInput != null)
        {
            moveDir = externalInput.MoveWorld;
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
        }
        else
        {
            // Read WASD directly rather than through the Horizontal/Vertical axes: those also
            // carry the arrow keys, which belong to menus now.
            float horizontal = Axis(KeyCode.D, KeyCode.A);
            float vertical = Axis(KeyCode.W, KeyCode.S);
            moveDir = (transform.right * horizontal + transform.forward * vertical).normalized;
        }

        float speed = walkSpeed;
        if (IsSprinting())
            speed = sprintSpeed;
        else if (isCrouching)
            speed = crouchSpeed;

        Vector3 before = transform.position;
        controller.Move(moveDir * speed * Time.deltaTime);

        bool jump = externalInput != null ? externalInput.Jump : Input.GetKeyDown(KeyCode.Space);
        if (isGrounded && jump && !isCrouching && !movementLocked)
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        Vector3 moved = transform.position - before;
        moved.y = 0f;
        planarVelocity = Time.deltaTime > 0f ? moved / Time.deltaTime : Vector3.zero;
    }

    static float Axis(KeyCode positive, KeyCode negative)
    {
        float value = 0f;
        if (Input.GetKey(positive)) value += 1f;
        if (Input.GetKey(negative)) value -= 1f;
        return value;
    }

    void HandleCrouch()
    {
        if (externalInput != null)
        {
            if (externalInput.Crouch != isCrouching && (externalInput.Crouch || CanStandUp()))
                SetCrouch(externalInput.Crouch);
            return;
        }

        // Crouch while Left Ctrl is held; let go and you stand up again, as soon as there's
        // headroom. Sprinting wins: Shift stands you up and runs, if you've the breath for it.
        bool wantsCrouch = Input.GetKey(KeyCode.LeftControl) && !WantsToSprint();
        if (wantsCrouch && !isCrouching) SetCrouch(true);
        else if (!wantsCrouch && isCrouching && CanStandUp()) SetCrouch(false);
    }

    bool WantsToSprint() => !movementLocked && Input.GetKey(KeyCode.LeftShift) && (burnout == null || burnout.CanSprint);

    // Every step is a noise on the bus. Sprinting is loud and frequent, crouching is
    // nearly silent — the whole stealth game is in these three numbers (Karen.md §3.2).
    void HandleFootsteps()
    {
        if (!IsMoving || !controller.isGrounded)
        {
            stepTimer = 0f;
            return;
        }

        bool sprinting = IsSprinting();
        float interval = sprinting ? sprintStepInterval : isCrouching ? crouchStepInterval : walkStepInterval;

        stepTimer += Time.deltaTime;
        if (stepTimer < interval) return;
        stepTimer = 0f;

        if (sprinting) NoiseBus.Emit(transform.position, 0.9f, NoiseKind.Sprint, NoiseAuthor.Player);
        else if (isCrouching) NoiseBus.Emit(transform.position, 0.1f, NoiseKind.CrouchStep, NoiseAuthor.Player);
        else NoiseBus.Emit(transform.position, 0.35f, NoiseKind.Footstep, NoiseAuthor.Player);

        // What you hear is what she hears: loud when sprinting, barely there when crouched.
        if (feet == null)
        {
            feet = gameObject.AddComponent<AudioSource>();
            feet.playOnAwake = false;
            feet.spatialBlend = 0f;
        }
        feet.pitch = 0.92f + 0.16f * (float)stepRandom.NextDouble();   // own RNG: leaves seeded runs alone
        feet.PlayOneShot(ProceduralAudio.PlayerStep(stepVariant = (stepVariant + 1) % 3),
                         (sprinting ? 0.45f : isCrouching ? 0.07f : 0.22f) * SoundSettings.Get(SoundKind.Effects));
    }

    AudioSource feet;
    int stepVariant;
    readonly System.Random stepRandom = new System.Random(7);

    void SetCrouch(bool crouch)
    {
        isCrouching = crouch;

        controller.height = crouch ? crouchControllerHeight : 2f;
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

        if (cameraRoot != null)
            cameraRoot.localPosition = new Vector3(0f, crouch ? crouchCameraY : standCameraY, 0f);
    }

    bool CanStandUp()
    {
        float skinWidth = 0.1f;
        float checkRadius = controller.radius - skinWidth;
        Vector3 checkStart = transform.position + Vector3.up * (controller.radius + skinWidth);
        float checkDistance = 2f - controller.radius * 2f;

        return !Physics.SphereCast(checkStart, checkRadius, Vector3.up, out _, checkDistance);
    }

    // Holding shift isn't enough — a burnt-out employee can only walk.
    public bool IsSprinting()
    {
        if (isCrouching || movementLocked) return false;
        bool wants = externalInput != null ? externalInput.Sprint : Input.GetKey(KeyCode.LeftShift);
        if (!wants) return false;
        return burnout == null || burnout.CanSprint;
    }
}
