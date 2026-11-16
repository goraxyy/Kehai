using NUnit.Framework;
using UnityEngine;

public class Kehai051SettingsTests
{
    static readonly Vector2Int[] Screens =
    {
        new Vector2Int(800, 600), new Vector2Int(1024, 768), new Vector2Int(1280, 720), new Vector2Int(1280, 800),
        new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3440, 1440), new Vector2Int(3840, 2160),
    };

    // The footer bar as OnGUI builds it: inside the panel, inside its padding, size * 1.9 tall.
    static Rect Bar(Vector2Int screen, out int size)
    {
        size = SettingsMenu.UiSize(screen.y);
        float panelW = SettingsMenu.PanelWidth(screen.x, size, false);
        float panelX = (screen.x - panelW) * 0.5f;
        float innerX = panelX + size * 1.2f, innerW = panelW - size * 2.4f;
        float panelH = screen.y - size * 2f, innerH = panelH - size * 2f;
        float innerY = size + size;
        return new Rect(innerX, innerY + innerH - size * 1.9f, innerW, size * 1.9f);
    }

    [Test]
    public void FooterButtonsAlignAndFitAtEveryScreenSize()
    {
        foreach (Vector2Int screen in Screens)
        {
            Rect bar = Bar(screen, out int size);
            Rect[] b = SettingsMenu.FooterButtons(bar, SettingsMenu.FooterSlots, size);
            string at = $"{screen.x}x{screen.y} (size {size})";
            Assert.AreEqual(SettingsMenu.FooterSlots, b.Length, at);
            for (int i = 0; i < b.Length; i++)
            {
                Assert.AreEqual(bar.y, b[i].y, 0.001f, at + " same top");
                Assert.AreEqual(bar.height, b[i].height, 0.001f, at + " same height");
                Assert.AreEqual(b[0].width, b[i].width, 0.001f, at + " same width");
                Assert.Greater(b[i].width, 0f, at);
                Assert.GreaterOrEqual(b[i].x, bar.x - 0.001f, at + " inside bar (left)");
                Assert.LessOrEqual(b[i].xMax, bar.xMax + 0.001f, at + " inside bar (right)");
                Assert.GreaterOrEqual(b[i].xMax, 0f, at); Assert.LessOrEqual(b[i].xMax, screen.x, at + " on screen");
                if (i > 0) Assert.Greater(b[i].x, b[i - 1].xMax, at + " no overlap, a gap between");
            }
            Assert.AreEqual(bar.x, b[0].x, 0.001f, at);
            Assert.AreEqual(bar.xMax, b[b.Length - 1].xMax, 0.001f, at + " last button on the bar's right edge");
        }
    }

    [Test]
    public void FooterLabelsFitTheirButtonsAtReadableSize()
    {
        foreach (Vector2Int screen in Screens)
        {
            Rect bar = Bar(screen, out int size);
            float w = SettingsMenu.FooterButtons(bar, SettingsMenu.FooterSlots, size)[0].width;
            int font = SettingsMenu.FooterFontSize(w, size);
            // MAIN MENU, the widest label, is 9 capitals of about 0.7 em plus padding.
            Assert.LessOrEqual(font * SettingsMenu.FooterLabelEm, w + 0.01f, $"{screen.x}x{screen.y}: label fits");
            Assert.GreaterOrEqual(font, Mathf.Min(size, 14), $"{screen.x}x{screen.y}: text stays readable");
        }
    }

    [Test]
    public void FooterStillFitsInANarrowWindowAndSingleButtonLayoutsAlign()
    {
        var bar = new Rect(10f, 500f, 220f, 30f);
        Rect[] b = SettingsMenu.FooterButtons(bar, 4, 15f);
        for (int i = 1; i < b.Length; i++) Assert.Greater(b[i].x, b[i - 1].xMax);
        Assert.AreEqual(bar.xMax, b[3].xMax, 0.001f);
        Assert.GreaterOrEqual(SettingsMenu.FooterFontSize(b[0].width, 15), 8);
        Rect[] one = SettingsMenu.FooterButtons(bar, 1, 15f);
        Assert.AreEqual(bar.width, one[0].width, 0.001f);
    }

    [Test]
    public void UiSizeIsClampedAndGrowsWithHeight()
    {
        Assert.AreEqual(15, SettingsMenu.UiSize(300));
        Assert.AreEqual(17, SettingsMenu.UiSize(720));
        Assert.AreEqual(26, SettingsMenu.UiSize(1080));
        Assert.AreEqual(34, SettingsMenu.UiSize(1440));
        Assert.AreEqual(36, SettingsMenu.UiSize(4000));
    }
}
