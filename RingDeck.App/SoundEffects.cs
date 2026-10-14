using System.Windows;
using System.Windows.Media;

namespace RingDeck.App;

/// <summary>The two optional interface sounds. WPF's MediaPlayer plays files, so the bundled sounds are copied to a
/// temporary folder once. Players are reused so a sound starts without delay.</summary>
public static class SoundEffects
{
    static readonly Dictionary<string, MediaPlayer> Players = new();

    public static void Play(string name, double volume)
    {
        try
        {
            if (!Players.TryGetValue(name, out var player))
            {
                var folder = Path.Combine(Path.GetTempPath(), "RingDeck-sounds");
                Directory.CreateDirectory(folder);
                using var source = new MemoryStream();
                using (var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}.wav"))!.Stream)
                    resource.CopyTo(source);
                // Named after its contents, so an update with new sounds doesn't keep playing the old copy.
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source.ToArray()))[..12];
                var file = Path.Combine(folder, $"{name}-{hash}.wav");
                if (!File.Exists(file)) File.WriteAllBytes(file, source.ToArray());
                player = new MediaPlayer();
                player.Open(new Uri(file));
                Players[name] = player;
            }
            player.Volume = Math.Clamp(volume, 0, 1);
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception ex) { App.Log(ex); }   // a missing sound must never get in the way
    }
}
