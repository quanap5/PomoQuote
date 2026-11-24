using PomoQuote.Mascot;

namespace PomoQuote.Tests;

public sealed class MascotStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "nested", "mascot.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_the_defaults()
    {
        var settings = new MascotStore(StorePath).Load();

        Assert.True(settings.Enabled);
        Assert.True(settings.SoundEnabled);
        Assert.Equal(70, settings.VolumePercent);
        Assert.Equal(100, settings.SpeedPercent);
        Assert.Equal(2, settings.SizeScale);
        Assert.Equal(2, settings.RandomActions);
        Assert.Null(settings.EnabledMonitors);
    }

    [Fact]
    public void Saved_settings_survive_a_reload_and_create_the_folder()
    {
        var store = new MascotStore(StorePath);
        store.Save(new MascotSettings(false, false, 25, 150, 4, ["\\\\.\\DISPLAY1", "\\\\.\\DISPLAY3"], 1));

        var loaded = new MascotStore(StorePath).Load();

        Assert.False(loaded.Enabled);
        Assert.False(loaded.SoundEnabled);
        Assert.Equal(25, loaded.VolumePercent);
        Assert.Equal(150, loaded.SpeedPercent);
        Assert.Equal(4, loaded.SizeScale);
        Assert.Equal(1, loaded.RandomActions);
        Assert.Equal(["\\\\.\\DISPLAY1", "\\\\.\\DISPLAY3"], loaded.EnabledMonitors!);
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(250, 100)]
    public void Volume_is_clamped(int stored, int expected)
    {
        Assert.Equal(expected, new MascotSettings(VolumePercent: stored).Normalized().VolumePercent);
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(10000, 300)]
    public void Speed_is_clamped(int stored, int expected)
    {
        Assert.Equal(expected, new MascotSettings(SpeedPercent: stored).Normalized().SpeedPercent);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(0, 2)]
    [InlineData(7, 2)]
    [InlineData(1, 2)]
    public void Size_must_be_one_of_the_offered_scales(int stored, int expected)
    {
        Assert.Equal(expected, new MascotSettings(SizeScale: stored).Normalized().SizeScale);
    }

    [Fact]
    public void An_empty_monitor_list_means_all_monitors()
    {
        Assert.Null(new MascotSettings(EnabledMonitors: []).Normalized().EnabledMonitors);
    }

    [Fact]
    public void Gain_and_speed_factor_are_derived_from_the_percentages()
    {
        var settings = new MascotSettings(VolumePercent: 50, SpeedPercent: 200);

        Assert.Equal(0.5, settings.VolumeGain, 3);
        Assert.Equal(2.0, settings.SpeedFactor, 3);
    }

    [Fact]
    public void Out_of_range_numbers_in_the_file_are_clamped_on_load()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, """{"VolumePercent":900,"SpeedPercent":0,"SizeScale":9}""");

        var settings = new MascotStore(StorePath).Load();

        Assert.Equal((100, 25, 2), (settings.VolumePercent, settings.SpeedPercent, settings.SizeScale));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"Enabled":"yes"}""")]
    [InlineData("""{"VolumePercent":"loud"}""")]
    public void Corrupt_file_falls_back_to_the_defaults(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, content);

        var settings = new MascotStore(StorePath).Load();

        Assert.True(settings.Enabled);
        Assert.Equal(70, settings.VolumePercent);
    }

    [Fact]
    public void Unreadable_path_falls_back_to_the_defaults()
    {
        Directory.CreateDirectory(StorePath);

        Assert.True(new MascotStore(StorePath).Load().Enabled);
    }
}
