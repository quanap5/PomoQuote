using System.Drawing;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

public sealed class WidgetStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "nested", "widget.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_normal_mode_full_opacity_and_no_position()
    {
        var settings = new WidgetStore(StorePath).Load();

        Assert.Equal(WidgetSettings.Default, settings);
        Assert.False(settings.Enabled);
        Assert.Equal(100, settings.OpacityPercent);
        Assert.Null(settings.Position);
    }

    [Fact]
    public void Saved_mode_position_and_opacity_survive_a_reload()
    {
        var store = new WidgetStore(StorePath);
        var settings = new WidgetSettings(true, -1200, 340, 60);

        store.Save(settings);

        var loaded = new WidgetStore(StorePath).Load();
        Assert.Equal(settings, loaded);
        Assert.Equal(new Point(-1200, 340), loaded.Position);
        Assert.Equal(0.6, loaded.Opacity, 3);
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void WithPosition_only_changes_the_position()
    {
        var moved = new WidgetSettings(true, 1, 2, 80).WithPosition(new Point(30, 40));

        Assert.Equal(new WidgetSettings(true, 30, 40, 80), moved);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(80)]
    [InlineData(60)]
    public void Allowed_opacities_are_kept(int percent)
    {
        Assert.Equal(percent, new WidgetSettings(OpacityPercent: percent).Normalized().OpacityPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(101)]
    [InlineData(-20)]
    public void Unknown_opacities_are_reset_to_100_when_loaded(int percent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, $$"""{"Enabled":true,"X":10,"Y":20,"OpacityPercent":{{percent}}}""");

        var settings = new WidgetStore(StorePath).Load();

        Assert.Equal(100, settings.OpacityPercent);
        Assert.True(settings.Enabled);
        Assert.Equal(new Point(10, 20), settings.Position);
    }

    [Fact]
    public void A_half_saved_position_is_dropped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, """{"Enabled":true,"X":10}""");

        var settings = new WidgetStore(StorePath).Load();

        Assert.Null(settings.Position);
        Assert.Null(settings.X);
        Assert.True(settings.Enabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"Enabled":"yes"}""")]
    [InlineData("""{"X":"left","Y":1}""")]
    public void Corrupt_file_falls_back_to_the_defaults(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, content);

        Assert.Equal(WidgetSettings.Default, new WidgetStore(StorePath).Load());
    }

    [Fact]
    public void Unreadable_path_falls_back_to_the_defaults()
    {
        Directory.CreateDirectory(StorePath); // a folder where the file should be

        Assert.Equal(WidgetSettings.Default, new WidgetStore(StorePath).Load());
    }

    [Fact]
    public void Saving_again_replaces_the_previous_settings()
    {
        var store = new WidgetStore(StorePath);
        store.Save(new WidgetSettings(true, 1, 2, 80));
        var newer = new WidgetSettings(false, 5, 6, 100);

        store.Save(newer);

        Assert.Equal(newer, store.Load());
    }
}
