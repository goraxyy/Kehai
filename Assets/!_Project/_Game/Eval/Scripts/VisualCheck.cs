using System.Collections;
using System.Collections.Generic;
using System.IO;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // The pictures for a pull request (0.4.1): what the visible changes look like in the real
    // store, taken by the game itself so nobody has to play to the right spot.
    //
    //   Unity -batchmode -projectPath <clone> -executeMethod EvalBatch.Play -kehai-shots <folder>
    //   Unity            -projectPath <clone> -executeMethod EvalBatch.Play -kehai-shots <folder>
    //
    // In batch mode (with graphics: no -nographics) it saves what a camera can render off
    // screen: the F1 map's drawing, each store camera's feed, the office screens, the boxes,
    // the fog from inside and from outside its room. Run in the editor's window (no -batchmode;
    // it mutes the editor first, see VisualCheckMute) it also saves the screen itself: the F1
    // map, the Esc menu's pages, the hold bar, the eyelids. Then it quits.
    public sealed class VisualCheck : MonoBehaviour
    {
        string folder;
        Camera eye;
        readonly List<string> saved = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnLoad()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-kehai-shots");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("~VisualCheck");
            DontDestroyOnLoad(go);
            go.AddComponent<VisualCheck>().folder = args[i + 1];
        }

        IEnumerator Start()
        {
            AudioListener.volume = 0f;                      // nothing this runs makes a sound
            Directory.CreateDirectory(folder);
            yield return Frames(30);                        // the store dresses itself in its first frames

            Map();
            yield return World();
            if (!Application.isBatchMode) yield return Screens();

            File.WriteAllLines(Path.Combine(folder, "shots.txt"), saved);
            Debug.Log($"VisualCheck: {saved.Count} pictures in {folder}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Seconds(float s)
        {
            float until = Time.unscaledTime + s;
            while (Time.unscaledTime < until) yield return null;
        }

        void Save(Texture2D picture, string name)
        {
            string path = Path.Combine(folder, name + ".png");
            File.WriteAllBytes(path, picture.EncodeToPNG());
            saved.Add(name + ".png");
        }

        // ---- the F1 map's drawing, before and after -----------------------------------------------

        void Map()
        {
            StoreMap map = StoreMap.Current;
            if (map == null) return;
            StoreFloorPlan plan = StoreFloorPlan.Build(map);
            Texture2D after = FloorPlanTexture.Render(plan, 12f);
            Save(after, "map_after");
            Destroy(after);

            // Before 0.4.1 the drawing was fitted round every walkable triangle, with no zones:
            // the same plan with its zones taken away and its bounds grown back over the floor.
            var before = StoreFloorPlan.Build(map);
            before.Zones.Clear();
            bool any = false;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (Vector2[] t in before.Floor)
                foreach (Vector2 p in t)
                {
                    if (!any) { minX = maxX = p.x; minY = maxY = p.y; any = true; continue; }
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
                }
            if (any) before.Bounds = Rect.MinMaxRect(minX - 2f, minY - 2f, maxX + 2f, maxY + 2f);
            Texture2D old = FloorPlanTexture.Render(before, 8f);
            Save(old, "map_before_fit");
            Destroy(old);
        }

        // ---- what a camera sees ----------------------------------------------------------------------

        Texture2D Render(Camera cam, int width = 1280, int height = 720)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture was = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = was;
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            return picture;
        }

        void Shot(Vector3 from, Vector3 at, string name, float fov = 60f)
        {
            if (eye == null)
            {
                eye = new GameObject("VisualCheck eye").AddComponent<Camera>();
                eye.enabled = false;
                eye.nearClipPlane = 0.05f;
                eye.farClipPlane = 120f;
            }
            eye.fieldOfView = fov;
            eye.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from));
            Texture2D picture = Render(eye);
            Save(picture, name);
            Destroy(picture);
        }

        // In front of a wall box, a little to one side, looking at it.
        void BoxShot(Transform box, string name, float back = 1.1f)
        {
            if (box == null) return;
            Shot(box.position + box.forward * back + box.right * 0.35f + Vector3.up * 0.12f, box.position, name, 50f);
        }

        static void MovePlayer(Vector3 to, Vector3 lookAt)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;
            var body = player.GetComponent<CharacterController>();
            if (body != null) body.enabled = false;
            if (Physics.Raycast(new Vector3(to.x, 2f, to.z), Vector3.down, out RaycastHit floor, 5f, ~0, QueryTriggerInteraction.Ignore)) to.y = floor.point.y + 0.05f;
            Vector3 flat = lookAt - to;
            flat.y = 0f;
            player.transform.SetPositionAndRotation(to, Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat : Vector3.forward));
            if (body != null) body.enabled = true;
            Physics.SyncTransforms();
        }

        void PlayerShot(string name)
        {
            Camera view = Camera.main;
            if (view == null) return;
            Texture2D picture = Render(view);
            Save(picture, name);
            Destroy(picture);
        }

        IEnumerator World()
        {
            StoreCameras cams = StoreCameras.Instance;
            if (cams != null)
            {
                // Each camera's own picture, and the camera itself from below.
                foreach (StoreCamera cam in cams.Cameras)
                {
                    cam.RenderFeed();
                    RenderTexture.active = cam.Picture;
                    var picture = new Texture2D(cam.Picture.width, cam.Picture.height, TextureFormat.RGB24, false);
                    picture.ReadPixels(new Rect(0, 0, cam.Picture.width, cam.Picture.height), 0, 0);
                    picture.Apply();
                    RenderTexture.active = null;
                    string room = cam.Spot.Area.Replace(' ', '_').ToLowerInvariant();
                    Save(picture, "feed_" + room);
                    Destroy(picture);
                    Vector3 below = cam.transform.position + cam.transform.forward * 1.6f + Vector3.down * 0.9f + cam.transform.right * 0.5f;
                    Shot(below, cam.transform.position, "camera_" + room, 40f);
                }

                // The office screens, working and broken.
                if (cams.Wall != null)
                {
                    Vector3 wall = cams.Wall.transform.position;
                    Vector3 seat = wall + cams.Wall.Front * 3.4f;
                    seat.y = wall.y + 0.1f;
                    cams.RenderAllNow();
                    yield return Frames(2);
                    Shot(seat, wall, "office_screens", 62f);
                    cams.Break();
                    yield return Frames(2);
                    Shot(seat, wall, "office_screens_broken", 62f);
                    BoxShot(cams.Box != null ? cams.Box.transform : null, "camera_box_broken");
                    cams.Fix();
                }
                BoxShot(cams.Box != null ? cams.Box.transform : null, "camera_box");
            }

            // The backstreet's boxes together, and each.
            FogBox fog = FindAnyObjectByType<FogBox>();
            ElectricBox breakers = FindAnyObjectByType<ElectricBox>();
            if (fog != null)
            {
                BoxShot(fog.transform, "fridge_box");
                Shot(fog.transform.position + fog.transform.forward * 2.6f + Vector3.up * 0.2f, fog.transform.position, "backstreet_boxes", 62f);
            }
            if (breakers != null && fog == null) Shot(breakers.transform.position + breakers.transform.forward * 2.5f, breakers.transform.position, "breaker_box");

            // The staff room wall: the time clock, the PA box, the PA microphone.
            ClosingMic mic = ClosingMic.Instance;
            if (mic != null)
            {
                BoxShot(mic.transform, "pa_microphone");
                Shot(mic.transform.position + mic.transform.forward * 2.2f - mic.transform.right * 0.5f + Vector3.up * 0.1f,
                     mic.transform.position - mic.transform.right * 0.5f, "staff_room_wall", 60f);
            }

            // Fog: in the stockroom, seen from inside it and from the sales floor looking in.
            var inside = new Vector3(34f, 0f, -182f);
            var outside = new Vector3(47f, 0f, -168f);
            MovePlayer(inside, inside + Vector3.right * 6f);
            yield return Frames(3);
            PlayerShot("stockroom_clear");
            FogState.Fill("Stockroom", 2f, 1f);
            yield return Seconds(6f);
            PlayerShot("stockroom_fog_inside");
            MovePlayer(outside, new Vector3(47f, 0f, -182f));
            yield return Seconds(1.5f);
            PlayerShot("stockroom_fog_from_sales_floor");
            FogState.Clear();
            yield return Seconds(4f);
        }

        // ---- the screen itself (the editor's window only) ------------------------------------------

        IEnumerator Screen(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            saved.Add(name + ".png");
            yield return Frames(3);
        }

        IEnumerator Screens()
        {
            MovePlayer(new Vector3(47f, 0f, -150f), new Vector3(47f, 0f, -160f));
            ShiftManager shift = FindAnyObjectByType<ShiftManager>();
            if (shift != null && !shift.IsShiftActive) shift.StartShift();
            yield return Seconds(3f);
            yield return Screen("hud");

            // The F1 map.
            KarenDebugOverlay overlay = FindAnyObjectByType<KarenDebugOverlay>();
            if (overlay != null)
            {
                overlay.showOverlay = true;
                FullScreenPanel.Set(overlay, true);
                yield return Seconds(1f);
                yield return Screen("f1_map");
                overlay.showOverlay = false;
                FullScreenPanel.Set(overlay, false);
            }

            // The Esc menu: settings, keys, tricks.
            SettingsMenu menu = SettingsMenu.Instance;
            if (menu != null)
            {
                menu.Open();
                yield return Frames(5);
                yield return Screen("esc_settings");
                menu.ShowTab(1);
                yield return Frames(5);
                yield return Screen("esc_keys");
                menu.ShowTab(2);
                yield return Seconds(1.5f);
                yield return Screen("esc_tricks");
                menu.Close();
            }

            // The eyelids: open, on the way, shut.
            Eyelids lids = FindAnyObjectByType<Eyelids>();
            if (lids != null)
            {
                lids.SetClosed(0.5f);
                yield return Screen("eyelids_half");
                lids.SetClosed(1f);
                yield return Screen("eyelids_shut");
                lids.SetClosed(0f);
            }

            // The hold bar at the fridge box, part-way through a hold.
            FogBox fog = FindAnyObjectByType<FogBox>();
            PlayerInteract hands = PlayerInteract.Local;
            if (fog != null && hands != null)
            {
                FogState.Fill("Stockroom", 2f, 1f);
                MovePlayer(fog.transform.position + fog.transform.forward * 1.2f, fog.transform.position);
                Camera view = Camera.main;
                if (view != null) view.transform.LookAt(fog.transform.position);
                hands.forcedTarget = fog;
                hands.forcedHold = true;
                yield return Seconds(7f);
                yield return Screen("hold_bar");
                hands.forcedHold = false;
                hands.forcedTarget = null;
                FogState.Clear();
            }
        }
    }
}
