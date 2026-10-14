using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace RingDeck.Windows;

/// <summary>Start with Windows, through a per-user "at sign-in" scheduled task. Windows starts those straight away;
/// entries under the Run key are started one at a time behind every other startup app, which can take minutes.</summary>
public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "RingDeck";

    /// <summary>One task per Windows user. The uninstaller deletes "RingDeck - [LogonUser]", so keep the two in step.</summary>
    static string TaskName => $"RingDeck - {Environment.UserName}";

    public static bool IsEnabled() => Schtasks("/Query", "/TN", TaskName) == 0;

    public static void Set(bool on)
    {
        RemoveRunEntry();
        if (!on)
        {
            Schtasks("/Delete", "/TN", TaskName, "/F");
            return;
        }
        var xml = Path.Combine(Path.GetTempPath(), $"ringdeck-task-{Guid.NewGuid():N}.xml");
        File.WriteAllText(xml, TaskXml(), Encoding.Unicode);
        try
        {
            if (Schtasks("/Create", "/TN", TaskName, "/XML", xml, "/F") != 0)
                throw new InvalidOperationException("Windows did not accept RingDeck's start-up task.");
        }
        finally { File.Delete(xml); }
    }

    /// <summary>Versions up to 0.1.3 used the Run key. Move such a setting over to the task once.</summary>
    public static void MigrateFromRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(RunName) is string) Set(true);
    }

    static void RemoveRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunName, false);
    }

    static string TaskXml()
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        var exe = SecurityElement.Escape(Environment.ProcessPath ?? "");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts RingDeck when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author"><Exec><Command>{exe}</Command><Arguments>--tray</Arguments></Exec></Actions>
            </Task>
            """;
    }

    static int Schtasks(params string[] args)
    {
        var info = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
