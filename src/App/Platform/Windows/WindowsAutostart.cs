using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ArnoA2.App;

/// <summary>Start at login through the user's Run key.</summary>
[SupportedOSPlatform("windows")]
static class WindowsAutostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "ArnoA2Toolkit";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(Name) != null;
        }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (on)
            key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
