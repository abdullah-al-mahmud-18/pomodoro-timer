using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NetCoreAudio;

namespace PomodoroTimer.App.Services;

/// <summary>Plays the bundled completion sound via NetCoreAudio (cross-platform, unlike NAudio).</summary>
public class SoundService : IDisposable
{
    private const int CompletionRepeatCount = 5;

    private Player? _currentPlayer;
    private readonly string _soundFilePath;

    public SoundService()
    {
        _soundFilePath = ExtractBundledSoundToTempFile();
    }

    public async Task PlayCompletionSoundAsync()
    {
        try
        {
            for (var i = 0; i < CompletionRepeatCount; i++)
            {
                // NetCoreAudio's WindowsPlayer reuses internal MCI/timer state across repeated Play() calls
                // on the same instance, which reliably stops firing PlaybackFinished after the first play.
                // A fresh Player per repeat avoids that entirely.
                var player = new Player();
                _currentPlayer = player;

                var finished = new TaskCompletionSource();
                player.PlaybackFinished += (_, _) => finished.TrySetResult();

                await player.Play(_soundFilePath);
                await finished.Task;
            }
        }
        catch
        {
            // Audio backend unavailable — the OS notification still fired, so don't crash the app over sound.
        }
        finally
        {
            _currentPlayer = null;
        }
    }

    private static string ExtractBundledSoundToTempFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "PomodoroTimer", "complete.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        if (!File.Exists(tempPath))
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = assembly.GetManifestResourceStream("PomodoroTimer.App.Assets.complete.wav")
                ?? throw new InvalidOperationException("Embedded completion sound resource not found.");
            using var fileStream = File.Create(tempPath);
            resourceStream.CopyTo(fileStream);
        }

        return tempPath;
    }

    public void Dispose()
    {
        try
        {
            _currentPlayer?.Stop().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup on shutdown.
        }
    }
}
