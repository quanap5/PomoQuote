using System.Drawing;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>Where a dialog goes next to the widget: beside it, inside that screen's usable area, at any edge, screen and scale.</summary>
public sealed class DialogPlacementTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1032);
    private const int Gap = 8;

    private static Rectangle At(int x, int y) => new(x, y, 280, 227);

    private static Rectangle Place(Rectangle widget, Size size, Rectangle area) => new(DialogPlacement.Beside(widget, size, area, Gap), size);

    [Theory]
    [InlineData(1640, 0, 1348, 0)] // top right (the usual place): to the left, level with the widget
    [InlineData(0, 0, 288, 0)] // top left: to the right
    [InlineData(1640, 805, 1348, 624)] // bottom right: to the left, moved up to stay on the screen
    [InlineData(0, 805, 288, 624)] // bottom left
    [InlineData(600, 400, 888, 400)] // more room on the right
    [InlineData(1100, 400, 808, 400)] // more room on the left
    public void The_dialog_goes_beside_the_widget_on_the_side_with_room(int widgetX, int widgetY, int expectedX, int expectedY)
    {
        var dialog = Place(At(widgetX, widgetY), new Size(284, 408), Primary);

        Assert.Equal(new Point(expectedX, expectedY), dialog.Location);
        Assert.True(Primary.Contains(dialog));
        Assert.False(dialog.IntersectsWith(At(widgetX, widgetY)));
    }

    [Theory]
    [InlineData(1920, 0)] // a second screen to the right
    [InlineData(-1920, 0)] // a screen to the left of the primary one: negative coordinates
    [InlineData(0, -1080)] // a screen above
    [InlineData(1920, 300)] // screens that do not line up
    public void On_another_screen_the_dialog_stays_on_that_screen(int left, int top)
    {
        var area = new Rectangle(left, top, 1920, 1032);
        foreach (var widget in new[] { At(area.Left, area.Top), At(area.Right - 280, area.Bottom - 227), At(area.Left + 800, area.Top + 400) })
        {
            var dialog = Place(widget, new Size(492, 957), area);

            Assert.True(area.Contains(dialog), $"{dialog} outside {area}");
            Assert.False(dialog.IntersectsWith(widget));
        }
    }

    [Fact]
    public void With_no_room_at_the_sides_it_goes_under_or_above_the_widget()
    {
        var narrow = new Rectangle(0, 0, 500, 900);
        var top = new Rectangle(110, 0, 280, 227);
        var bottom = new Rectangle(110, 673, 280, 227);

        var under = Place(top, new Size(400, 300), narrow);
        var above = Place(bottom, new Size(400, 300), narrow);

        Assert.Equal(new Point(100, 235), under.Location);
        Assert.Equal(new Point(100, 365), above.Location);
        Assert.False(under.IntersectsWith(top));
        Assert.False(above.IntersectsWith(bottom));
    }

    [Fact]
    public void With_no_free_spot_it_goes_to_the_edge_with_more_room_and_stays_on_the_screen()
    {
        var area = new Rectangle(0, 0, 900, 700);
        var widget = new Rectangle(500, 200, 280, 227); // room: 500 left, 120 right

        var dialog = Place(widget, new Size(600, 650), area);

        Assert.True(area.Contains(dialog));
        Assert.Equal(area.Left, dialog.Left);
    }

    [Fact]
    public void A_dialog_larger_than_the_area_starts_at_its_top_left_corner()
    {
        var area = new Rectangle(100, 50, 400, 300);

        Assert.Equal(area.Location, DialogPlacement.Beside(new Rectangle(200, 100, 280, 227), new Size(900, 900), area, Gap));
        Assert.Equal(area.Location, DialogPlacement.Clamp(new Point(300, 300), new Size(900, 900), area));
    }

    /// <summary>Every widget position on screens as they are at 100, 125 and 150 percent: always inside, and clear of the widget whenever a clear spot exists.</summary>
    [Theory]
    [InlineData(1920, 1032, 280, 227)] // 100 %
    [InlineData(1920, 1020, 350, 284)] // 125 %
    [InlineData(1920, 1008, 420, 341)] // 150 %
    [InlineData(1366, 720, 280, 227)] // a small laptop screen
    [InlineData(2560, 1380, 420, 341)]
    public void Everywhere_the_dialog_is_inside_the_area_and_clear_of_the_widget_when_it_can_be(int width, int height, int widgetWidth, int widgetHeight)
    {
        var area = new Rectangle(0, 0, width, height);
        foreach (var size in new[] { new Size(284, 408), new Size(345, 273), new Size(513, 427), new Size(492, 700), new Size(900, 600) })
        {
            for (var x = 0; x <= width - widgetWidth; x += 53)
            {
                for (var y = 0; y <= height - widgetHeight; y += 47)
                {
                    var widget = new Rectangle(x, y, widgetWidth, widgetHeight);
                    var dialog = Place(widget, size, area);

                    Assert.True(area.Contains(dialog), $"{dialog} outside {area} for {widget}");
                    var freeSpot = widget.Left - area.Left >= size.Width + Gap || area.Right - widget.Right >= size.Width + Gap
                        || widget.Top - area.Top >= size.Height + Gap || area.Bottom - widget.Bottom >= size.Height + Gap;
                    if (freeSpot) Assert.False(dialog.IntersectsWith(widget), $"{dialog} covers {widget}");
                }
            }
        }
    }

    [Theory]
    [InlineData(1640, 0, 960, 540, 960)] // room beside it: unchanged
    [InlineData(820, 322, 960, 540, 812)] // the middle: narrowed to the room beside the widget
    [InlineData(700, 322, 960, 540, 932)] // the wider side
    [InlineData(820, 0, 960, 540, 960)] // free under the widget: unchanged
    public void A_resizable_window_is_narrowed_only_when_that_is_what_keeps_it_off_the_widget(int x, int y, int width, int height, int expected)
    {
        var widget = At(x, y);

        var size = DialogPlacement.FitBeside(widget, new Size(width, height), Primary, Gap, DialogPlacement.NarrowestResizable);

        Assert.Equal(new Size(expected, height), size);
        Assert.False(new Rectangle(DialogPlacement.Beside(widget, size, Primary, Gap), size).IntersectsWith(widget));
    }

    [Fact]
    public void A_resizable_window_is_not_narrowed_below_its_smallest_width()
    {
        var area = new Rectangle(0, 0, 1366, 720);
        var widget = new Rectangle(543, 250, 280, 227); // 543 on each side, less than the narrowest width

        Assert.Equal(new Size(960, 540), DialogPlacement.FitBeside(widget, new Size(960, 540), area, Gap, DialogPlacement.NarrowestResizable));
    }

    [Fact]
    public void Over_a_window_the_dialog_is_centred_and_kept_on_the_screen()
    {
        var parent = new Rectangle(500, 200, 800, 500);

        Assert.Equal(new Point(750, 300), DialogPlacement.Over(parent, new Size(300, 300), Primary, null, Gap));
        Assert.Equal(new Point(1620, 732), DialogPlacement.Over(new Rectangle(1800, 900, 800, 500), new Size(300, 300), Primary, null, Gap));
    }

    [Fact]
    public void Over_a_dialog_that_is_next_to_the_widget_a_wider_child_does_not_cover_the_widget()
    {
        var widget = At(1640, 0);
        var parent = new Rectangle(1340, 0, 292, 400);
        var size = new Size(600, 300);

        var child = new Rectangle(DialogPlacement.Over(parent, size, Primary, widget, Gap), size);

        Assert.False(child.IntersectsWith(widget));
        Assert.True(Primary.Contains(child));
    }
}
