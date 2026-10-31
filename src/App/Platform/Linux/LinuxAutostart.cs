namespace ArnoA2.App;

/// <summary>The desktop's start-at-login entry, the same one tools/install.sh --autostart writes.</summary>
static class LinuxAutostart
{
    const string Id = "arno-a2-toolkit";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string Entry => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Home, ".config"), "autostart", Id + ".desktop");
    static string Menu => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data ? data : Path.Combine(Home, ".local/share"), "applications", Id + ".desktop");

    public static bool Enabled => File.Exists(Entry);

    public static void Set(bool on)
    {
        if (!on)
        {
            File.Delete(Entry);
            return;
        }

        string entry = File.Exists(Menu) ? File.ReadAllText(Menu).TrimEnd() : $"[Desktop Entry]\nType=Application\nName=Arno's A2 Toolkit\nExec={Environment.ProcessPath}";
        Directory.CreateDirectory(Path.GetDirectoryName(Entry)!);
        File.WriteAllText(Entry, entry + "\nX-KDE-autostart-after=panel\n");
    }
}
