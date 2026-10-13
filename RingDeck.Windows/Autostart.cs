using Microsoft.Win32;

namespace RingDeck.Windows;

public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "RingDeck";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string;
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue(Name, false);
    }
}
