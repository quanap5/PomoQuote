using HelloWorldBot.Weather;

namespace HelloWorldBot.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "HelloWorldBotTests-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_the_default_city_Hanoi()
    {
        var settings = new SettingsStore(SettingsPath).Load();

        Assert.Equal("Hanoi", settings.City.Name);
        Assert.Equal(AppSettings.Default, settings);
    }

    [Fact]
    public void Saved_city_survives_a_reload_and_creates_the_folder()
    {
        var store = new SettingsStore(SettingsPath);
        var tokyo = new CityLocation("Tokyo", "Japan", 35.6895, 139.69171);

        store.Save(new AppSettings(tokyo));

        Assert.Equal(tokyo, new SettingsStore(SettingsPath).Load().City);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Saving_again_replaces_the_previous_city()
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(new AppSettings(new CityLocation("Tokyo", "Japan", 35.6895, 139.69171)));
        var paris = new CityLocation("Paris", "France", 48.85341, 2.3488);

        store.Save(new AppSettings(paris));

        Assert.Equal(paris, store.Load().City);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"City":null}""")]
    [InlineData("""{"City":{"Name":"","Country":"","Latitude":1,"Longitude":2}}""")]
    [InlineData("""{"City":{"Name":"Nowhere","Country":"","Latitude":123,"Longitude":2}}""")]
    [InlineData("""{"City":{"Name":"Nowhere","Country":"","Latitude":1,"Longitude":-999}}""")]
    public void Corrupt_or_invalid_file_falls_back_to_the_default(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, content);

        Assert.Equal(AppSettings.Default, new SettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void Unreadable_path_falls_back_to_the_default()
    {
        // A directory where the file should be: reading it fails with an IO/permission error.
        Directory.CreateDirectory(SettingsPath);

        Assert.Equal(AppSettings.Default, new SettingsStore(SettingsPath).Load());
    }
}
