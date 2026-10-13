using System.Windows;

namespace RingDeck.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MessageBox.Show("RingDeck toolchain OK", "RingDeck");
        Shutdown();
    }
}
