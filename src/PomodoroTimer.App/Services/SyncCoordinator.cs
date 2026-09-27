using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.Data.Sqlite;
using PomodoroTimer.App.ViewModels;
using PomodoroTimer.App.Views;
using PomodoroTimer.Core.Sync;
using Serilog;

namespace PomodoroTimer.App.Services;

/// <summary>
/// App-side sync wiring: sign-in, the startup/retry sync with its user prompts, the shutdown upload, and the
/// status shown in the UI. Sync failures never block local use — the local db always stays the working copy.
/// All members are called on the UI thread.
/// </summary>
public sealed class SyncCoordinator
{
    private static readonly TimeSpan PlanTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    private const string OfflineDetail = "Couldn't reach Google Drive — your data is saved on this device and will sync later.";

    private readonly GoogleAuthService _auth;
    private readonly ILocalFileGate _gate;
    private readonly Func<Window?> _owner;

    private GoogleDriveFileStore? _store;
    private SyncService? _service;
    private bool _needsSignIn;
    private bool _uploadOnShutdownAllowed;
    private Task _current = Task.CompletedTask;

    public SyncCoordinator(GoogleAuthService auth, ILocalFileGate gate, Func<Window?> owner)
    {
        _auth = auth;
        _gate = gate;
        _owner = owner;
        Status.ActionHandler = RetryAsync;
    }

    public SyncStatusViewModel Status { get; } = new();

    /// <summary>Raised on the UI thread after a download replaced the local db, so pages can migrate and reload it.</summary>
    public event Action? LocalDatabaseReplaced;

    /// <summary>Startup sync. Runs before the database is opened; never signs in interactively.</summary>
    public Task StartupAsync() => RunExclusive(() => SyncAsync(interactiveSignIn: false));

    /// <summary>The status bar's Retry / Sign in button.</summary>
    public Task RetryAsync() => RunExclusive(() => SyncAsync(interactiveSignIn: _needsSignIn));

    /// <summary>
    /// Shutdown sync: upload local changes if the cloud copy hasn't changed since the last sync. Bounded by a
    /// ~30 s timeout; on any failure the app closes anyway and the next start uploads the pending changes.
    /// </summary>
    public async Task ShutdownAsync()
    {
        using var cts = new CancellationTokenSource(ShutdownTimeout);
        try
        {
            if (!_current.IsCompleted)
            {
                var finished = await Task.WhenAny(_current, Task.Delay(Timeout.Infinite, cts.Token));
                if (finished != _current)
                {
                    Log.Warning("Shutdown sync skipped: a sync was still running");
                    return;
                }
            }

            if (_service is null || !_uploadOnShutdownAllowed)
            {
                Log.Information("Shutdown sync skipped (configured: {Configured}, signed in: {SignedIn}, allowed this session: {Allowed})",
                    _auth.IsConfigured, _service is not null, _uploadOnShutdownAllowed);
                return;
            }

            var result = await _service.UploadOnShutdownAsync(cts.Token);
            Log.Information("Shutdown sync finished: {Result}", result.Kind);
        }
        catch (Exception ex) when (IsAuthorizationFailure(ex))
        {
            Log.Warning(ex, "Shutdown sync: Google authorization failed; changes stay on this device and upload after the next sign-in");
            await TryClearTokenAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown sync failed or timed out; changes stay on this device and upload at the next start");
        }
        finally
        {
            DropService();
        }
    }

    private Task RunExclusive(Func<Task> work)
    {
        if (!_current.IsCompleted)
        {
            return _current;
        }

        _current = work();
        return _current;
    }

