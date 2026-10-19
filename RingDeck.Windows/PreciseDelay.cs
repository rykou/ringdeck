using System.Runtime.InteropServices;

namespace RingDeck.Windows;

/// <summary>Waits for macros. Windows normally wakes timers about every 15.6 ms, so a 5 ms wait would take 15;
/// while a macro waits, the timer resolution is raised to 1 ms.</summary>
public static class PreciseDelay
{
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    public static async Task Wait(int ms, CancellationToken ct)
    {
        if (ms <= 0) { ct.ThrowIfCancellationRequested(); return; }
        timeBeginPeriod(1);
        try { await Task.Delay(ms, ct); }
        finally { timeEndPeriod(1); }
    }
}
