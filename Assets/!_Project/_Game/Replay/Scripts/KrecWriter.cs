using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Kehai.Replay
{
    // What a recording knows before its first tick: the shift, and the store as it was.
    public sealed class KrecHeader
    {
        public string Game = GameNames.Game, Stem = "", Started = "", Scene = "", Rung = "";
        public int Shift, Seed;

        // Karen's belief map is recorded on this grid (x across, z down the rows).
        public Vector2 BeliefOrigin;
        public float BeliefCell = Krec.BeliefCell;
        public int BeliefCols, BeliefRows;
        public byte[] BeliefMask = new byte[0];    // 1 where the store has floor

        public readonly List<(int bay, Vector3 from, Vector3 to)> MazeMoves = new List<(int, Vector3, Vector3)>();
        public readonly List<(Vector3 position, bool filled, string productId)> Slots = new List<(Vector3, bool, string)>();
        public readonly List<(Vector3 position, bool on)> Lights = new List<(Vector3, bool)>();
    }

    // Writes a .krec as the shift happens. Keeps each entity's last written state, so a Pose
    // carries only what changed and positions travel as millimetre deltas.
    public sealed class KrecWriter : IDisposable
    {
        sealed class Last
        {
            public Vector3Int Position;
            public Krec.Rotation Rotation;
            public int State;
            public bool Visible;
        }

        readonly FileStream file;
        readonly GZipStream zip;
        readonly BinaryWriter w;
        readonly Dictionary<int, Last> last = new Dictionary<int, Last>();
        Vector3Int cameraPosition;
        Krec.Rotation cameraRotation;
        int cameraFov = -1, cameraLids = -1, cameraHeld = -1;
        bool cameraStarted;
        byte[] lastBelief;

        public string Path { get; }
        public bool Closed { get; private set; }
        public long CompressedBytes => file.CanSeek ? file.Position : 0;

        public KrecWriter(string path)
        {
            Path = path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            zip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal);
            w = new BinaryWriter(zip);
        }

        public void Header(KrecHeader h)
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes(Krec.Magic));
            w.Write(Krec.Version);
            w.Write(h.Game ?? "");
            w.Write(h.Stem ?? "");
            w.Write(h.Shift);
            w.Write(h.Started ?? "");
            w.Write(h.Scene ?? "");
            w.Write(h.Seed);
            w.Write(h.Rung ?? "");
            w.Write(Krec.TickRate);
            w.Write(Krec.CameraRate);
            w.Write(Krec.BeliefRate);
            w.Write(Krec.PositionScale);

            w.Write(h.BeliefOrigin.x);
            w.Write(h.BeliefOrigin.y);
            w.Write(h.BeliefCell);
            w.Write(h.BeliefCols);
            w.Write(h.BeliefRows);
            w.Write(h.BeliefMask.Length);
            w.Write(h.BeliefMask);

            w.Write(h.MazeMoves.Count);
            foreach (var m in h.MazeMoves)
            {
                Krec.WriteVarint(w, m.bay);
                Krec.WritePosition(w, Krec.Quantise(m.from));
                Krec.WritePosition(w, Krec.Quantise(m.to));
            }
            w.Write(h.Slots.Count);
            foreach (var s in h.Slots)
            {
                Krec.WritePosition(w, Krec.Quantise(s.position));
                w.Write(s.filled);
                w.Write(s.productId ?? "");
            }
            w.Write(h.Lights.Count);
            foreach (var l in h.Lights)
            {
                Krec.WritePosition(w, Krec.Quantise(l.position));
                w.Write(l.on);
            }
        }

        public void Tick(float t)
        {
            w.Write((byte)KrecTag.Tick);
            w.Write(t);
        }

        public void Spawn(int id, KrecKind kind, string key, string label, Vector3 position, Quaternion rotation, int state, bool visible)
        {
            var l = new Last { Position = Krec.Quantise(position), Rotation = Krec.Compress(rotation), State = state, Visible = visible };
            last[id] = l;
            w.Write((byte)KrecTag.Spawn);
            Krec.WriteVarint(w, id);
            w.Write((byte)kind);
            w.Write(key ?? "");
            w.Write(label ?? "");
            Krec.WritePosition(w, l.Position);
            Krec.WriteRotation(w, l.Rotation);
            Krec.WriteVarint(w, state);
            w.Write(visible);
        }

        public bool IsSpawned(int id) => last.ContainsKey(id);

        // Writes what changed since the entity's last sample; nothing if nothing did.
        public bool Pose(int id, Vector3 position, Quaternion rotation, int state, bool visible)
        {
            if (!last.TryGetValue(id, out Last l)) return false;
            Vector3Int p = Krec.Quantise(position);
            Krec.Rotation r = Krec.Compress(rotation);
            int mask = 0;
            if (p != l.Position) mask |= 1;
            if (!r.Equals(l.Rotation)) mask |= 2;
            if (state != l.State) mask |= 4;
            if (visible != l.Visible) mask |= 8;
            if (mask == 0) return false;

            w.Write((byte)KrecTag.Pose);
            Krec.WriteVarint(w, id);
            w.Write((byte)mask);
            if ((mask & 1) != 0)
            {
                Krec.WriteVarint(w, p.x - l.Position.x);
                Krec.WriteVarint(w, p.y - l.Position.y);
                Krec.WriteVarint(w, p.z - l.Position.z);
                l.Position = p;
            }
            if ((mask & 2) != 0)
            {
                Krec.WriteRotation(w, r);
                l.Rotation = r;
            }
            if ((mask & 4) != 0)
            {
                Krec.WriteVarint(w, state);
                l.State = state;
            }
            if ((mask & 8) != 0)
            {
                w.Write(visible);
                l.Visible = visible;
            }
            return true;
        }

        public void Despawn(int id)
        {
            if (!last.Remove(id)) return;
            w.Write((byte)KrecTag.Despawn);
            Krec.WriteVarint(w, id);
        }

        // The player's view, every camera tick (an empty record when nothing changed, so the
        // replay knows the view held still).
        public void Camera(float t, Vector3 position, Quaternion rotation, float fov, float eyelidsClosed01, int heldId)
        {
            Vector3Int p = Krec.Quantise(position);
            Krec.Rotation r = Krec.Compress(rotation);
            int f = Mathf.RoundToInt(fov * 100f);
            int lids = Mathf.RoundToInt(Mathf.Clamp01(eyelidsClosed01) * 255f);
            int mask = 0;
            if (!cameraStarted || p != cameraPosition) mask |= 1;
            if (!cameraStarted || !r.Equals(cameraRotation)) mask |= 2;
            if (f != cameraFov) mask |= 4;
            if (lids != cameraLids) mask |= 8;
            if (heldId != cameraHeld) mask |= 16;

            w.Write((byte)KrecTag.Camera);
            w.Write(t);
            w.Write((byte)mask);
            if ((mask & 1) != 0)
            {
                if (!cameraStarted) Krec.WritePosition(w, p);
                else
                {
                    Krec.WriteVarint(w, p.x - cameraPosition.x);
                    Krec.WriteVarint(w, p.y - cameraPosition.y);
                    Krec.WriteVarint(w, p.z - cameraPosition.z);
                }
                cameraPosition = p;
            }
            if ((mask & 2) != 0) { Krec.WriteRotation(w, r); cameraRotation = r; }
            if ((mask & 4) != 0) { Krec.WriteVarint(w, f); cameraFov = f; }
            if ((mask & 8) != 0) { w.Write((byte)lids); cameraLids = lids; }
            if ((mask & 16) != 0) { Krec.WriteVarint(w, heldId); cameraHeld = heldId; }
            cameraStarted = true;
        }

        // ---- events ----------------------------------------------------------------------------

        void Event(float t, KrecEvent type)
        {
            w.Write((byte)KrecTag.Event);
            w.Write(t);
            w.Write((byte)type);
        }

        public void Noise(float t, int kind, int author, Vector3 at, float loudness)
        {
            Event(t, KrecEvent.Noise);
            w.Write((byte)kind);
            w.Write((byte)author);
            Krec.WritePosition(w, Krec.Quantise(at));
            w.Write(loudness);
        }

        public void Tell(float t, int kind, Vector3 at, float lead)
        {
            Event(t, KrecEvent.Tell);
            w.Write((byte)kind);
            Krec.WritePosition(w, Krec.Quantise(at));
            w.Write(lead);
        }

        public void Text(float t, KrecEvent type, string text)
        {
            Event(t, type);
            w.Write(text ?? "");
        }

        public void Circuits(float t, int bits)
        {
            Event(t, KrecEvent.Circuits);
            w.Write((byte)bits);
        }

        public void Thought(float t, string kind, string text)
        {
            Event(t, KrecEvent.Thought);
            w.Write(kind ?? "");
            w.Write(text ?? "");
        }

        public void Story(float t, int kind, string text)
        {
            Event(t, KrecEvent.Story);
            w.Write((byte)kind);
            w.Write(text ?? "");
        }

        public void Slot(float t, int index, bool filled, string productId)
        {
            Event(t, KrecEvent.Slot);
            Krec.WriteVarint(w, index);
            w.Write(filled);
            w.Write(productId ?? "");
        }

        public void Lights(float t, List<(int index, bool on)> changed)
        {
            Event(t, KrecEvent.Lights);
            Krec.WriteVarint(w, changed.Count);
            foreach (var c in changed)
            {
                Krec.WriteVarint(w, c.index);
                w.Write(c.on);
            }
        }

        // ---- her belief map ----------------------------------------------------------------------

        public void Belief(float t, Vector2 peak, float confidence, byte[] grid)
        {
            w.Write((byte)KrecTag.Belief);
            w.Write(t);
            w.Write(Krec.Q(peak.x));
            w.Write(Krec.Q(peak.y));
            w.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01(confidence) * 65535f));
            Krec.WriteVarint(w, grid.Length);
            if (lastBelief == null || lastBelief.Length != grid.Length) lastBelief = new byte[grid.Length];
            for (int i = 0; i < grid.Length; i++)
            {
                w.Write((byte)(grid[i] ^ lastBelief[i]));
                lastBelief[i] = grid[i];
            }
        }

        public void End(float length, bool clockedOut)
        {
            if (Closed) return;
            w.Write((byte)KrecTag.End);
            w.Write(length);
            w.Write(clockedOut);
            Dispose();
        }

        public void Dispose()
        {
            if (Closed) return;
            Closed = true;
            w.Flush();
            w.Dispose();   // closes the gzip stream and the file
        }
    }
}