    private async Task SyncAsync(bool interactiveSignIn)
    {
        _uploadOnShutdownAllowed = false;

        if (!_auth.IsConfigured)
        {
            Log.Information("Sync not configured: {Path} not found", AppPaths.ClientSecretPath);
            Status.Set("Sync not configured",
                "To sync through Google Drive, put client_secret.json next to the app (see HOWTO.md).", "Retry");
            return;
        }

        Status.SetBusy(interactiveSignIn ? "Waiting for Google sign-in…" : "Syncing…");
        try
        {
            if (_service is null)
            {
                var credential = interactiveSignIn
                    ? await SignInAsync()
                    : await _auth.TryGetStoredCredentialAsync(CancellationToken.None);
                if (credential is null)
                {
                    ShowNeedsSignIn();
                    return;
                }

                _store = new GoogleDriveFileStore(credential);
                _service = new SyncService(_store, AppPaths.DatabasePath, AppPaths.SyncStatePath, _gate);
                _needsSignIn = false;
                Status.SetBusy("Syncing…");
            }

            // From here the hash rules keep the shutdown upload safe even if this sync fails part-way.
            _uploadOnShutdownAllowed = true;

            SyncPlan plan;
            using (var cts = new CancellationTokenSource(PlanTimeout))
            {
                plan = await _service.PlanAsync(cts.Token);
            }

            if (await ResolveAsync(plan) is not { } action)
            {
                return;
            }

            SyncResult result;
            using (var cts = new CancellationTokenSource(TransferTimeout))
            {
                result = await _service.ExecuteAsync(plan, action, cts.Token);
            }

            await ReportAsync(plan, result);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(ex);
        }
    }

    /// <summary>Turns plan actions that need the user into Upload/Download, or null to sync nothing this session.</summary>
    private async Task<SyncAction?> ResolveAsync(SyncPlan plan)
    {
        switch (plan.Action)
        {
            case SyncAction.Conflict:
            {
                Log.Information("Sync conflict: asking the user which copy to keep");
                var choice = await AskAsync(
                    "Choose which data to keep",
                    "This device and Google Drive both have changes the other doesn't have. They can't be merged, so choose one copy to keep.\n\n" +
                    "• Keep this device's data: upload it. The Google Drive copy stays in Drive's version history.\n" +
                    "• Keep cloud data: download it. This device's data is backed up first.\n" +
                    "• Work offline for now: change nothing and ask again next time the app starts.",
                    "Keep this device's data", "Keep cloud data", "Work offline for now");
                Log.Information("Sync conflict resolved by user: {Choice}", choice switch { 0 => "keep this device", 1 => "keep cloud", _ => "work offline" });
                return choice switch
                {
                    0 => SyncAction.Upload,
                    1 => SyncAction.Download,
                    _ => PauseSync()
                };
            }

            case SyncAction.CloudFileDeleted:
            {
                Log.Information("Cloud file was deleted: asking the user before re-uploading");
                var choice = await AskAsync(
                    "Google Drive copy is missing",
                    "The pomodoro.db file this device syncs with is no longer in your Google Drive (it was deleted or moved to the bin). " +
                    "Upload this device's data as a new copy?",
                    "Upload this device's data", "Work offline for now");
                Log.Information("Deleted cloud file: user chose {Choice}", choice == 0 ? "re-upload" : "work offline");
                return choice == 0 ? SyncAction.Upload : PauseSync();
            }

            case SyncAction.AmbiguousCloudFiles:
                await AskAsync(
                    "Several Google Drive copies found",
                    $"Found {plan.CloudMatchCount} pomodoro.db files from this app in your Google Drive, so sync can't tell which one is right. " +
                    "Nothing was changed. Delete the copies you don't want in Google Drive, then press Retry.",
                    "OK");
                return PauseSync("Sync paused — several copies in Google Drive");

            default:
                return plan.Action;
        }
    }

    private SyncAction? PauseSync(string statusText = "Sync paused — working offline")
    {
        _uploadOnShutdownAllowed = false;
        Status.Set(statusText, "Nothing is uploaded or downloaded this session. You'll be asked again next time the app starts.", "Retry");
        return null;
    }

    private async Task ReportAsync(SyncPlan plan, SyncResult result)
    {
        var time = DateTime.Now.ToString("HH:mm");
        switch (result.Kind)
        {
            case SyncResultKind.Downloaded:
                RaiseLocalDatabaseReplaced();
                Status.Set($"Synced {time}",
                    result.BackupPath is null
                        ? "Downloaded your data from Google Drive."
                        : $"Downloaded your data from Google Drive. The previous data on this device was backed up to {result.BackupPath}.");
                break;

            case SyncResultKind.Uploaded when plan.Action == SyncAction.Upload:
                // L != S, C == S: the last session's changes never reached the cloud (crash or offline close).
                Status.Set($"Synced {time} · uploaded changes from last time",
                    "Changes from your last session hadn't reached Google Drive yet; they've been uploaded now.");
                break;

            case SyncResultKind.DownloadVerificationFailed:
                Status.Set("Sync problem", "The Google Drive copy couldn't be verified, so it wasn't used. Your data on this device is unchanged.", "Retry");
                await AskAsync("Couldn't use the Google Drive copy",
                    "The copy in Google Drive couldn't be verified as a valid database, so it wasn't used. Your data on this device is unchanged.",
                    "OK");
                break;

            default:
                Status.Set($"Synced {time}");
                break;
        }
    }

