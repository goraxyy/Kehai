using System;
using System.IO;
using Kehai.Karen;
using Kehai.Replay;
using UnityEngine;

namespace Kehai.Sandbox
{
    // Records the actors and the fridge door without a store shift or the store's save system.
    // No sound events are written, so renders of these checks are silent.
    internal sealed class SandboxCheckRecording : IDisposable
    {
        const int PlayerId = 1, KarenId = 2, FridgeDoorId = 3;

        readonly PlayerPresence player;
        readonly KarenBody body;
        readonly SandboxDoor door;
        readonly KarenBrain brain;
        readonly ThoughtLog log;
        readonly Camera camera;
        readonly KrecWriter writer;
        readonly float start;
        readonly string path;
        float nextTick, nextCamera;

        public SandboxCheckRecording(string path, PursuitSandbox sandbox)
            : this(path, sandbox.Player, sandbox.Brain.Body, sandbox.FridgeDoor,
                new KrecHeader { Scene = PursuitCheckPlayback.SceneId, Seed = sandbox.Brain.Rng.Seed,
                    Rung = sandbox.Brain.config.rung.ToString(), Shift = sandbox.Brain.Stats.Shift }, sandbox.Brain)
        {
        }

        public SandboxCheckRecording(string path, SightSandbox sandbox)
            : this(path, sandbox.Player, sandbox.Karen.Body,
                sandbox.Fridge.transform.parent.GetComponentInChildren<SandboxDoor>(),
                new KrecHeader { Scene = SightSandbox.ReplayScene, Rung = sandbox.Karen.Config.rung.ToString() })
        {
        }

        SandboxCheckRecording(string path, PlayerPresence player, KarenBody body, SandboxDoor door, KrecHeader header, KarenBrain brain = null)
        {
            this.path = path;
            this.player = player;
            this.body = body;
            this.door = door;
            this.brain = brain;
            log = brain?.Log;
            camera = player.GetComponentInChildren<Camera>();
            start = Time.time;
            header.Stem = Path.GetFileNameWithoutExtension(path);
            header.Started = DateTime.UtcNow.ToString("O");
            writer = new KrecWriter(path + ".part");
            writer.Header(header);
            writer.Tick(0f);
            Transform actor = this.player.transform;
            Transform karen = body.transform;
            Transform hinge = this.door.transform;
            writer.Spawn(PlayerId, KrecKind.Player, "Player", "you", actor.position, actor.rotation, PlayerState, true);
            writer.Spawn(KarenId, KrecKind.Karen, "Karen", GameNames.Antagonist, karen.position, karen.rotation, KarenState, true);
            writer.Spawn(FridgeDoorId, KrecKind.Door, "FridgeDoor", "Fridge door", hinge.position, hinge.rotation, 0, true);
            Sample();
            if (log != null) log.Written += OnThought;
        }

        int PlayerState => (int)player.Motion;

        int KarenState
        {
            get
            {
                int state = (int)body.CurrentMood;
                if (body.Sight.SeesNow) state |= KrecState.KarenSees;
                if (brain != null && brain.IsChasing) state |= KrecState.KarenChasing;
                return state;
            }
        }

        public void Sample()
        {
            float t = Time.time - start;
            if (t >= nextTick)
            {
                nextTick = t + 1f / Krec.TickRate;
                RecordActors(t);
            }
            if (camera != null && t >= nextCamera)
            {
                nextCamera = t + 1f / Krec.CameraRate;
                writer.Camera(t, camera.transform.position, camera.transform.rotation, camera.fieldOfView, 0f, 0);
            }
        }

        void RecordActors(float t)
        {
            writer.Tick(t);
            Transform actor = this.player.transform;
            Transform karen = body.transform;
            Transform hinge = this.door.transform;
            writer.Pose(PlayerId, actor.position, actor.rotation, PlayerState, true);
            writer.Pose(KarenId, karen.position, karen.rotation, KarenState, true);
            writer.Pose(FridgeDoorId, hinge.position, hinge.rotation, 0, true);
        }

        public void Mark(string kind, string message) => writer.Thought(Time.time - start, kind, message);

        void OnThought(ThoughtRecord thought) => Mark(thought.Kind, thought.Text);

        public void Finish()
        {
            float t = Time.time - start;
            // Capture final state even if the last frame did not fall on a recording tick.
            RecordActors(t);
            writer.End(t, false);
            Dispose();
            File.Move(path + ".part", path);
        }

        public void Dispose()
        {
            if (log != null) log.Written -= OnThought;
            writer.Dispose();
        }
    }
}
