using System.Runtime.InteropServices;

namespace RingDeck.Windows;

/// <summary>Whether RingDeck is running as the Microsoft Store (MSIX) version, and how it was started.</summary>
public static class AppPackage
{
    const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    public static bool IsPackaged { get; } = Detect();

    static bool Detect()
    {
        var length = 0;
        try { return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage; }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>True when Windows started the Store version at sign-in (its startup task cannot pass --tray).</summary>
    public static bool StartedAtSignIn()
    {
        if (!IsPackaged) return false;
        try { return global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()?.Kind == global::Windows.ApplicationModel.Activation.ActivationKind.StartupTask; }
        catch (Exception) { return false; }
    }
}
