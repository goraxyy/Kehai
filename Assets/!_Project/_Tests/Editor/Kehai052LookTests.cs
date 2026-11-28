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
