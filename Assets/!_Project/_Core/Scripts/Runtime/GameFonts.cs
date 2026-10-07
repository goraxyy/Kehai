using TMPro;
using UnityEngine;

namespace Kehai
{
    // Fonts for text built from code (the main menu): the computer's own, found by name, so no
    // font file has to live in the project. A Mac has Avenir Next and Hiragino, Windows has
    // Segoe UI and Yu Gothic. Without them TextMesh Pro's default font is used, and Japanese
    // text is left out rather than drawn as boxes.
    public static class GameFonts
    {
        static readonly (string family, string style)[] HeadingFaces =
        {
            ("Avenir Next", "Heavy"), ("Avenir Next", "Bold"), ("Helvetica Neue", "Bold"),
            ("Segoe UI", "Black"), ("Segoe UI", "Bold"), ("Arial", "Bold"),
        };
        static readonly (string family, string style)[] BodyFaces =
        {
            ("Avenir Next", "Medium"), ("Avenir Next", "Regular"), ("Helvetica Neue", "Medium"),
            ("Helvetica Neue", "Regular"), ("Segoe UI", "Regular"), ("Arial", "Regular"),
        };
        static readonly (string family, string style)[] JapaneseFaces =
        {
            ("Hiragino Mincho ProN", "W6"), ("Hiragino Mincho ProN", "W3"), ("Hiragino Sans", "W6"),
            ("Hiragino Kaku Gothic ProN", "W6"), ("Yu Mincho", "Demibold"), ("Yu Gothic", "Bold"),
            ("Meiryo", "Bold"), ("MS Gothic", "Regular"),
        };

        // A softer, rounded face for signage, with a rounded Japanese one behind it.
        static readonly (string family, string style)[] RoundedFaces =
        {
            ("Arial Rounded MT Bold", "Regular"), ("Hiragino Maru Gothic ProN", "W4"),
            ("Segoe UI", "Semibold"), ("Arial", "Bold"),
        };
        static readonly (string family, string style)[] RoundedJapaneseFaces =
        {
            ("Hiragino Maru Gothic ProN", "W4"), ("Hiragino Sans", "W5"), ("Yu Gothic", "Bold"), ("Meiryo", "Bold"),
        };

        static TMP_FontAsset heading, body, japanese, rounded, roundedJapanese;
        static bool triedHeading, triedBody, triedJapanese, triedRounded, triedRoundedJapanese;

        public static TMP_FontAsset Heading => WithJapanese(Find(ref heading, ref triedHeading, HeadingFaces)) ?? TMP_Settings.defaultFontAsset;
        public static TMP_FontAsset Body => WithJapanese(Find(ref body, ref triedBody, BodyFaces)) ?? TMP_Settings.defaultFontAsset;

        // Null when this computer has no Japanese font.
        public static TMP_FontAsset Japanese => Find(ref japanese, ref triedJapanese, JapaneseFaces);

        // Rounded, for the aisle signs; its Japanese falls back to a rounded Japanese face.
        public static TMP_FontAsset Rounded =>
            WithFallback(Find(ref rounded, ref triedRounded, RoundedFaces),
                         Find(ref roundedJapanese, ref triedRoundedJapanese, RoundedJapaneseFaces) ?? Japanese) ?? Heading;

        // A font that can draw `text`: `usual`, unless the text has Japanese in it (the burnout
        // ending's 過労死) and a Japanese font is there to draw it.
        public static TMP_FontAsset ForText(string text, TMP_FontAsset usual)
        {
            if (string.IsNullOrEmpty(text) || Japanese == null) return usual;
            if (usual != null && usual == rounded) return usual;     // it has its own Japanese
            foreach (char c in text)
                if ((c >= '\u3000' && c <= '\u9FFF') || (c >= '\uFF00' && c <= '\uFFEF')) return Body;
            return usual;
        }

        public static string Describe(TMP_FontAsset font) => font == null ? "none" : font.faceInfo.familyName + " " + font.faceInfo.styleName;

        static TMP_FontAsset Find(ref TMP_FontAsset cached, ref bool tried, (string family, string style)[] faces)
        {
            if (cached != null || tried) return cached;
            tried = true;
            foreach ((string family, string style) in faces)
            {
                TMP_FontAsset font;
                try { font = TMP_FontAsset.CreateFontAsset(family, style); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Font {family} {style} couldn't be loaded: {e.Message}");
                    continue;
                }
                if (font == null) continue;
                font.name = family + " " + style;
                font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (font.material != null) font.material.hideFlags = HideFlags.DontUnloadUnusedAsset;
                cached = font;
                return font;
            }
            return null;
        }

        // A Latin font that also draws 気配 and 愛子 from the Japanese one.
        static TMP_FontAsset WithJapanese(TMP_FontAsset font) => WithFallback(font, Japanese);

        static TMP_FontAsset WithFallback(TMP_FontAsset font, TMP_FontAsset jp)
        {
            if (font == null || jp == null || font == jp) return font;
            font.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(jp)) font.fallbackFontAssetTable.Add(jp);
            return font;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            heading = body = japanese = rounded = roundedJapanese = null;
            triedHeading = triedBody = triedJapanese = triedRounded = triedRoundedJapanese = false;
        }
    }
}
