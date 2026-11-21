using System;
using System.IO;
using Kehai.Karen;
using Kehai.Replay;
using UnityEngine;

namespace Kehai.Sandbox
{
    // Records the actors and the fridge door without a store shift or the store's save system.
    // No sound events are written, so renders of these checks are silent.
    internal sealed class PursuitCheckRecording : IDisposable
    {
        const int PlayerId = 1, KarenId = 2, FridgeDoorId = 3;

        readonly PursuitSandbox sandbox;
        readonly ThoughtLog log;
        readonly Camera camera;
        readonly KrecWriter writer;
        readonly float start;
        readonly string path;
        float nextTick, nextCamera;

        public PursuitCheckRecording(string path, PursuitSandbox sandbox)
        {
            this.path = path;
            this.sandbox = sandbox;
            log = sandbox.Brain.Log;
            camera = sandbox.Player.GetComponentInChildren<Camera>();
            start = Time.time;
            writer = new KrecWriter(path + ".part");
            writer.Header(new KrecHeader
            {
                Scene = PursuitCheckPlayback.SceneId,
                Stem = Path.GetFileNameWithoutExtension(path),
                Started = DateTime.UtcNow.ToString("O"),
                Seed = sandbox.Brain.Rng.Seed,
                Rung = sandbox.Brain.config.rung.ToString(),
                Shift = sandbox.Brain.Stats.Shift
            });
            writer.Tick(0f);
            Transform player = sandbox.Player.transform;
            Transform karen = sandbox.Brain.Body.transform;
            Transform door = sandbox.FridgeDoor.transform;
            writer.Spawn(PlayerId, KrecKind.Player, "Player", "you", player.position, player.rotation, PlayerState, true);
            writer.Spawn(KarenId, KrecKind.Karen, "Karen", GameNames.Antagonist, karen.position, karen.rotation, KarenState, true);
            writer.Spawn(FridgeDoorId, KrecKind.Door, "FridgeDoor", "Fridge door", door.position, door.rotation, 0, true);
            Sample();
            log.Written += OnThought;
        }

        int PlayerState => (int)sandbox.Player.Motion;

        int KarenState
        {
            get
            {
                KarenBrain brain = sandbox.Brain;
                int state = (int)brain.Body.CurrentMood;
                if (brain.Body.Sight.SeesNow) state |= KrecState.KarenSees;
                if (brain.IsChasing) state |= KrecState.KarenChasing;
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
            Transform player = sandbox.Player.transform;
            Transform karen = sandbox.Brain.Body.transform;
            Transform door = sandbox.FridgeDoor.transform;
            writer.Pose(PlayerId, player.position, player.rotation, PlayerState, true);
            writer.Pose(KarenId, karen.position, karen.rotation, KarenState, true);
            writer.Pose(FridgeDoorId, door.position, door.rotation, 0, true);
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
            log.Written -= OnThought;
            writer.Dispose();
        }
    }
}
