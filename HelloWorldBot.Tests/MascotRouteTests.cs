using System.Drawing;
using HelloWorldBot.Mascot;

namespace HelloWorldBot.Tests;

public class MascotRouteTests
{
    // 1920x1080 with a 48 px taskbar at the bottom.
    private static MonitorInfo BottomTaskbar(string id, int left = 0, int top = 0, int dpi = 96) =>
        new(id, new Rectangle(left, top, 1920, 1080), new Rectangle(left, top, 1920, 1032), dpi);

    [Fact]
    public void Bottom_taskbar_means_the_robot_stands_on_its_top_edge()
    {
        var route = MascotRoute.Build([BottomTaskbar("A")]);

        var walkway = Assert.Single(route);
        Assert.Equal(new Walkway("A", 0, 1920, 1032, 96, 0, 1080), walkway);
    }

    [Fact]
    public void A_hidden_taskbar_means_the_bottom_of_the_screen()
    {
        var monitor = new MonitorInfo("A", new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1080));

        var walkway = Assert.Single(MascotRoute.Build([monitor]));

        Assert.Equal(1080, walkway.GroundY);
    }

    [Fact]
    public void A_taskbar_on_the_top_edge_leaves_the_ground_at_the_bottom_of_the_screen()
    {
        var monitor = new MonitorInfo("A", new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 48, 1920, 1032));

        var walkway = Assert.Single(MascotRoute.Build([monitor]));

        Assert.Equal(1080, walkway.GroundY);
        Assert.Equal((0, 1920), (walkway.Left, walkway.Right));
    }

    [Theory]
    [InlineData(0, 62, 1920)] // taskbar on the left: 62 px wide
    [InlineData(1, 0, 1858)] // taskbar on the right
    public void A_taskbar_on_a_side_edge_narrows_the_walkway_and_keeps_the_ground_at_the_screen_bottom(int side, int expectedLeft, int expectedRight)
    {
        var workArea = side == 0 ? new Rectangle(62, 0, 1858, 1080) : new Rectangle(0, 0, 1858, 1080);
        var monitor = new MonitorInfo("A", new Rectangle(0, 0, 1920, 1080), workArea);

        var walkway = Assert.Single(MascotRoute.Build([monitor]));

        Assert.Equal((expectedLeft, expectedRight, 1080), (walkway.Left, walkway.Right, walkway.GroundY));
    }

    [Fact]
    public void Monitors_are_ordered_left_to_right_including_negative_coordinates()
    {
        var route = MascotRoute.Build([BottomTaskbar("Right", 1920), BottomTaskbar("Primary"), BottomTaskbar("Left", -1920)]);

        Assert.Equal(["Left", "Primary", "Right"], route.Select(w => w.MonitorId));
    }

    [Fact]
    public void Monitors_stacked_above_and_below_follow_each_other_top_to_bottom()
    {
        var route = MascotRoute.Build([BottomTaskbar("Below", 0, 1080), BottomTaskbar("Above", 0, 0)]);

        Assert.Equal(["Above", "Below"], route.Select(w => w.MonitorId));
        Assert.Equal([1032, 2112], route.Select(w => w.GroundY));
    }

    [Fact]
    public void Each_walkway_keeps_the_dpi_of_its_monitor()
    {
        var route = MascotRoute.Build([BottomTaskbar("A", 0, 0, 96), BottomTaskbar("B", 1920, 0, 192)]);

        Assert.Equal([96, 192], route.Select(w => w.Dpi));
    }

    [Fact]
    public void Only_enabled_monitors_are_used()
    {
        var route = MascotRoute.Build([BottomTaskbar("A"), BottomTaskbar("B", 1920), BottomTaskbar("C", 3840)], ["B", "C"]);

        Assert.Equal(["B", "C"], route.Select(w => w.MonitorId));
    }

    [Fact]
    public void A_filter_that_matches_nothing_falls_back_to_all_monitors()
    {
        var route = MascotRoute.Build([BottomTaskbar("A"), BottomTaskbar("B", 1920)], ["Unplugged"]);

        Assert.Equal(["A", "B"], route.Select(w => w.MonitorId));
    }

    [Fact]
    public void No_monitors_gives_an_empty_route()
    {
        Assert.Empty(MascotRoute.Build([]));
    }
}
