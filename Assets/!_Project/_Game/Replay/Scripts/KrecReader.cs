using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Kehai.Replay
{
    public struct EntitySample
    {
        public float T;
        public Vector3 Position;
        public Quaternion Rotation;
        public int State;
        public bool Visible;
    }

    // One thing that moved (or could have) during the recording, and its samples.
    public sealed class ReplayEntity
    {
        public int Id;
        public KrecKind Kind;
        public string Key, Label;
        public float Spawned, Despawned = float.PositiveInfinity;
        public readonly List<EntitySample> Samples = new List<EntitySample>();
    }

    public struct CameraSample
    {
        public float T;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Fov;
        public float Eyelids;       // 0 open … 1 closed
        public int Held;            // the entity in the player's hand; 0 for none
    }

    public struct ReplayEvent
    {
        public float T;
        public KrecEvent Type;
        public int Kind, Author, Index;
        public Vector3 Position;
        public float Value;
        public bool Flag;
        public string Text, Extra;
        public List<(int index, bool on)> Lights;
    }

    public sealed class BeliefFrame
    {
        public float T;
        public Vector2 Peak;
        public float Confidence;
        public byte[] Grid;
    }

    // Her guesses at your route at one moment, once a hunt has lost you (Karen 0.2.0): each
    // with its weight, its path on the floor and how far along it each point is. A frame with
    // no guesses means she has dropped them.
    public sealed class RouteFrame
    {
        public float T;
        public float LostAt;                  // when she last saw you, in replay time
        public Vector3 Seen;                  // where she last saw you
        public float Sprint, Walk, Crouch;    // the speeds the windows assume, m/s
        public RouteLine[] Guesses = new RouteLine[0];

        // When you could be at point i of a guess, in replay time: earliest at a sprint, likely
        // at a walk, latest crouching.
        public (float earliest, float likely, float latest) Window(RouteLine g, int i) =>
            (LostAt + g.Along[i] / Sprint, LostAt + g.Along[i] / Walk, LostAt + g.Along[i] / Crouch);
    }

    public sealed class RouteLine
    {
        public string Name;       // "to the time clock", "turned back", "hiding close by"
        public int Kind;          // 0 a goal, 1 turned back, 2 hiding (Karen's GuessKind)
        public float Weight;      // the guesses' weights add up to 1
        public Vector3[] Path;    // her cells' centres, from where she last saw you
        public float[] Along;     // metres along the path to each point
    }

    // A whole recording, read back: the timeline of every entity, the view, the events and
    // her belief map. `TryPose` answers "where was it at t", the way the replay will ask.
    public sealed class ReplayData
    {
        public KrecHeader Header = new KrecHeader();
        public ushort Version;
        public float Length;
        public bool ClockedOut, Complete;
        public readonly List<float> Ticks = new List<float>();
        public readonly Dictionary<int, ReplayEntity> Entities = new Dictionary<int, ReplayEntity>();
        public readonly List<CameraSample> Camera = new List<CameraSample>();
        public readonly List<ReplayEvent> Events = new List<ReplayEvent>();
        public readonly List<BeliefFrame> Belief = new List<BeliefFrame>();
        public readonly List<RouteFrame> Routes = new List<RouteFrame>();

        public float End => Complete ? Length : Ticks.Count > 0 ? Ticks[Ticks.Count - 1] : 0f;

        // Samples are written only when something changed, so between two samples an entity
        // held still until the tick before the later one, then moved to it.
        public bool TryPose(int id, float t, out EntitySample pose)
        {
            pose = default;
            if (!Entities.TryGetValue(id, out ReplayEntity e) || e.Samples.Count == 0) return false;
            if (t < e.Spawned || t >= e.Despawned) return false;
            List<EntitySample> s = e.Samples;
            int i = LastAtOrBefore(s, t);
            if (i < 0) return false;
            pose = s[i];
            if (i + 1 >= s.Count || s[i].T == t) return true;

            EntitySample next = s[i + 1];
            float from = Math.Max(s[i].T, TickBefore(next.T));
            if (t <= from || next.T <= from) return true;
            float k = (t - from) / (next.T - from);
            pose.T = t;
            pose.Position = Vector3.Lerp(s[i].Position, next.Position, k);
            pose.Rotation = Quaternion.Slerp(s[i].Rotation, next.Rotation, k);
            return true;
        }

        public bool TryCamera(float t, out CameraSample view)
        {
            view = default;
            int lo = 0, hi = Camera.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Camera[mid].T <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (found < 0) return false;
            view = Camera[found];
            if (found + 1 < Camera.Count && Camera[found].T < t)
            {
                CameraSample next = Camera[found + 1];
                float k = (t - view.T) / Mathf.Max(1e-4f, next.T - view.T);
                view.Position = Vector3.Lerp(view.Position, next.Position, k);
                view.Rotation = Quaternion.Slerp(view.Rotation, next.Rotation, k);
                view.T = t;
            }
            return true;
        }

        public BeliefFrame BeliefAt(float t)
        {
            int lo = 0, hi = Belief.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Belief[mid].T <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return found >= 0 ? Belief[found] : null;
        }

        // Her route guesses in force at t: null before the first, and none once she drops them.
        public RouteFrame RoutesAt(float t)
        {
            int lo = 0, hi = Routes.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Routes[mid].T <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return found >= 0 ? Routes[found] : null;
        }

        // The first event at or after t (events are written in the order they happened).
        public int FirstEventAt(float t)
        {
            int lo = 0, hi = Events.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Events[mid].T < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        static int LastAtOrBefore(List<EntitySample> s, float t)
        {
            int lo = 0, hi = s.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (s[mid].T <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return found;
        }

        float TickBefore(float t)
        {
            int lo = 0, hi = Ticks.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (Ticks[mid] < t) { found = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return found >= 0 ? Ticks[found] : float.NegativeInfinity;
        }
    }

    // The file isn't a recording this game can read (as opposed to one that was cut short).
    public sealed class KrecFormatException : Exception
    {
        public KrecFormatException(string message) : base(message) { }
    }

    public static class KrecReader
    {
        public static ReplayData Load(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return Read(file);
        }

        // Only the header: which shift, which scene (to load the right store before the rest).
        public static KrecHeader LoadHeader(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new GZipStream(file, CompressionMode.Decompress))
            using (var r = new BinaryReader(zip))
            {
                var data = new ReplayData();
                ReadHeader(r, data);
                return data.Header;
            }
        }

        // Reads as much as there is: a recording cut short (the game quit) still loads.
        public static ReplayData Read(Stream stream)
        {
            var data = new ReplayData();
            using (var zip = new GZipStream(stream, CompressionMode.Decompress, true))
            using (var r = new BinaryReader(zip))
            {
                ReadHeader(r, data);
                var last = new Dictionary<int, EntitySample>();
                var quantised = new Dictionary<int, Vector3Int>();
                CameraSample view = default;
                bool viewStarted = false;
                Vector3Int viewQ = default;
                byte[] belief = null;
                RouteFrame routes = null;
                float t = 0f;

                while (true)
                {
                    int tag;
                    try { tag = r.ReadByte(); }
                    catch (Exception e) when (CutShort(e)) { break; }
                    try
                    {
                        switch ((KrecTag)tag)
                        {
                            case KrecTag.Tick:
                                t = r.ReadSingle();
                                data.Ticks.Add(t);
                                break;

                            case KrecTag.Spawn:
                            {
                                int id = Krec.ReadVarint(r);
                                var e = new ReplayEntity { Id = id, Kind = (KrecKind)r.ReadByte(), Key = r.ReadString(), Label = r.ReadString(), Spawned = t };
                                var sample = new EntitySample
                                {
                                    T = t, Position = Krec.Position(Krec.ReadPosition(r)), Rotation = Krec.Expand(Krec.ReadRotation(r)),
                                    State = Krec.ReadVarint(r), Visible = r.ReadBoolean()
                                };
                                e.Samples.Add(sample);
                                data.Entities[id] = e;
                                last[id] = sample;
                                quantised[id] = Krec.Quantise(sample.Position);
                                break;
                            }

                            case KrecTag.Pose:
                            {
                                int id = Krec.ReadVarint(r);
                                int mask = r.ReadByte();
                                EntitySample s = last[id];
                                s.T = t;
                                if ((mask & 1) != 0)
                                {
                                    Vector3Int q = quantised[id];
                                    q.x += Krec.ReadVarint(r);
                                    q.y += Krec.ReadVarint(r);
                                    q.z += Krec.ReadVarint(r);
                                    quantised[id] = q;
                                    s.Position = Krec.Position(q);
                                }
                                if ((mask & 2) != 0) s.Rotation = Krec.Expand(Krec.ReadRotation(r));
                                if ((mask & 4) != 0) s.State = Krec.ReadVarint(r);
                                if ((mask & 8) != 0) s.Visible = r.ReadBoolean();
                                last[id] = s;
                                data.Entities[id].Samples.Add(s);
                                break;
                            }

                            case KrecTag.Despawn:
                            {
                                int id = Krec.ReadVarint(r);
                                if (data.Entities.TryGetValue(id, out ReplayEntity e)) e.Despawned = t;
                                last.Remove(id);
                                break;
                            }

                            case KrecTag.Camera:
                            {
                                view.T = r.ReadSingle();
                                int mask = r.ReadByte();
                                if ((mask & 1) != 0)
                                {
                                    if (!viewStarted) viewQ = Krec.ReadPosition(r);
                                    else
                                    {
                                        viewQ.x += Krec.ReadVarint(r);
                                        viewQ.y += Krec.ReadVarint(r);
                                        viewQ.z += Krec.ReadVarint(r);
                                    }
                                    view.Position = Krec.Position(viewQ);
                                }
                                if ((mask & 2) != 0) view.Rotation = Krec.Expand(Krec.ReadRotation(r));
                                if ((mask & 4) != 0) view.Fov = Krec.ReadVarint(r) / 100f;
                                if ((mask & 8) != 0) view.Eyelids = r.ReadByte() / 255f;
                                if ((mask & 16) != 0) view.Held = Krec.ReadVarint(r);
                                viewStarted = true;
                                data.Camera.Add(view);
                                break;
                            }

                            case KrecTag.Event:
                                data.Events.Add(ReadEvent(r));
                                break;

                            case KrecTag.Belief:
                            {
                                var b = new BeliefFrame { T = r.ReadSingle() };
                                b.Peak = new Vector2(Krec.Metres(r.ReadInt32()), Krec.Metres(r.ReadInt32()));
                                b.Confidence = r.ReadUInt16() / 65535f;
                                int n = Krec.ReadVarint(r);
                                if (belief == null || belief.Length != n) belief = new byte[n];
                                b.Grid = new byte[n];
                                for (int i = 0; i < n; i++) b.Grid[i] = belief[i] = (byte)(belief[i] ^ r.ReadByte());
                                data.Belief.Add(b);
                                break;
                            }

                            case KrecTag.Routes:
                                routes = ReadRoutes(r, routes);
                                data.Routes.Add(routes);
                                break;

                            case KrecTag.End:
                                data.Length = r.ReadSingle();
                                data.ClockedOut = r.ReadBoolean();
                                data.Complete = true;
                                return data;

                            default:
                                throw new KrecFormatException($"unknown record {tag}");
                        }
                    }
                    catch (Exception e) when (CutShort(e)) { break; }
                }
            }
            return data;
        }

        // New guesses come whole; a weights-only frame keeps the routes of the one before it.
        static RouteFrame ReadRoutes(BinaryReader r, RouteFrame last)
        {
            var f = new RouteFrame { T = r.ReadSingle() };
            byte what = r.ReadByte();
            switch (what)
            {
                case 0:
                    return f;

                case 1:
                    f.LostAt = r.ReadSingle();
                    f.Seen = Krec.Position(Krec.ReadPosition(r));
                    f.Sprint = r.ReadSingle();
                    f.Walk = r.ReadSingle();
                    f.Crouch = r.ReadSingle();
                    f.Guesses = new RouteLine[r.ReadByte()];
                    for (int i = 0; i < f.Guesses.Length; i++)
                    {
                        var g = new RouteLine { Name = r.ReadString(), Kind = r.ReadByte(), Weight = r.ReadUInt16() / 65535f };
                        int n = Krec.ReadVarint(r);
                        if (n < 0 || n > 100000) throw new KrecFormatException($"a route of {n} points");
                        g.Path = new Vector3[n];
                        g.Along = new float[n];
                        Vector3Int at = default;
                        int along = 0;
                        for (int k = 0; k < n; k++)
                        {
                            at += new Vector3Int(Krec.ReadVarint(r), Krec.ReadVarint(r), Krec.ReadVarint(r));
                            along += Krec.ReadVarint(r);
                            g.Path[k] = Krec.Position(at);
                            g.Along[k] = along / 100f;
                        }
                        f.Guesses[i] = g;
                    }
                    return f;

                case 2:
                {
                    int count = r.ReadByte();
                    if (last == null || last.Guesses.Length != count) throw new KrecFormatException("route weights without their routes");
                    f.LostAt = last.LostAt;
                    f.Seen = last.Seen;
                    f.Sprint = last.Sprint;
                    f.Walk = last.Walk;
                    f.Crouch = last.Crouch;
                    f.Guesses = new RouteLine[count];
                    for (int i = 0; i < count; i++)
                    {
                        RouteLine g = last.Guesses[i];
                        f.Guesses[i] = new RouteLine { Name = g.Name, Kind = g.Kind, Weight = r.ReadUInt16() / 65535f, Path = g.Path, Along = g.Along };
                    }
                    return f;
                }

                default:
                    throw new KrecFormatException($"unknown routes record {what}");
            }
        }

        // The end of what was written: the game quit mid-shift, or the file was copied half-done.
        static bool CutShort(Exception e) => !(e is KrecFormatException) && (e is EndOfStreamException || e is IOException || e is InvalidDataException);

        static void ReadHeader(BinaryReader r, ReplayData data)
        {
            string magic = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
            if (magic != Krec.Magic) throw new KrecFormatException("not a .krec file");
            data.Version = r.ReadUInt16();
            if (data.Version > Krec.Version) throw new KrecFormatException($".krec version {data.Version} is newer than this game ({Krec.Version})");
            KrecHeader h = data.Header;
            h.Game = r.ReadString();
            h.Stem = r.ReadString();
            h.Shift = r.ReadInt32();
            h.Started = r.ReadString();
            h.Scene = r.ReadString();
            h.Seed = r.ReadInt32();
            h.Rung = r.ReadString();
            r.ReadSingle(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();   // rates and scale, as written

            h.BeliefOrigin = new Vector2(r.ReadSingle(), r.ReadSingle());
            h.BeliefCell = r.ReadSingle();
            h.BeliefCols = r.ReadInt32();
            h.BeliefRows = r.ReadInt32();
            h.BeliefMask = r.ReadBytes(r.ReadInt32());

            int moves = r.ReadInt32();
            for (int i = 0; i < moves; i++)
                h.MazeMoves.Add((Krec.ReadVarint(r), Krec.Position(Krec.ReadPosition(r)), Krec.Position(Krec.ReadPosition(r))));
            int slots = r.ReadInt32();
            for (int i = 0; i < slots; i++)
                h.Slots.Add((Krec.Position(Krec.ReadPosition(r)), r.ReadBoolean(), r.ReadString()));
            int lights = r.ReadInt32();
            for (int i = 0; i < lights; i++)
                h.Lights.Add((Krec.Position(Krec.ReadPosition(r)), r.ReadBoolean()));
        }

        static ReplayEvent ReadEvent(BinaryReader r)
        {
            var e = new ReplayEvent { T = r.ReadSingle(), Type = (KrecEvent)r.ReadByte() };
            switch (e.Type)
            {
                case KrecEvent.Noise:
                    e.Kind = r.ReadByte();
                    e.Author = r.ReadByte();
                    e.Position = Krec.Position(Krec.ReadPosition(r));
                    e.Value = r.ReadSingle();
                    break;
                case KrecEvent.Tell:
                    e.Kind = r.ReadByte();
                    e.Position = Krec.Position(Krec.ReadPosition(r));
                    e.Value = r.ReadSingle();
                    break;
                case KrecEvent.PaChime:
                case KrecEvent.PaSpeech:
                    e.Text = r.ReadString();
                    break;
                case KrecEvent.Circuits:
                    e.Kind = r.ReadByte();
                    break;
                case KrecEvent.Thought:
                    e.Extra = r.ReadString();
                    e.Text = r.ReadString();
                    break;
                case KrecEvent.Story:
                    e.Kind = r.ReadByte();
                    e.Text = r.ReadString();
                    break;
                case KrecEvent.Slot:
                    e.Index = Krec.ReadVarint(r);
                    e.Flag = r.ReadBoolean();
                    e.Text = r.ReadString();
                    break;
                case KrecEvent.Lights:
                    int n = Krec.ReadVarint(r);
                    e.Lights = new List<(int, bool)>(n);
                    for (int i = 0; i < n; i++) e.Lights.Add((Krec.ReadVarint(r), r.ReadBoolean()));
                    break;
                case KrecEvent.Fog:
                    e.Text = r.ReadString();
                    e.Value = r.ReadSingle();
                    break;
                case KrecEvent.Clock:
                    e.Flag = r.ReadBoolean();
                    break;
                default:
                    throw new KrecFormatException($"unknown event {e.Type}");
            }
            return e;
        }
    }
}
