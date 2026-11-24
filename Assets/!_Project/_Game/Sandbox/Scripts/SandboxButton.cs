using System;
using UnityEngine;

namespace Kehai.Sandbox
{
    // A button on a sandbox's control panel: look at it and press E. Its label is written on it,
    // so you can tell what it does without a prompt.
    public sealed class SandboxButton : HighlightInteractable
    {
        public string label = "Button";
        public string prompt = "Press";
        public Action pressed;

        public override void Interact(PlayerInteract player) => pressed?.Invoke();
        public override string GetPrompt() => prompt;

        public void Press() => pressed?.Invoke();

        // A panel of buttons in a row, centred on `at` (its local space faces +z), each a
        // small coloured block with its label above it. `at` sits at chest height, within reach.
        // Each button gets half a metre, and a label never runs wider than its button's share.
        public const float Spacing = 0.5f;
        public const float LabelSize = 0.02f;

        public static SandboxButton[] Panel(Transform parent, string name, Vector3 at, float yaw, params (string label, string prompt, Color colour, Action pressed)[] buttons)
        {
            Transform panel = SandboxKit.Group(parent, name, at, yaw);
            float start = -(buttons.Length - 1) * Spacing * 0.5f;
            SandboxKit.Box(panel, "Backplate", new Vector3(0f, 0f, -0.02f), new Vector3(buttons.Length * Spacing + 0.1f, 0.42f, 0.03f), new Color(0.15f, 0.15f, 0.17f));
            var made = new SandboxButton[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                GameObject block = SandboxKit.Box(panel, b.label, new Vector3(start + i * Spacing, -0.05f, 0.03f), new Vector3(0.22f, 0.22f, 0.08f), b.colour);
                block.layer = SandboxKit.InteractLayer;   // so the store's player can press it on the island
                var button = block.AddComponent<SandboxButton>();
                button.label = b.label;
                button.prompt = b.prompt;
                button.pressed = b.pressed;
                made[i] = button;
                TextMesh text = Label(panel, b.label, new Vector3(start + i * Spacing, 0.13f, 0.03f), LabelSize);
                float width = text.GetComponent<Renderer>().localBounds.size.x;
                float room = Spacing - 0.08f;
                if (width > room) text.transform.localScale *= room / width;
            }
            return made;
        }

        static Font builtinFont;
        public static Font BuiltinFont
        {
            get
            {
                if (builtinFont == null)
                {
                    try { builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                    catch (Exception) { builtinFont = null; }
                }
                return builtinFont;
            }
        }

        // The built-in font's material draws over everything, so a label showed through walls (the
        // panels inside and outside a door sit back to back). This copy hides behind what's in front.
        static Material depthTested;
        static Material DepthTested(Font font)
        {
            if (depthTested == null || depthTested.mainTexture != font.material.mainTexture)
            {
                depthTested = new Material(font.material) { name = "Sandbox label" };
                depthTested.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
            return depthTested;
        }

        // A world-space text label, facing the way its parent faces.
        public static TextMesh Label(Transform parent, string text, Vector3 at, float size)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            // TextMesh reads towards -z; turn it so it faces whoever stands in front (+z side).
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = size;
            mesh.fontSize = 48;
            mesh.color = Color.white;
            // A TextMesh draws nothing without a font: the built-in one ships with the editor.
            Font font = BuiltinFont;
            if (font != null)
            {
                mesh.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = DepthTested(font);
            }
            return mesh;
        }
    }
}
