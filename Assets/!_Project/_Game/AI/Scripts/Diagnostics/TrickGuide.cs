using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kehai.Karen
{
    // What Karen can do, for the player to look through (0.4.1): the Esc menu's TRICKS tab.
    // One entry a trick: its name, what she does, how you notice, what you can do about it,
    // and a short video of it happening in the store.
    //
    // The words are here, in code, like the rest of the game's authored data. The videos are
    // media and never go in git: they're recorded from the game itself (tools/tricks/
    // record_tricks.sh: the trick check forces each trick in a real shift, and the replay
    // renderer films it) into Assets/StreamingAssets/TrickVideos/<id>.mp4, which .gitignore
    // leaves out and a build carries along. An entry without its video still shows its words.
    public static class TrickGuide
    {
        public sealed class Entry
        {
            public string Id;        // the trick's id in TacticLibrary, and its video's file name
            public string Title;
            public string Does;      // what she does
            public string Notice;    // what you see or hear
            public string Answer;    // what you can do
            public bool Chore;       // it leaves you a job
            public Entry(string id, string title, string does, string notice, string answer, bool chore = false)
            {
                Id = id; Title = title; Does = does; Notice = notice; Answer = answer; Chore = chore;
            }
        }

        public const string Folder = "TrickVideos";
        public const string Extension = ".mp4";

        // In the order a new player meets them: what she is, then her tricks from light to heavy.
        public static readonly Entry[] All =
        {
            new Entry("hunt", "The hunt",
                "She runs at where she thinks you are. She's a little faster than your sprint, and her touch ends the shift.",
                "Her light turns red and she comes straight for you.",
                "Break the line between her eyes and yours: a corner, a tall shelf, a door. Then move. She follows her guess, not you."),
            new Entry("poll_witnesses", "Ask the customers",
                "She walks up to a shopper who saw you and asks where you went.",
                "She stops beside a customer for a moment.",
                "Stay out of shoppers' sight when it matters, or lead them the wrong way first."),
            new Entry("ambush", "Ambush",
                "She stops and waits, silent, somewhere you're about to go: often on the way to a job she left you.",
                "Her footsteps stop. Nothing else.",
                "Look before you walk a route you take every time. Take the long way round once in a while."),
            new Entry("cut", "Cut",
                "During a hunt she leaves your trail and runs to where your route comes out.",
                "She breaks off the chase, and is ahead of you a moment later.",
                "Change direction after you lose her. Don't run to the same exit twice."),
            new Entry("throw_flour", "Throw",
                "During a hunt she throws what's at hand: powder that hangs in the air, detergent that's slippery, a tin that's loud.",
                "Something flies past you and bursts on the floor.",
                "Go round it. Powder hides you both; a slick floor slows you; a clatter tells her where you are."),
            new Entry("shelf_sweep", "Shelf sweep",
                "She stands at a full shelf on your way and knocks its stock onto the floor.",
                "A shelf rattles. The task list names the aisle.",
                "Restock the shelf. Mind the stock on the floor: it's loud underfoot.", true),
            new Entry("spill", "Spill",
                "She leaves a spill on a path you're likely to cross.",
                "A bucket clanks. A new spill on the task list.",
                "Mop it. Walk through it and you leave wet footprints she can follow.", true),
            new Entry("bin_tamper", "Bin tamper",
                "She tips rubbish back into a bin she's standing at.",
                "A bin lid clatters.",
                "Bag it again and take it out the back.", true),
            new Entry("tool_theft", "Tool theft",
                "She takes the mop from the WC and hides it where you rarely go.",
                "The mop rattles on its hook. The task list asks for it back.",
                "Find it and carry it back to its rack.", true),
            new Entry("phantom_chime", "Phantom chime",
                "The front doors open and shut by themselves, chiming, for twenty seconds.",
                "The door chime, again and again, and nobody comes in.",
                "Nothing has to be done. She's listening for whether you go and look."),
            new Entry("door_lock", "Block door",
                "She puts a restock box behind a door on your route, on the far side from you.",
                "A thud. The door won't open.",
                "Go round and pick the box up, or use another way.", true),
            new Entry("camera_bolt_on", "Camera bolt-on",
                "She stands a small camera of her own on a shelf, aimed where you usually are.",
                "Drilling. Then a pip a second while it can see you, and its red light goes solid.",
                "Hold E to unplug it, or keep out of its view.", true),
            new Entry("camera_watch", "Camera watch",
                "She goes to the office and watches the store's six cameras on the wall of screens. A camera that sees you tells her where you are.",
                "The office screens hum. She's standing at them with her back to the room.",
                "Stay out of the six rooms with a camera, or out of the cameras' view, until she leaves. You can watch her on the same screens."),
            new Entry("camera_break", "Camera break",
                "She shorts out the store's cameras. The office screens show static, for you and for her.",
                "A camera fizzes and its light goes out. The task list asks you to fix them.",
                "Hold the camera box in the backstreet for twenty seconds.", true),
            new Entry("fog", "Fog",
                "She fills the cold room or the stockroom with fog. Nobody in it sees further than two metres: not you, not her.",
                "The fridges hiss. You see the fog when you step into the room, not from outside.",
                "Hold the fridge control box in the backstreet for twenty seconds, or cross the room blind.", true),
            new Entry("blackout", "Blackout",
                "She cuts the lights: one wing of the store, or all of it.",
                "The lights whine, flicker and go out.",
                "Take the torch. Hold the breaker box in the backstreet for twenty seconds.", true),
            new Entry("understudy", "The understudy",
                "She sends in Kenji, the new hire. He follows you at a distance and tells her where you are.",
                "A man in a staff apron who keeps turning up behind you.",
                "Walk up to him and send him to the tills.", true),
            new Entry("overtime", "The overtime",
                "In the shift's last two minutes she stops the time clock. The countdown stops with it.",
                "The time clock buzzes. The task list asks you to restart it.",
                "Hold E on the time clock in the staff room to start it again.", true),
        };

        public static Entry Get(string id)
        {
            foreach (Entry e in All) if (e.Id == id) return e;
            return null;
        }

        // Where a trick's video would be, wherever one is found: beside the game (a build's
        // StreamingAssets, or the project's in the editor), else the player's own data folder.
        // Null when there's no video for it.
        public static string VideoPath(string id)
        {
            foreach (string folder in VideoFolders)
            {
                string path = Path.Combine(folder, id + Extension);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public static IEnumerable<string> VideoFolders
        {
            get
            {
                yield return Path.Combine(Application.streamingAssetsPath, Folder);
                yield return Path.Combine(Application.persistentDataPath, Folder);
            }
        }

        // How many of the entries have their video.
        public static int VideosFound
        {
            get
            {
                int n = 0;
                foreach (Entry e in All) if (VideoPath(e.Id) != null) n++;
                return n;
            }
        }
    }
}
