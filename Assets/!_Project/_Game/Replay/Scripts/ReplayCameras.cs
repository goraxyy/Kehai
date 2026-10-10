using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kehai.Replay
{
    public enum ShotPreset { Pov, Cctv, Chase, Orbit, TopDown, Free, Path }

    // Where the replay's camera is. The presets follow a subject (Karen, or you) and are worked
    // out from the recording at t alone, so scrubbing, pausing and rendering all see the same
    // picture; only the CCTV corner remembers which corner it's in, and only the free camera
    // takes input.
    //   POV       your eyes, as recorded (eyelids too)
    //   CCTV      a high corner looking at the subject; cuts to another corner when it loses sight
    //   Chase     behind the subject, over the shoulder
    //   Orbit     circling the subject
    //   TopDown   straight down through the ceiling onto the subject (and the other one, if near)
    //   Free      fly: right mouse to look, WASD, Q/E, scroll for speed, Z/X for field of view
    //   Path      the keyframes saved with K (ShotPath)
    public sealed class ReplayCameras
    {
        public ShotPreset Preset = ShotPreset.Chase;
        public KrecKind Subject = KrecKind.Karen;
        public ShotPath Path;
        public bool DepthOfFieldOn;
        public float Focus { get; private set; } = 5f;
        public float Eyelids { get; private set; }

        readonly ReplayStage stage;
        readonly ReplayData data;
        readonly Camera cam;
        readonly Transform lids;
        readonly Transform topLid, bottomLid;
        Volume volume;
        DepthOfField dof;

        Vector3 flyPosition;
        float flyYaw, flyPitch, flySpeed = 4f, flyFov = 60f;
        Vector3 cctvAt;
        bool cctvSet;
        float cctvLost, lastT = float.NegativeInfinity;

        public ReplayCameras(ReplayStage stage)
        {
            this.stage = stage;
            data = stage.Data;
            cam = stage.Camera;

            lids = new GameObject("Eyelids").transform;
            lids.SetParent(cam.transform, false);
            topLid = Lid("Top lid");
            bottomLid = Lid("Bottom lid");
            lids.gameObject.SetActive(false);
            BuildDepthOfField();
        }

        Transform Lid(string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(lids, false);
            go.GetComponent<Renderer>().sharedMaterial = ReplayLook.Flat(Color.black, onTop: true);
            return go.transform;
        }

        void BuildDepthOfField()
        {
            var go = new GameObject("Replay depth of field");
            go.transform.SetParent(stage.Camera.transform.parent, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            dof = volume.sharedProfile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focalLength.Override(55f);
            dof.aperture.Override(2.4f);
            dof.focusDistance.Override(5f);
            volume.weight = 0f;
        }

        public void CycleSubject() => Subject = Subject == KrecKind.Karen ? KrecKind.Player : KrecKind.Karen;

        int SubjectId => Subject == KrecKind.Player ? stage.PlayerId : stage.KarenId;
        int OtherId => Subject == KrecKind.Player ? stage.KarenId : stage.PlayerId;

        public void Use(ShotPreset preset)
        {
            if (preset == ShotPreset.Free && Preset != ShotPreset.Free)
            {
                // Fly on from wherever the camera is.
                flyPosition = cam.transform.position;
                Vector3 e = cam.transform.rotation.eulerAngles;
                flyYaw = e.y;
                flyPitch = e.x > 180f ? e.x - 360f : e.x;
                flyFov = cam.fieldOfView;
            }
            Preset = preset;
            cctvSet = false;
        }

        // ---- each frame --------------------------------------------------------------------

        public void Apply(float t, float dt, bool input)
        {
            if (t < lastT || t - lastT > 0.5f) cctvSet = false;   // a jump: pick the corner again
            lastT = t;

            Vector3 position;
            Quaternion rotation;
            float fov;
            Eyelids = 0f;
            Focus = 5f;
            stage.ShowPlayerBody = Preset != ShotPreset.Pov;

            switch (Preset)
            {
                case ShotPreset.Pov:
                    if (!data.TryCamera(t, out CameraSample view)) goto default;
                    position = view.Position;
                    rotation = view.Rotation;
                    fov = view.Fov > 1f ? view.Fov : 60f;
                    Eyelids = view.Eyelids;
                    Focus = FocusAhead(position, rotation);
                    break;
                case ShotPreset.Free:
                    if (input) Fly(dt);
                    position = flyPosition;
                    rotation = Quaternion.Euler(flyPitch, flyYaw, 0f);
                    fov = flyFov;
                    Focus = FocusAhead(position, rotation);
                    break;
                case ShotPreset.Path:
                    if (Path == null || !Path.Evaluate(t, out ShotPath.Key key)) goto default;
                    position = key.position;
                    rotation = key.rotation;
                    fov = key.fov;
                    Focus = key.focus;
                    break;
                case ShotPreset.Cctv:
                    Cctv(t, dt, out position, out rotation, out fov);
                    break;
                case ShotPreset.Orbit:
                    Orbit(t, out position, out rotation, out fov);
                    break;
                case ShotPreset.TopDown:
                    TopDown(t, out position, out rotation, out fov);
                    break;
                default:
                    Chase(t, out position, out rotation, out fov);
                    break;
            }

            cam.transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = Mathf.Clamp(fov, 10f, 120f);
            if (Preset != ShotPreset.TopDown) cam.nearClipPlane = 0.05f;
            ShowLids(Eyelids);

            volume.weight = DepthOfFieldOn ? 1f : 0f;
            dof.focusDistance.Override(Mathf.Max(0.2f, Focus));
            if (DepthOfFieldOn) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        // ---- the subject -------------------------------------------------------------------

        // Where the subject was, averaged over the last `window` seconds: a camera that follows
        // this doesn't shake with every step, and needs no memory of earlier frames.
        bool Smoothed(int id, float t, float window, out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero;
            forward = Vector3.zero;
            if (id < 0) return false;
            int n = 0;
            for (int i = 0; i < 7; i++)
            {
                if (!data.TryPose(id, t - window * i / 6f, out EntitySample s)) continue;
                position += s.Position;
                Vector3 f = s.Rotation * Vector3.forward;
                f.y = 0f;
                forward += f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.zero;
                n++;
            }
            if (n == 0) return false;
            position /= n;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            return true;
        }

        float HeadHeight(int id) => id == stage.KarenId ? 2.05f : 1.6f;

        Vector3 Head(Vector3 at, int id) => new Vector3(at.x, stage.FloorY + HeadHeight(id), at.z);

        void Chase(float t, out Vector3 position, out Quaternion rotation, out float fov)
        {
            fov = 58f;
            if (!Smoothed(SubjectId, t, 0.6f, out Vector3 at, out Vector3 forward)) { Fallback(out position, out rotation); return; }
            // Up and behind, over the shelves where it can: the subject low in the picture, and
            // the floor ahead of them (where her mind is drawn) in view.
            Vector3 head = Head(at, SubjectId);
            Vector3 want = head + Vector3.up * 1.4f - forward * 4.2f + Vector3.Cross(Vector3.up, forward) * 0.6f;
            position = Clear(head, want);
            rotation = Quaternion.LookRotation(head + forward * 3f - Vector3.up * 1.1f - position, Vector3.up);
            Focus = Vector3.Distance(position, head);
        }

        void Orbit(float t, out Vector3 position, out Quaternion rotation, out float fov)
        {
            fov = 55f;
            if (!Smoothed(SubjectId, t, 1f, out Vector3 at, out _)) { Fallback(out position, out rotation); return; }
            Vector3 head = Head(at, SubjectId);
            float angle = (t * 14f + 30f) * Mathf.Deg2Rad;
            Vector3 want = head + new Vector3(Mathf.Cos(angle) * 6.5f, 1.4f, Mathf.Sin(angle) * 6.5f);
            position = Clear(head, want);
            rotation = Quaternion.LookRotation(head - Vector3.up * 0.3f - position, Vector3.up);
            Focus = Vector3.Distance(position, head);
        }

        void TopDown(float t, out Vector3 position, out Quaternion rotation, out float fov)
        {
            fov = 55f;
            if (!Smoothed(SubjectId, t, 1f, out Vector3 at, out _)) { Fallback(out position, out rotation); return; }
            float spread = 0f;
            if (Smoothed(OtherId, t, 1f, out Vector3 other, out _) && Vector3.Distance(at, other) < 18f)
            {
                spread = Vector3.Distance(at, other);
                at = (at + other) * 0.5f;
            }
            float height = Mathf.Clamp(9f + spread * 1.1f, 12f, 32f);
            position = new Vector3(at.x, stage.FloorY + height, at.z);
            rotation = Quaternion.Euler(90f, 0f, 0f);
            // The roof is cut away: nothing above the ceiling's underside is drawn.
            float ceiling = Ceiling(Head(at, SubjectId));
            cam.nearClipPlane = Mathf.Max(0.05f, stage.FloorY + height - ceiling + 0.25f);
            Focus = height;
        }

        // A high corner that can see the subject; another one when it can't (a cut, like a
        // switch between security feeds).
        void Cctv(float t, float dt, out Vector3 position, out Quaternion rotation, out float fov)
        {
            fov = 64f;
            if (!Smoothed(SubjectId, t, 0.8f, out Vector3 at, out Vector3 forward)) { Fallback(out position, out rotation); return; }
            Vector3 head = Head(at, SubjectId);
            bool sees = cctvSet && !Blocked(cctvAt, head) && Vector3.Distance(cctvAt, head) < 15f;
            cctvLost = sees ? 0f : cctvLost + Mathf.Max(dt, 1f / 60f);
            if (!cctvSet || cctvLost > 0.75f)
            {
                cctvAt = PickCorner(head, forward);
                cctvSet = true;
                cctvLost = 0f;
            }
            position = cctvAt;
            rotation = Quaternion.LookRotation(head - Vector3.up * 0.5f - position, Vector3.up);
            Focus = Vector3.Distance(position, head);
        }

        Vector3 PickCorner(Vector3 head, Vector3 forward)
        {
            float mount = Mathf.Min(Ceiling(head) - 0.35f, stage.FloorY + 4.2f);
            Vector3 best = head + Vector3.up;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = Quaternion.AngleAxis(45f * i + 22.5f, Vector3.up) * forward;
                Vector3 from = new Vector3(head.x, mount, head.z);
                float reach = Physics.Raycast(from, d, out RaycastHit hit, 9f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance - 0.4f : 9f;
                if (reach < 2.5f) continue;
                Vector3 at = from + d * reach;
                if (Blocked(at, head)) continue;
                // Far enough to take in the aisle, and in front of the subject rather than behind.
                float score = reach + Vector3.Dot(d, forward) * 2f;
                if (score > bestScore) { bestScore = score; best = at; }
            }
            return best;
        }

        // ---- free fly ----------------------------------------------------------------------

        void Fly(float dt)
        {
            if (Input.GetMouseButton(1))
            {
                flyYaw += Input.GetAxis("Mouse X") * 3f;
                flyPitch = Mathf.Clamp(flyPitch - Input.GetAxis("Mouse Y") * 3f, -89f, 89f);
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) flySpeed = Mathf.Clamp(flySpeed * Mathf.Pow(1.2f, scroll), 0.3f, 60f);
            if (Input.GetKey(KeyCode.Z)) flyFov = Mathf.Max(15f, flyFov - 30f * dt);
            if (Input.GetKey(KeyCode.X)) flyFov = Mathf.Min(100f, flyFov + 30f * dt);

            Quaternion look = Quaternion.Euler(flyPitch, flyYaw, 0f);
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += look * Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move -= look * Vector3.forward;
            if (Input.GetKey(KeyCode.D)) move += look * Vector3.right;
            if (Input.GetKey(KeyCode.A)) move -= look * Vector3.right;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;
            float speed = flySpeed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            flyPosition += move * speed * dt;
        }

        // ---- helpers -----------------------------------------------------------------------

        // A camera position short of whatever stands between it and what it looks at.
        static Vector3 Clear(Vector3 from, Vector3 want)
        {
            Vector3 d = want - from;
            float length = d.magnitude;
            if (length < 1e-3f) return want;
            if (Physics.SphereCast(from, 0.25f, d / length, out RaycastHit hit, length, ~0, QueryTriggerInteraction.Ignore))
                return from + d / length * Mathf.Max(0.6f, hit.distance - 0.1f);
            return want;
        }

        static bool Blocked(Vector3 a, Vector3 b) => Physics.Linecast(a, b, ~0, QueryTriggerInteraction.Ignore);

        float Ceiling(Vector3 head) =>
            Physics.Raycast(head, Vector3.up, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : stage.FloorY + 6f;

        static float FocusAhead(Vector3 position, Quaternion rotation) =>
            Physics.Raycast(position, rotation * Vector3.forward, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 10f;

        void Fallback(out Vector3 position, out Quaternion rotation)
        {
            if (data.TryCamera(lastT, out CameraSample view))
            {
                position = view.Position;
                rotation = view.Rotation;
            }
            else
            {
                position = cam.transform.position;
                rotation = cam.transform.rotation;
            }
        }

        void ShowLids(float closed)
        {
            bool show = closed > 0.01f;
            lids.gameObject.SetActive(show);
            if (!show) return;
            float d = cam.nearClipPlane + 0.02f;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f, w = h * cam.aspect * 1.05f;
            float cover = h * 0.5f * Mathf.Clamp01(closed) * 1.02f;
            topLid.localPosition = new Vector3(0f, h * 0.5f - cover * 0.5f, d);
            topLid.localScale = new Vector3(w, cover, 1f);
            bottomLid.localPosition = new Vector3(0f, -h * 0.5f + cover * 0.5f, d);
            bottomLid.localScale = new Vector3(w, cover, 1f);
        }

        // A keyframe for the path, from the camera as it is now.
        public ShotPath.Key KeyNow(float t) => new ShotPath.Key
        {
            t = t, position = cam.transform.position, rotation = cam.transform.rotation, fov = cam.fieldOfView, focus = Focus
        };

        public static bool TryParse(string name, out ShotPreset preset)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "pov": case "eyes": preset = ShotPreset.Pov; return true;
                case "cctv": case "corner": preset = ShotPreset.Cctv; return true;
                case "chase": preset = ShotPreset.Chase; return true;
                case "orbit": preset = ShotPreset.Orbit; return true;
                case "top": case "topdown": case "top-down": preset = ShotPreset.TopDown; return true;
                case "free": preset = ShotPreset.Free; return true;
                case "path": preset = ShotPreset.Path; return true;
            }
            preset = ShotPreset.Chase;
            return false;
        }
    }
}
