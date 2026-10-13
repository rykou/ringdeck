using System.Diagnostics;
using RingDeck.Core;

namespace RingDeck.Windows;

public sealed class Launcher : ILauncher
{
    public void Launch(string target, string arguments, string workingFolder)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("This slice has no target to open.");
        var folder = Environment.ExpandEnvironmentVariables(workingFolder ?? "");
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(target.Trim()),
            Arguments = arguments ?? "",
            WorkingDirectory = Directory.Exists(folder) ? folder : "",
            UseShellExecute = true,
        })?.Dispose();
    }
}
