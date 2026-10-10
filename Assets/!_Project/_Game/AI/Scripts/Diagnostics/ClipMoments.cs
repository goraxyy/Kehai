using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Kehai.Karen
{
    // Turns a shift's clip markers into moments: markers no more than ClipMarkers.MergeGap
    // apart become one stretch of the shift, with each marker's pre- and post-roll around it,
    // kept inside the shift and no longer than ClipMarkers.MaxMoment (a long marker, like a
    // blackout, stretches its moment by at most ClipMarkers.MaxSpan). A moment scores the sum
    // of its markers' weights, times ClipMarkers.ChaseBoost when a chase or a catch is in it;
    // the best come first.
    //
    // At clock-out the recorder writes them next to the shift record:
    //   <persistent data>/shift_records/<stem>.markers.json
    public static class ClipMoments
    {
        static readonly string[] SubjectOrder = { "karen", "player", "customer", "store" };
        static readonly HashSet<string> StoryKinds = new HashSet<string>(Enum.GetNames(typeof(StoryKind)));
        public const int CaptionLines = 12;

        public static List<ClipMoment> Build(ShiftRecording r)
        {
            float length = r.Length;
            if (length <= 0f && r.Frames.Count > 0) length = r.Frames[r.Frames.Count - 1].T;
            foreach (ClipMarker m in r.Markers) length = Mathf.Max(length, m.End);
            return Build(r.Markers, r.ChaseSpans, r.Events, length);
        }

        public static List<ClipMoment> Build(IReadOnlyList<ClipMarker> markers, IReadOnlyList<Vector2> chaseSpans,
                                             IReadOnlyList<ShiftEvent> events, float length)
        {
            var sorted = markers.Where(m => m.Kind != null).OrderBy(m => m.T).ThenBy(m => m.End).ToList();

            var groups = new List<List<ClipMarker>>();
            List<ClipMarker> group = null;
            float groupEnd = float.MinValue, groupFrom = 0f, groupTo = 0f;
            foreach (ClipMarker m in sorted)
            {
                float from = m.T - m.Kind.PreRoll, to = Until(m) + m.Kind.PostRoll;
                bool joins = group != null && m.T - groupEnd <= ClipMarkers.MergeGap &&
                             Mathf.Max(groupTo, to) - Mathf.Min(groupFrom, from) <= ClipMarkers.MaxMoment;
                if (!joins)
                {
                    group = new List<ClipMarker>();
                    groups.Add(group);
                    groupEnd = Until(m);
                    groupFrom = from;
                    groupTo = to;
                }
                group.Add(m);
                groupEnd = Mathf.Max(groupEnd, Until(m));
                groupFrom = Mathf.Min(groupFrom, from);
                groupTo = Mathf.Max(groupTo, to);
            }

            var moments = new List<ClipMoment>();
            foreach (List<ClipMarker> g in groups)
            {
                var moment = new ClipMoment();
                float start = float.MaxValue, end = float.MinValue;
                int weight = 0;
                foreach (ClipMarker m in g)
                {
                    ClipMarkerKind k = m.Kind;
                    start = Mathf.Min(start, m.T - k.PreRoll);
                    end = Mathf.Max(end, Until(m) + k.PostRoll);
                    weight += k.Weight;
                    moment.Kept |= k.AlwaysKept;
                    moment.Chase |= m.Id == "catch" || m.Id == "escape";
                    foreach (string s in k.Subjects) if (!moment.Subjects.Contains(s)) moment.Subjects.Add(s);
                    foreach (string t in k.Tags) if (!moment.Tags.Contains(t)) moment.Tags.Add(t);
                    moment.Markers.Add(m);
                }
                moment.Start = Mathf.Clamp(start, 0f, Mathf.Max(0f, length));
                moment.End = Mathf.Clamp(end, moment.Start, Mathf.Max(moment.Start, length));

                if (chaseSpans != null)
                    foreach (Vector2 chase in chaseSpans)
                        if (chase.x <= moment.End && chase.y >= moment.Start) moment.Chase = true;
                if (moment.Chase && !moment.Tags.Contains("chase")) moment.Tags.Add("chase");
                moment.Subjects.Sort((a, b) => Order(a).CompareTo(Order(b)));
                moment.Score = weight * (moment.Chase ? ClipMarkers.ChaseBoost : 1f);

                if (events != null)
                    foreach (ShiftEvent e in events)
                    {
                        if (e.T < moment.Start || e.T > moment.End || !StoryKinds.Contains(e.Kind) || string.IsNullOrEmpty(e.Text)) continue;
                        if (moment.CaptionSeed.Count > 0 && moment.CaptionSeed[moment.CaptionSeed.Count - 1] == e.Text) continue;
                        moment.CaptionSeed.Add(e.Text);
                        if (moment.CaptionSeed.Count >= CaptionLines) break;
                    }

                if (moment.Score >= ClipMarkers.MinScore || moment.Kept) moments.Add(moment);
            }

            // Best first; equal scores in the order they happened.
            return moments.OrderByDescending(m => m.Score).ThenBy(m => m.Start).ToList();
        }

        // Where a marker stops counting for its moment: its end, but no more than MaxSpan in.
        static float Until(ClipMarker m) => Mathf.Min(m.End, m.T + ClipMarkers.MaxSpan);

        static int Order(string subject)
        {
            int i = Array.IndexOf(SubjectOrder, subject);
            return i < 0 ? SubjectOrder.Length : i;
        }

        // ---- JSON ------------------------------------------------------------------------

        // The file the marketing tools read: the shift, every marker, and the moments, best first.
        public static string FileJson(ShiftRecording r, string stem, List<ClipMoment> moments)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"version\":1,\"game\":\"").Append(MiniJson.EscapeInner(GameNames.Game))
              .Append("\",\"stem\":\"").Append(MiniJson.EscapeInner(stem))
              .Append("\",\"shift\":").Append(r.ShiftNumber)
              .Append(",\"started\":\"").Append(MiniJson.EscapeInner(r.StartedAt))
              .Append("\",\"length\":").Append(N(r.Length))
              .Append(",\"clockedOut\":").Append(r.ClockedOut ? "true" : "false")
              .Append(",\"rung\":\"").Append(MiniJson.EscapeInner(r.KarenRung)).Append('"');
            sb.Append(",\"markers\":");
            WriteMarkers(sb, r.Markers);
            sb.Append(",\"moments\":");
            WriteMoments(sb, moments);
            sb.Append('}');
            return sb.ToString();
        }

        // In the order they happened (some are only decided later, such as an escape).
        public static void WriteMarkers(StringBuilder sb, IReadOnlyList<ClipMarker> markers)
        {
            List<ClipMarker> ordered = markers != null ? markers.OrderBy(m => m.T).ToList() : new List<ClipMarker>();
            sb.Append('[');
            for (int i = 0; i < ordered.Count; i++)
            {
                ClipMarker m = ordered[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(MiniJson.EscapeInner(m.Id))
                  .Append("\",\"t\":").Append(N(m.T))
                  .Append(",\"end\":").Append(N(m.End))
                  .Append(",\"weight\":").Append(m.Kind != null ? m.Kind.Weight : 0);
                if (m.HasPlace) sb.Append(",\"x\":").Append(N(m.At.x)).Append(",\"y\":").Append(N(m.At.y));
                if (!float.IsNaN(m.Value)) sb.Append(",\"value\":").Append(N(m.Value));
                if (!string.IsNullOrEmpty(m.Text)) sb.Append(",\"text\":\"").Append(MiniJson.EscapeInner(m.Text)).Append('"');
                sb.Append('}');
            }
            sb.Append(']');
        }

        public static void WriteMoments(StringBuilder sb, IReadOnlyList<ClipMoment> moments)
        {
            sb.Append('[');
            for (int i = 0; moments != null && i < moments.Count; i++)
            {
                ClipMoment m = moments[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"rank\":").Append(i + 1)
                  .Append(",\"start\":").Append(N(m.Start))
                  .Append(",\"end\":").Append(N(m.End))
                  .Append(",\"score\":").Append(N(m.Score))
                  .Append(",\"chase\":").Append(m.Chase ? "true" : "false")
                  .Append(",\"kept\":").Append(m.Kept ? "true" : "false")
                  .Append(",\"markers\":[");
                for (int j = 0; j < m.Markers.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(MiniJson.EscapeInner(m.Markers[j].Id)).Append("\",\"t\":").Append(N(m.Markers[j].T)).Append('}');
                }
                sb.Append("],\"subjects\":");
                Strings(sb, m.Subjects);
                sb.Append(",\"tags\":");
                Strings(sb, m.Tags);
                sb.Append(",\"captionSeed\":");
                Strings(sb, m.CaptionSeed);
                sb.Append('}');
            }
            sb.Append(']');
        }

        static void Strings(StringBuilder sb, List<string> items)
        {
            sb.Append('[');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(MiniJson.EscapeInner(items[i])).Append('"');
            }
            sb.Append(']');
        }

        // JSON has no NaN or Infinity; a broken number becomes 0 rather than a broken file.
        static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : (Mathf.Round(v * 100f) / 100f).ToString(CultureInfo.InvariantCulture);
    }
}