    private void RaiseLocalDatabaseReplaced()
    {
        try
        {
            LocalDatabaseReplaced?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, "Reloading data after sync");
        }
    }

    private async Task HandleFailureAsync(Exception ex)
    {
        switch (ex)
        {
            case var _ when IsAuthorizationFailure(ex):
                Log.Warning(ex, "Google authorization failed; the user needs to sign in again");
                _uploadOnShutdownAllowed = false;
                await TryClearTokenAsync();
                DropService();
                ShowNeedsSignIn();
                break;

            case GoogleApiException { HttpStatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests }:
                Log.Warning(ex, "Google Drive refused or rate-limited the request after retries");
                Status.Set("Offline — will sync later", OfflineDetail, "Retry");
                break;

            case HttpRequestException or OperationCanceledException or TimeoutException or GoogleApiException:
                Log.Warning(ex, "Couldn't reach Google Drive; working offline");
                Status.Set("Offline — will sync later", OfflineDetail, "Retry");
                break;

            case SqliteException or IOException or UnauthorizedAccessException:
                Log.Error(ex, "Sync failed on a local file");
                Status.Set("Sync problem",
                    $"Couldn't read or write a local file while syncing. Your data wasn't changed. Details are in the log in {AppPaths.AppDirectory}.", "Retry");
                break;

            default:
                Log.Error(ex, "Sync failed");
                Status.Set("Sync problem", $"Sync failed. Your data is saved on this device. Details are in the log in {AppPaths.AppDirectory}.", "Retry");
                break;
        }
    }

    private static bool IsAuthorizationFailure(Exception ex) =>
        ex is TokenResponseException or SignInRequiredException
        || ex is GoogleApiException { HttpStatusCode: HttpStatusCode.Unauthorized };

    private void ShowNeedsSignIn()
    {
        _needsSignIn = true;
        Status.Set("Needs sign-in", "Sign in with your Google account to sync your history through Google Drive.", "Sign in");
    }

    /// <summary>Interactive sign-in with a dialog that shows the sign-in link and lets the user cancel.</summary>
    private async Task<UserCredential?> SignInAsync()
    {
        var owner = _owner();
        using var cts = new CancellationTokenSource(SignInTimeout);
        MessageDialog? dialog = null;
        var finished = false;

        void OnSignInUrl(string url) => Dispatcher.UIThread.Post(() =>
        {
            if (finished || dialog is not null || owner is null)
            {
                return;
            }

            dialog = new MessageDialog(
                "Sign in to Google",
                "Your browser should open so you can sign in and let Pomodoro Timer use its own file in your Google Drive. " +
                "If it didn't open, copy this link into your browser:",
                new[] { "Cancel" },
                url);
            dialog.Closed += (_, _) =>
            {
                if (!finished)
                {
                    cts.Cancel();
                }
            };
            _ = dialog.ShowDialog(owner);
        });

        try
        {
            return await _auth.SignInAsync(OnSignInUrl, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Google sign-in cancelled or timed out");
            return null;
        }
        catch (TokenResponseException ex)
        {
            Log.Warning(ex, "Google sign-in was not completed");
            return null;
        }
        finally
        {
            finished = true;
            dialog?.Close();
        }
    }

    private async Task<int?> AskAsync(string title, string message, params string[] buttons)
    {
        var owner = _owner();
        if (owner is null)
        {
            Log.Warning("No window to show '{Title}'; treating it as dismissed", title);
            return null;
        }

        return await MessageDialog.ShowAsync(owner, title, message, buttons);
    }

    private async Task TryClearTokenAsync()
    {
        try
        {
            await _auth.ClearTokenAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Couldn't delete the stored Google token");
        }
    }

    private void DropService()
    {
        _store?.Dispose();
        _store = null;
        _service = null;
    }
}
