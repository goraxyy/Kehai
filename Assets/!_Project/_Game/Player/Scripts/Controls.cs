using Kehai;

// Every key the game uses, in one place. The Esc menu's Keys tab shows this list, and
// CONTROLS.md is the same list to read outside the game (a test keeps the two in step).
public static class Controls
{
    public readonly struct Entry
    {
        public readonly string Keys, Action;
        public Entry(string keys, string action) { Keys = keys; Action = action; }
    }

    public readonly struct Section
    {
        public readonly string Title;
        public readonly Entry[] Entries;
        public Section(string title, params Entry[] entries) { Title = title; Entries = entries; }
    }

    static Entry K(string keys, string action) => new Entry(keys, action);

    // The section the 3D replay shows as its help (F1 there).
    public const string ReplaySection = "3D replay";

    public static readonly Section[] All =
    {
        new Section("Main menu",
            K("Up / Down, mouse", "Choose"),
            K("Enter / click", "Select"),
            K("Esc (main menu)", "Back, from a question or a settings page")),
        new Section("Moving",
            K("W A S D", "Walk"),
            K("Mouse", "Look around"),
            K("Left Shift (hold)", "Sprint; stands you up if you're crouching. Loud: " + GameNames.Antagonist + " hears it"),
            K("Left Ctrl (hold)", "Crouch while held; let go to stand up. Quiet, and slow"),
            K("Space", "Jump")),
        new Section("Hands and work",
            K("E", "Use what you're looking at: pick up, stock a shelf, open a door, serve, talk, switch the radio"),
            K("E (hold)", "Mop a spill (with the mop in hand), clear a crate wall, unplug a camera, scrub footprints"),
            K("E (nothing in view)", "Use what's in your hand, such as switching the torch on or off"),
            K("Q", "Put down what you're holding"),
            K("Q (hold, then let go)", "Throw it; hold longer to throw harder"),
            K("1 2 3 4 / mouse wheel", "Choose a hand slot"),
            K("C", "Show or hide the task list")),
        new Section("Customers",
            K("Up / Down", "Choose an answer when a customer asks you something"),
            K("E / Enter", "Give that answer")),
        new Section("Menus and maps",
            K("Esc", "Pause: restart the shift or leave for the main menu, volume, mouse, " + GameNames.Antagonist + "'s floor cone, webcam; closes any open panel"),
            K("F1", "Live map of the store and what " + GameNames.Antagonist + " is doing, in plain words"),
            K("H (on the F1 map)", GameNames.Antagonist + "'s guess of where you are, as a heat map"),
            K("T (on the F1 map)", "Technical view: goal scores and her thought log"),
            K("F2", "Replay the shift so far"),
            K("Space (in the replay)", "Play or pause"),
            K("1 / 2 / 3 (in the replay)", "Speed: 1x, 4x, 16x"),
            K("Left / Right (in the replay)", "Jump back or forward 5 seconds"),
            K("O (in the replay)", "Open the full shift report in your browser")),
        new Section("Blinking (webcam)",
            K("F8", "Turn webcam blink tracking on (asks first) or off"),
            K("Y / N", "Answer the webcam question"),
            K("F9", "Calibrate: eyes open, then closed until the beep, then 3 blinks"),
            K("F10", "Blink test panel: is the game reading your eyes?"),
            K("R (in the blink test)", "Restart the camera helper"),
            K("V (in the blink test)", "Switch to the next camera"),
            K("M (in the blink test)", "Switch between the Apple Vision and MediaPipe helpers"),
            K("B (hold)", "Close your eyes with the keyboard; tap it to blink")),
        new Section("After a shift",
            K("Enter", "Continue"),
            K("O", "Open the shift report in your browser"),
            K("R", "Watch the shift again in 3D"),
            K("Q", "Hand in your notice (from shift 5)"),
            K("Enter (after a career ends)", "Back to the main menu")),
        new Section(ReplaySection,
            K("Space", "Play or pause"),
            K("Left / Right", "Back or forward 5 seconds"),
            K(", / .", "Back or forward one frame (pauses)"),
            K("- / =", "Slower or faster, from 0.1x to 4x"),
            K("[ / ]", "The previous or next clip moment"),
            K("Home / End", "The start or the end of the shift"),
            K("1 2 3 4 5 6 7", "Camera: your eyes, CCTV corner, chase, orbit, top down, free, your path"),
            K("Tab", "Follow " + GameNames.Antagonist + " or yourself"),
            K("Right mouse (hold)", "Free camera: look around"),
            K("W A S D / Q E", "Free camera: move, and down or up (Left Shift: faster)"),
            K("Mouse wheel", "Free camera: how fast it flies"),
            K("Z / X", "Free camera: zoom in or out"),
            K("F", "Depth of field on or off"),
            K("K", "Add the camera as it is now to your path (saved next to the recording)"),
            K("Left Shift + K", "Take the last keyframe off your path"),
            K("M", "Her mind on or off"),
            K("B / G / V / N / T", "Her belief map, her guess, her view cone, sound rings, her thought log"),
            K("H", "Hide or show the timeline"),
            K("F1", "These keys"),
            K("Backspace", "Leave the replay")),
        new Section("Clips",
            K("F7", "Mark this moment for a clip; a tick shows for a second"),
            K("Left Shift + F7", "Mark a bug at this moment (in a playtest build, then say what went wrong)")),
        new Section("For testing",
            K("L", "Cut the power to the whole store")),
    };
}
