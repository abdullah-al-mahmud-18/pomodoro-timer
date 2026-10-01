using System;
using System.Collections.Generic;
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

    // Synced one after the other, each against its own Drive file and state file. The db goes first: it matters most.
    private readonly SyncTarget _database = new(
        "pomodoro.db", "data", DriveFileSpec.Database, () => new DatabaseSyncFile(AppPaths.DatabasePath), AppPaths.SyncStatePath);
    private readonly SyncTarget _names = new(
        "names.txt", "names list", DriveFileSpec.Names, () => new NamesSyncFile(AppPaths.NamesPath), AppPaths.NamesSyncStatePath);

    private bool _needsSignIn;
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

    /// <summary>Raised on the UI thread after a download replaced names.txt, so the Name list can be reloaded.</summary>
    public event Action? NamesReplaced;

    private SyncTarget[] Targets => new[] { _database, _names };

    /// <summary>Startup sync. Runs before the database is opened; never signs in interactively.</summary>
    public Task StartupAsync() => RunExclusive(() => SyncAsync(interactiveSignIn: false));

    /// <summary>The status bar's Retry / Sign in button.</summary>
    public Task RetryAsync() => RunExclusive(() => SyncAsync(interactiveSignIn: _needsSignIn));

    /// <summary>
    /// Shutdown sync: upload local changes to each file if its cloud copy hasn't changed since the last sync.
    /// Bounded by a ~30 s timeout; on any failure the app closes anyway and the next start uploads the pending changes.
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

            foreach (var target in Targets)
            {
                if (target.Service is null || !target.UploadOnShutdownAllowed)
                {
                    Log.Information("Shutdown sync of {File} skipped (configured: {Configured}, signed in: {SignedIn}, allowed this session: {Allowed})",
                        target.FileName, _auth.IsConfigured, target.Service is not null, target.UploadOnShutdownAllowed);
                    continue;
                }

                try
                {
                    var result = await target.Service.UploadOnShutdownAsync(cts.Token);
                    Log.Information("Shutdown sync of {File} finished: {Result}", target.FileName, result.Kind);
                }
                catch (Exception ex) when (IsAuthorizationFailure(ex))
                {
                    Log.Warning(ex, "Shutdown sync: Google authorization failed; changes stay on this device and upload after the next sign-in");
                    await TryClearTokenAsync();
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Shutdown sync of {File} failed or timed out; changes stay on this device and upload at the next start", target.FileName);
                }
            }
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
        foreach (var target in Targets)
        {
            target.UploadOnShutdownAllowed = false;
        }

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
            if (_database.Service is null)
            {
                var credential = interactiveSignIn
                    ? await SignInAsync()
                    : await _auth.TryGetStoredCredentialAsync(CancellationToken.None);
                if (credential is null)
                {
                    ShowNeedsSignIn();
                    return;
                }

                foreach (var target in Targets)
                {
                    target.Connect(credential, _gate);
                }
                _needsSignIn = false;
                Status.SetBusy("Syncing…");
            }

            var report = new SyncReport();
            foreach (var target in Targets)
            {
                await SyncTargetAsync(target, report);
            }

            report.Apply(Status);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(ex);
        }
    }

    private async Task SyncTargetAsync(SyncTarget target, SyncReport report)
    {
        var service = target.Service!;

        // From here the hash rules keep the shutdown upload safe even if this sync fails part-way.
        target.UploadOnShutdownAllowed = true;

        SyncPlan plan;
        using (var cts = new CancellationTokenSource(PlanTimeout))
        {
            plan = await service.PlanAsync(cts.Token);
        }

        if (await ResolveAsync(target, plan, report) is not { } action)
        {
            return;
        }

        SyncResult result;
        using (var cts = new CancellationTokenSource(TransferTimeout))
        {
            result = await service.ExecuteAsync(plan, action, cts.Token);
        }

        await ReportAsync(target, plan, result, report);
    }

    /// <summary>Turns plan actions that need the user into Upload/Download, or null to sync nothing this session.</summary>
    private async Task<SyncAction?> ResolveAsync(SyncTarget target, SyncPlan plan, SyncReport report)
    {
        var isDatabase = target == _database;
        switch (plan.Action)
        {
            case SyncAction.Conflict:
            {
                Log.Information("Sync conflict on {File}: asking the user which copy to keep", target.FileName);
                var choice = await AskAsync(
                    isDatabase ? "Choose which data to keep" : "Choose which names list to keep",
                    (isDatabase
                        ? "This device and Google Drive both have changes the other doesn't have. They can't be merged, so choose one copy to keep.\n\n"
                        : "Your names list (names.txt) was changed both on this device and in Google Drive. They can't be merged, so choose one copy to keep.\n\n") +
                    $"• Keep this device's {target.Noun}: upload it. The Google Drive copy stays in Drive's version history.\n" +
                    $"• Keep cloud {target.Noun}: download it. This device's {target.Noun} is backed up first.\n" +
                    $"• Work offline for now: change nothing and ask again next time the app starts.",
                    $"Keep this device's {target.Noun}", $"Keep cloud {target.Noun}", "Work offline for now");
                Log.Information("Sync conflict on {File} resolved by user: {Choice}", target.FileName,
                    choice switch { 0 => "keep this device", 1 => "keep cloud", _ => "work offline" });
                return choice switch
                {
                    0 => SyncAction.Upload,
                    1 => SyncAction.Download,
                    _ => Pause(target, report)
                };
            }

            case SyncAction.CloudFileDeleted:
            {
                Log.Information("Cloud copy of {File} was deleted: asking the user before re-uploading", target.FileName);
                var choice = await AskAsync(
                    "Google Drive copy is missing",
                    $"The {target.FileName} file this device syncs with is no longer in your Google Drive (it was deleted or moved to the bin). " +
                    $"Upload this device's {target.Noun} as a new copy?",
                    $"Upload this device's {target.Noun}", "Work offline for now");
                Log.Information("Deleted cloud {File}: user chose {Choice}", target.FileName, choice == 0 ? "re-upload" : "work offline");
                return choice == 0 ? SyncAction.Upload : Pause(target, report);
            }

            case SyncAction.AmbiguousCloudFiles:
                await AskAsync(
                    "Several Google Drive copies found",
                    $"Found {plan.CloudMatchCount} {target.FileName} files from this app in your Google Drive, so sync can't tell which one is right. " +
                    "Nothing was changed. Delete the copies you don't want in Google Drive, then press Retry.",
                    "OK");
                return Pause(target, report, "Sync paused — several copies in Google Drive");

            default:
                return plan.Action;
        }
    }

    private static SyncAction? Pause(SyncTarget target, SyncReport report, string statusText = "Sync paused — working offline")
    {
        target.UploadOnShutdownAllowed = false;
        report.PausedStatus ??= statusText;
        report.Details.Add($"{target.FileName}: nothing is uploaded or downloaded this session. You'll be asked again next time the app starts.");
        return null;
    }

    private async Task ReportAsync(SyncTarget target, SyncPlan plan, SyncResult result, SyncReport report)
    {
        var isDatabase = target == _database;
        var backupNote = result.BackupPath is null ? "" : $" The previous copy on this device was backed up to {result.BackupPath}.";
        switch (result.Kind)
        {
            case SyncResultKind.Downloaded when isDatabase:
                RaiseReplaced(LocalDatabaseReplaced, "Reloading data after sync");
                report.Details.Add("Downloaded your data from Google Drive." + backupNote);
                break;

            case SyncResultKind.Downloaded:
                RaiseReplaced(NamesReplaced, "Reloading names after sync");
                report.Details.Add("Downloaded your names list from Google Drive." + backupNote);
                break;

            case SyncResultKind.Uploaded when isDatabase && plan.Action == SyncAction.Upload:
                // L != S, C == S: the last session's changes never reached the cloud (crash or offline close).
                report.UploadedPendingData = true;
                report.Details.Add("Changes from your last session hadn't reached Google Drive yet; they've been uploaded now.");
                break;

            case SyncResultKind.Uploaded when !isDatabase && plan.Action == SyncAction.Upload:
                // Usually the user edited names.txt while the app was closed — expected, so no notice in the status text.
                report.Details.Add("Uploaded your updated names list.");
                break;

            case SyncResultKind.DownloadVerificationFailed:
                report.Problem = true;
                report.Details.Add($"The Google Drive copy of {target.FileName} couldn't be verified, so it wasn't used. The copy on this device is unchanged.");
                await AskAsync($"Couldn't use the Google Drive copy of {target.FileName}",
                    isDatabase
                        ? "The copy in Google Drive couldn't be verified as a valid database, so it wasn't used. Your data on this device is unchanged."
                        : "The names list in Google Drive couldn't be verified as a text file, so it wasn't used. The names list on this device is unchanged.",
                    "OK");
                break;
        }
    }

    private static void RaiseReplaced(Action? handler, string context)
    {
        try
        {
            handler?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report(ex, context);
        }
    }

    private async Task HandleFailureAsync(Exception ex)
    {
        switch (ex)
        {
            case var _ when IsAuthorizationFailure(ex):
                Log.Warning(ex, "Google authorization failed; the user needs to sign in again");
                foreach (var target in Targets)
                {
                    target.UploadOnShutdownAllowed = false;
                }
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
        foreach (var target in Targets)
        {
            target.Disconnect();
        }
    }

    /// <summary>One synced file: its Drive file, local file, and per-session sync state.</summary>
    private sealed class SyncTarget
    {
        private readonly DriveFileSpec _spec;
        private readonly Func<SyncedFile> _createFile;
        private readonly string _statePath;
        private GoogleDriveFileStore? _store;

        public SyncTarget(string fileName, string noun, DriveFileSpec spec, Func<SyncedFile> createFile, string statePath)
        {
            FileName = fileName;
            Noun = noun;
            _spec = spec;
            _createFile = createFile;
            _statePath = statePath;
        }

        /// <summary>The local file name shown to the user, e.g. "names.txt".</summary>
        public string FileName { get; }

        /// <summary>What the dialogs call its content, e.g. "Keep this device's data".</summary>
        public string Noun { get; }

        public SyncService? Service { get; private set; }

        public bool UploadOnShutdownAllowed { get; set; }

        public void Connect(UserCredential credential, ILocalFileGate gate)
        {
            _store = new GoogleDriveFileStore(credential, _spec);
            Service = new SyncService(_store, _createFile(), _statePath, gate);
        }

        public void Disconnect()
        {
            _store?.Dispose();
            _store = null;
            Service = null;
        }
    }

    /// <summary>What one startup/retry sync did across all files, turned into the status bar text at the end.</summary>
    private sealed class SyncReport
    {
        public List<string> Details { get; } = new();
        public string? PausedStatus { get; set; }
        public bool Problem { get; set; }
        public bool UploadedPendingData { get; set; }

        public void Apply(SyncStatusViewModel status)
        {
            var detail = Details.Count > 0 ? string.Join("\n", Details) : null;
            if (Problem)
            {
                status.Set("Sync problem", detail, "Retry");
            }
            else if (PausedStatus is not null)
            {
                status.Set(PausedStatus, detail, "Retry");
            }
            else
            {
                var time = DateTime.Now.ToString("HH:mm");
                status.Set(UploadedPendingData ? $"Synced {time} · uploaded changes from last time" : $"Synced {time}", detail);
            }
        }
    }
}
