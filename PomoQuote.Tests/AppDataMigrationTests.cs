namespace PomoQuote.Tests;

public sealed class AppDataMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    private string Old => Path.Combine(_root, "HelloWorldBot");

    private string New => Path.Combine(_root, "PomoQuote");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Put(string folder, string name, string content)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name), content);
    }

    private int Run() => AppDataMigration.Run(Old, New, _log.Add);

    [Fact]
    public void All_old_settings_are_copied_into_the_new_folder()
    {
        string[] names = ["settings.json", "pomodoro.json", "widget.json", "mascot.json", "mascot-config.json"];
        foreach (var name in names) Put(Old, name, "old " + name);

        var copied = Run();

        Assert.Equal(names.Length, copied);
        foreach (var name in names) Assert.Equal("old " + name, File.ReadAllText(Path.Combine(New, name)));
    }

    [Fact]
    public void The_old_folder_and_its_files_are_left_untouched()
    {
        Put(Old, "settings.json", "keep me");
        Put(Old, "pomodoro.json", "keep me too");

        Run();

        Assert.Equal("keep me", File.ReadAllText(Path.Combine(Old, "settings.json")));
        Assert.Equal("keep me too", File.ReadAllText(Path.Combine(Old, "pomodoro.json")));
        Assert.Equal(2, Directory.GetFiles(Old).Length);
    }

    [Fact]
    public void A_file_that_already_exists_in_the_new_folder_is_never_overwritten()
    {
        Put(Old, "settings.json", "old city");
        Put(Old, "widget.json", "old widget");
        Put(New, "settings.json", "new city");

        var copied = Run();

        Assert.Equal(1, copied);
        Assert.Equal("new city", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.Equal("old widget", File.ReadAllText(Path.Combine(New, "widget.json")));
    }

    [Fact]
    public void It_runs_once_so_a_file_deleted_later_does_not_come_back()
    {
        Put(Old, "settings.json", "old");
        Run();
        File.Delete(Path.Combine(New, "settings.json"));

        var copiedAgain = Run();

        Assert.Equal(0, copiedAgain);
        Assert.False(File.Exists(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(New, AppDataMigration.DoneMarker)));
    }

    [Fact]
    public void No_old_folder_means_nothing_happens_and_nothing_is_created()
    {
        var copied = Run();

        Assert.Equal(0, copied);
        Assert.False(Directory.Exists(New));
        Assert.Empty(_log);
    }

    [Fact]
    public void Leftover_temp_files_are_not_copied()
    {
        Put(Old, "settings.json", "ok");
        Put(Old, "settings.json.tmp", "half written");

        Run();

        Assert.False(File.Exists(Path.Combine(New, "settings.json.tmp")));
    }

    [Fact]
    public void A_new_location_that_cannot_be_created_is_logged_and_does_not_throw()
    {
        Put(Old, "settings.json", "old");
        Directory.CreateDirectory(_root);
        File.WriteAllText(New, "a file where the new folder should be");

        var copied = AppDataMigration.Run(Old, New, _log.Add);

        Assert.Equal(0, copied);
        Assert.NotEmpty(_log);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Old, "settings.json")));
    }

    [Fact]
    public void A_file_that_cannot_be_copied_is_logged_the_rest_still_copy_and_the_next_start_retries()
    {
        Put(Old, "settings.json", "old settings");
        Put(Old, "widget.json", "old widget");
        Directory.CreateDirectory(Path.Combine(New, "settings.json")); // a folder blocks that one target

        var copied = Run();

        Assert.True(Directory.Exists(Path.Combine(New, "settings.json")));
        Assert.Equal("old widget", File.ReadAllText(Path.Combine(New, "widget.json")));
        Assert.Equal(1, copied);
        Assert.Contains(_log, line => line.Contains("settings.json"));
        Assert.False(File.Exists(Path.Combine(New, AppDataMigration.DoneMarker)));
    }

    [Fact]
    public void A_locked_source_file_is_logged_and_retried_on_the_next_start()
    {
        Put(Old, "settings.json", "old settings");
        Put(Old, "widget.json", "old widget");

        int copied;
        using (new FileStream(Path.Combine(Old, "settings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            copied = Run();
            Assert.Equal(1, copied);
            Assert.Contains(_log, line => line.Contains("settings.json"));
            Assert.False(File.Exists(Path.Combine(New, AppDataMigration.DoneMarker)));
        }

        Assert.Equal(1, Run());
        Assert.Equal("old settings", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(New, AppDataMigration.DoneMarker)));
    }
}
