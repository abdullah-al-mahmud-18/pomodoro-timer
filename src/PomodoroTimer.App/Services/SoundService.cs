using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NetCoreAudio;

namespace PomodoroTimer.App.Services;

/// <summary>Plays the bundled completion sound via NetCoreAudio (cross-platform, unlike NAudio).</summary>
public class SoundService : IDisposable
{
    private readonly Player _player = new();
    private readonly string _soundFilePath;

    public SoundService()
    {
        _soundFilePath = ExtractBundledSoundToTempFile();
    }

    public async Task PlayCompletionSoundAsync()
    {
        try
        {
            await _player.Play(_soundFilePath);
        }
        catch
        {
            // Audio backend unavailable — the OS notification still fired, so don't crash the app over sound.
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
            _player.Stop().GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort cleanup on shutdown.
        }
    }
}
