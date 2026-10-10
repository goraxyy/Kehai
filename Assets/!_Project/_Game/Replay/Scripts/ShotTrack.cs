using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Replay
{
    // Where Karen and you are in a rendered shot's picture, ten times a second: written into the
    // shot's sidecar so the editor can point an arrow or draw a circle at her without anyone
    // looking at the frames (tools/marketing, Phase 6).
    //
    // Each sample is [x, y, r]: the body's middle in the frame (0..1 from the top left) and half
    // its width as a fraction of the frame's width; null when the body is behind the camera,
    // outside the frame, hidden, or not in the store yet.
    public sealed class ShotTrack
    {
        public const int Hz = 10;
        const float HalfWidth = 0.35f;

        public readonly List<double[]> Karen = new List<double[]>();
        public readonly List<double[]> You = new List<double[]>();

        public int Count => Karen.Count;

        // The body's middle: halfway up Karen (2.05 m to the top of her head) and you (1.6 m to your eyes).
        public static float MiddleHeight(bool karen) => karen ? 1.05f : 0.85f;

        public static double[] Project(Camera cam, Vector3 middle)
        {
            Vector3 v = cam.WorldToViewportPoint(middle);
            if (v.z <= cam.nearClipPlane + 0.05f || v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return null;
            Vector3 side = cam.WorldToViewportPoint(middle + cam.transform.right * HalfWidth);
            return new[] { Round(v.x), Round(1f - v.y), Round(Mathf.Abs(side.x - v.x)) };
        }

        // A frame is due a sample when it reaches the next tenth of a second.
        public static bool Due(int frame, int fps, int count) => frame >= 0 && frame * Hz / fps >= count;

        public void SampleIfDue(int frame, int fps, Camera cam, ReplayData data, ReplayStage stage, float t)
        {
            if (!Due(frame, fps, Count)) return;
            Karen.Add(Where(cam, data, stage.KarenId, t, true));
            You.Add(Where(cam, data, stage.PlayerId, t, false));
        }

        static double[] Where(Camera cam, ReplayData data, int id, float t, bool karen)
        {
            if (id < 0 || !data.TryPose(id, t, out EntitySample pose) || !pose.Visible) return null;
            return Project(cam, pose.Position + Vector3.up * MiddleHeight(karen));
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["hz"] = Hz,
            ["karen"] = Karen,
            ["you"] = You,
        };

        static double Round(float v) => System.Math.Round(v, 3);
    }
}
