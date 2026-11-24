using PomoQuote.Mascot;
using PomoQuote.Pomodoro;

namespace PomoQuote.Tests;

public class MascotSpritesTests
{
    [Theory]
    [InlineData(24, 2, 96, 48)]
    [InlineData(24, 3, 96, 72)]
    [InlineData(24, 4, 96, 96)]
    [InlineData(24, 2, 192, 96)]
    [InlineData(24, 2, 144, 72)]
    [InlineData(24, 2, 24, 24)]
    [InlineData(16, 3, 96, 48)]
    public void Pixel_size_is_a_whole_multiple_of_the_frame_size_scaled_by_dpi(int frame, int scale, int dpi, int expected)
    {
        var size = MascotSprites.PixelSize(frame, scale, dpi);

        Assert.Equal(expected, size);
        Assert.Equal(0, size % frame);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(168)]
    [InlineData(192)]
    [InlineData(288)]
    public void At_every_common_scale_each_sprite_pixel_is_a_whole_number_of_screen_pixels(int dpi)
    {
        foreach (var scale in MascotSettings.SizeChoices)
        {
            var size = MascotSprites.PixelSize(24, scale, dpi);

            Assert.Equal(0, size % 24);
        }
    }
}

public class PomodoroSkipEventsTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void Skipping_a_phase_raises_PhaseSkipped_and_not_PhaseCompleted()
    {
        var timer = new PomodoroTimer(_clock, PomodoroSettings.Default);
        timer.Start();
        int skipped = 0, completed = 0;
        timer.PhaseSkipped += (_, _) => skipped++;
        timer.PhaseCompleted += (_, _) => completed++;

        timer.Skip();

        Assert.Equal((1, 0), (skipped, completed));
    }

    [Fact]
    public void Resetting_raises_ResetPerformed()
    {
        var timer = new PomodoroTimer(_clock, PomodoroSettings.Default);
        var resets = 0;
        timer.ResetPerformed += (_, _) => resets++;

        timer.Reset();

        Assert.Equal(1, resets);
    }

    [Fact]
    public void A_phase_that_simply_ends_raises_neither_skip_nor_reset()
    {
        var timer = new PomodoroTimer(_clock, PomodoroSettings.Default);
        timer.Start();
        int skipped = 0, resets = 0;
        timer.PhaseSkipped += (_, _) => skipped++;
        timer.ResetPerformed += (_, _) => resets++;

        _clock.Advance(timer.Remaining);
        timer.Tick();

        Assert.Equal((0, 0), (skipped, resets));
    }
}
