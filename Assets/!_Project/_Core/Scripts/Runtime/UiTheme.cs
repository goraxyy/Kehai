using TMPro;
using UnityEngine;

namespace Kehai
{
    // How everything drawn on the screen looks (0.4.1): the menus, the F1 map, the HUD.
    //
    // The pattern is the one the game already had — black, red and white — turned down: a soft
    // charcoal for the black, a coral for the red, a warm off-white for the white. Shapes have
    // round corners, and the text is set in a rounded face. One place for all of it, so the
    // owner's redesign before launch changes this file and not twenty.
    public static class UiTheme
    {
        // ---- colours -----------------------------------------------------------------------
        public static readonly Color Ink = new Color32(21, 22, 27, 247);         // the page behind everything
        public static readonly Color Panel = new Color32(31, 33, 40, 250);       // a card on the page
        public static readonly Color Raised = new Color32(47, 50, 60, 255);      // a button, a row, a chip
        public static readonly Color Accent = new Color32(228, 96, 108, 255);    // the red, softened to coral
        public static readonly Color AccentDim = new Color32(228, 96, 108, 96);
        public static readonly Color Text = new Color32(241, 237, 229, 255);     // the white, warmed
        public static readonly Color Dim = new Color32(241, 237, 229, 150);
        public static readonly Color Faint = new Color32(241, 237, 229, 26);
        public static readonly Color Good = new Color32(128, 203, 156, 255);
        public static readonly Color Warn = new Color32(240, 200, 120, 255);
        public static readonly Color Scrim = new Color(0.03f, 0.032f, 0.045f, 0.8f);   // over the game, under a menu

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        // A corner's radius for something `height` tall: round, but not a pill.
        public static float Corner(float height) => Mathf.Clamp(height * 0.28f, 3f, 18f);

        // ---- IMGUI ---------------------------------------------------------------------------

        // A filled shape with round corners.
        public static void Fill(Rect r, Color c, float radius = 0f)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, Mathf.Max(0f, radius));
        }

        // Only its edge.
        public static void Outline(Rect r, Color c, float width, float radius)
        {
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Mathf.Max(1f, width), Mathf.Max(0f, radius));
        }

        // A bar that fills from the left: the hold ring's plain cousin.
        public static void Bar(Rect r, float filled01, Color back, Color fill)
        {
            float radius = r.height * 0.5f;
            Fill(r, back, radius);
            float w = Mathf.Max(r.height, r.width * Mathf.Clamp01(filled01));
            if (filled01 > 0.001f) Fill(new Rect(r.x, r.y, w, r.height), fill, radius);
        }

        static readonly string[] SoftFaces =
        {
            "Arial Rounded MT Bold", "Hiragino Maru Gothic ProN", "Nunito", "Varela Round", "Segoe UI", "Helvetica Neue", "Arial",
        };
        static Font font;
        static bool triedFont;

        // The rounded face for IMGUI text: the computer's own, found by name (as GameFonts does
        // for TextMesh Pro), so no font file lives in the project. Null without one: the skin's.
        public static Font Font
        {
            get
            {
                if (font != null || triedFont) return font;
                triedFont = true;
                try { font = Font.CreateDynamicFontFromOSFont(SoftFaces, 16); }
                catch (System.Exception) { font = null; }
                if (font != null) font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return font;
            }
        }

        // A label in the theme's face and colour.
        public static GUIStyle Label(int size, Color colour, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = true)
        {
            var style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = wrap, alignment = anchor, fontSize = size };
            if (Font != null) style.font = Font;
            style.normal.textColor = colour;
            return style;
        }

        // Puts the theme's face on a style made elsewhere.
        public static GUIStyle Soften(GUIStyle style)
        {
            if (style != null && Font != null) style.font = Font;
            return style;
        }

        // ---- TextMesh Pro and uGUI -------------------------------------------------------------

        // The rounded face for TextMesh Pro text (it carries a rounded Japanese behind it).
        public static TMP_FontAsset TmpFont => GameFonts.Rounded;

        static Sprite rounded;

        // A white tile with round corners, nine-sliced: an Image set to Sliced keeps its corners
        // at any size. `pixelsPerUnitMultiplier` on the Image scales how round they look.
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int size = 64, radius = 20;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "UiTheme rounded", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance into the nearest corner's circle; 1 px of soft edge.
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);
                rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                rounded.name = "UiTheme rounded";
                rounded.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return rounded;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            font = null;
            triedFont = false;
            rounded = null;
        }
    }
}
