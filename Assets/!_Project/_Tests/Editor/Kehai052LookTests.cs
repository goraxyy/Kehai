using System.Collections.Generic;
using Kehai.Look;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 28 November: anti-aliasing on, the film look and FPS switches, and stock that stands a little
// off true as if put there by hand.
public sealed class Kehai052LookTests
{
    [Test]
    public void AntiAliasing_IsOnAtEveryQuality()
    {
        Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, ScreenExtras.AntialiasingFor(DisplaySettings.Quality.High));
        Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, ScreenExtras.AntialiasingFor(DisplaySettings.Quality.Medium));
        Assert.AreEqual(AntialiasingMode.FastApproximateAntialiasing, ScreenExtras.AntialiasingFor(DisplaySettings.Quality.Low));
        Assert.AreEqual(AntialiasingQuality.High, ScreenExtras.SmaaQualityFor(DisplaySettings.Quality.High));
    }

    [Test]
    public void TheFilmLook_SitsUnderTheScenesOwnVolume()
    {
        // The burnout's vignette and the stun's blur are on the scene's volume (priority 0); they win.
        Assert.Less(ScreenExtras.FilmPriority, 0f);
        Assert.IsFalse(ScreenExtras.IsFilmVolume(null));
    }

    [Test]
    public void ARetinaScreen_IsDrawnSmallerAndScaledUp_ASmallOneIsnt()
    {
        // 2880 x 1688 on High: drawn at about 1440p's pixel count.
        float s = DisplaySettings.PixelCapScale(2880, 1688, DisplaySettings.Quality.High);
        Assert.That(2880f * 1688f * s * s, Is.EqualTo(DisplaySettings.MaxPixels[2]).Within(1e3f));
        Assert.AreEqual(1f, DisplaySettings.PixelCapScale(1920, 1080, DisplaySettings.Quality.High));
        Assert.Less(DisplaySettings.PixelCapScale(1920, 1080, DisplaySettings.Quality.Low), 1f);
    }

    [Test]
    public void AStoreDoor_GetsTrimRoundItsLeaf_UpToItsOpening()
    {
        // A 1.84 m leaf (0.03 to 2.43 m) in a 2 m opening 2.5 m tall: a strip each side, one on top.
        var pieces = KarenFixtures.TrimPieces(0.92f, -1.2f, 1.2f, -1f, 1f, 1.27f);
        Assert.AreEqual(3, pieces.Count);
        Assert.That(pieces[0].size.x, Is.EqualTo(0.08f).Within(1e-4f));
        Assert.That(pieces[0].centre.x, Is.EqualTo(-0.96f).Within(1e-4f));
        Assert.That(pieces[1].centre.x, Is.EqualTo(0.96f).Within(1e-4f));
        Assert.That(pieces[2].size.y, Is.EqualTo(0.07f).Within(1e-4f));
        Assert.AreEqual(0, KarenFixtures.TrimPieces(1f, -1f, 1f, -1f, 1f, 1f).Count, "a leaf that fills its opening: nothing");
    }

    [Test]
    public void Stock_StandsALittleOffTrue_TheSameEveryTime()
    {
        var yaws = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            var at = new Vector3(12.34f + i * 0.31f, 1.2f + (i % 4) * 0.4f, -56.7f - i * 0.17f);
            ShelfSlot.Jitter(at, out Vector2 offset, out float yaw);
            Assert.LessOrEqual(Mathf.Abs(offset.x), ShelfSlot.JitterSide + 1e-6f);
            Assert.LessOrEqual(Mathf.Abs(offset.y), ShelfSlot.JitterDepth + 1e-6f);
            Assert.LessOrEqual(Mathf.Abs(yaw), ShelfSlot.JitterYaw + 1e-4f);
            ShelfSlot.Jitter(at, out Vector2 again, out float yawAgain);
            Assert.AreEqual(offset, again, "the same place, the same look");
            Assert.AreEqual(yaw, yawAgain);
            yaws.Add(Mathf.RoundToInt(yaw));
        }
        Assert.GreaterOrEqual(yaws.Count, 7, "turns spread over the range (-4..4 degrees), not all one way");
    }
}
