using System.Drawing;
using HelloWorldBot.Widget;

namespace HelloWorldBot.Tests;

public class WidgetPlacementTests
{
    private static readonly Size Widget = new(260, 140);

    // Primary 1920x1040 work area (taskbar at the bottom) with a second monitor to its right and one to its left.
    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);
    private static readonly Rectangle Right = new(1920, 0, 2560, 1400);
    private static readonly Rectangle Left = new(-1280, 100, 1280, 960);

    [Fact]
    public void A_position_fully_on_screen_is_kept()
    {
        var point = WidgetPlacement.Clamp(new Point(500, 300), Widget, [Primary]);

        Assert.Equal(new Point(500, 300), point);
    }

    [Fact]
    public void No_saved_position_goes_to_the_top_right_of_the_primary_screen()
    {
        var point = WidgetPlacement.Clamp(null, Widget, [Primary, Right]);

        Assert.Equal(new Point(1920 - 260 - WidgetPlacement.DefaultMargin, WidgetPlacement.DefaultMargin), point);
    }

    [Theory]
    [InlineData(-100, 300, 0, 300)] // off the left edge
    [InlineData(1900, 300, 1660, 300)] // overhangs the right edge
    [InlineData(500, -50, 500, 0)] // above the top
    [InlineData(500, 1000, 500, 900)] // overhangs the bottom (taskbar)
    [InlineData(1900, 1000, 1660, 900)] // corner
    public void A_partly_hidden_widget_is_pulled_fully_inside_its_screen(int x, int y, int expectedX, int expectedY)
    {
        var point = WidgetPlacement.Clamp(new Point(x, y), Widget, [Primary]);

        Assert.Equal(new Point(expectedX, expectedY), point);
    }

    [Fact]
    public void A_widget_on_a_second_monitor_stays_there()
    {
        var point = WidgetPlacement.Clamp(new Point(3000, 1200), Widget, [Primary, Right]);

        Assert.Equal(new Point(3000, 1200), point); // 1200 + 140 fits the 1400 high work area

        // Lower than that is pulled up (it would have been fine on a 1920x1080 primary, but not here).
        point = WidgetPlacement.Clamp(new Point(3000, 1350), Widget, [Primary, Right]);

        Assert.Equal(new Point(3000, 1260), point);
    }

    [Fact]
    public void A_widget_on_a_monitor_to_the_left_with_negative_coordinates_is_kept()
    {
        var point = WidgetPlacement.Clamp(new Point(-1000, 200), Widget, [Primary, Left]);

        Assert.Equal(new Point(-1000, 200), point);
    }

    [Fact]
    public void A_widget_straddling_two_monitors_goes_to_the_one_it_overlaps_most()
    {
        // 200 px on the primary screen, 60 px on the right monitor: primary wins.
        var point = WidgetPlacement.Clamp(new Point(1720, 300), Widget, [Primary, Right]);

        Assert.Equal(new Point(1660, 300), point);

        // 60 px on the primary screen, 200 px on the right monitor: right monitor wins.
        point = WidgetPlacement.Clamp(new Point(1860, 300), Widget, [Primary, Right]);

        Assert.Equal(new Point(1920, 300), point);
    }

    [Fact]
    public void A_disconnected_monitor_brings_the_widget_back_to_the_nearest_screen()
    {
        // Saved on the right monitor (x 3000) but only the primary screen is connected now.
        var point = WidgetPlacement.Clamp(new Point(3000, 400), Widget, [Primary]);

        Assert.Equal(new Point(1660, 400), point);
    }

    [Fact]
    public void A_widget_saved_far_off_every_screen_lands_on_the_closest_one()
    {
        var point = WidgetPlacement.Clamp(new Point(-5000, -5000), Widget, [Primary, Left]);

        // Closest to (-5000,-5000) is the left monitor (x -1280..0, y 100..1060).
        Assert.Equal(new Point(-1280, 100), point);
    }

    [Fact]
    public void A_widget_bigger_than_the_screen_is_aligned_to_its_top_left()
    {
        var tiny = new Rectangle(100, 100, 200, 100);

        var point = WidgetPlacement.Clamp(new Point(400, 400), Widget, [tiny]);

        Assert.Equal(new Point(100, 100), point);
    }

    [Fact]
    public void Scaled_sizes_for_125_150_and_200_percent_all_fit_inside_the_screen()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var size = new Size((int)(260 * scale), (int)(140 * scale));

            var point = WidgetPlacement.Clamp(new Point(1900, 1030), size, [Primary]);

            Assert.True(Primary.Contains(new Rectangle(point, size)), $"scale {scale}");
        }
    }

    [Fact]
    public void No_screens_at_all_returns_the_saved_position_unchanged()
    {
        Assert.Equal(new Point(7, 9), WidgetPlacement.Clamp(new Point(7, 9), Widget, []));
        Assert.Equal(Point.Empty, WidgetPlacement.Clamp(null, Widget, []));
    }
}
