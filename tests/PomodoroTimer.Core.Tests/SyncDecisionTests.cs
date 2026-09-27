using PomodoroTimer.Core.Sync;
using Xunit;

namespace PomodoroTimer.Core.Tests;

/// <summary>One test per row of the startup decision table, plus the edge cases around it.</summary>
public class SyncDecisionTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string X = "cccccccccccccccccccccccccccccccc";

    [Fact]
    public void NoCloudFile_UploadsLocal()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, null, null, CloudExists: false, StateExists: false, LocalHasSessions: true));
        Assert.Equal(SyncAction.CreateCloudFile, action);
    }

    [Fact]
    public void NoCloudFile_WithStateFromEarlierSync_UploadsLocal()
    {
        // Row 1 applies whatever the state says, as long as the cloud file wasn't one this device synced with.
        var action = SyncDecision.Decide(new SyncInputs(A, null, B, CloudExists: false, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.CreateCloudFile, action);
    }

    [Fact]
    public void NoCloudFile_NoLocalDb_DoesNothing()
    {
        var action = SyncDecision.Decide(new SyncInputs(null, null, null, CloudExists: false, StateExists: false, LocalHasSessions: false));
        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void SyncedCloudFileWasDeleted_AsksBeforeReuploading()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, null, A, CloudExists: false, StateExists: true, LocalHasSessions: true, CloudFileDeleted: true));
        Assert.Equal(SyncAction.CloudFileDeleted, action);
    }

    [Fact]
    public void CloudExists_NoState_LocalMissing_Downloads()
    {
        var action = SyncDecision.Decide(new SyncInputs(null, B, null, CloudExists: true, StateExists: false, LocalHasSessions: false));
        Assert.Equal(SyncAction.Download, action);
    }

    [Fact]
    public void CloudExists_NoState_LocalHasNoSessions_Downloads()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, B, null, CloudExists: true, StateExists: false, LocalHasSessions: false));
        Assert.Equal(SyncAction.Download, action);
    }

    [Fact]
    public void CloudExists_NoState_LocalHasSessions_IsConflict()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, B, null, CloudExists: true, StateExists: false, LocalHasSessions: true));
        Assert.Equal(SyncAction.Conflict, action);
    }

    [Fact]
    public void LocalEqualsSynced_CloudEqualsSynced_InSync()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, A, A, CloudExists: true, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void LocalEqualsSynced_CloudChanged_Downloads()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, B, A, CloudExists: true, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.Download, action);
    }

    [Fact]
    public void LocalChanged_CloudEqualsSynced_Uploads()
    {
        var action = SyncDecision.Decide(new SyncInputs(B, A, A, CloudExists: true, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.Upload, action);
    }

    [Fact]
    public void LocalChanged_CloudChanged_IsConflict()
    {
        var action = SyncDecision.Decide(new SyncInputs(B, X, A, CloudExists: true, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.Conflict, action);
    }

    [Fact]
    public void LocalIdenticalToCloud_IsInSync_EvenWithoutState()
    {
        var action = SyncDecision.Decide(new SyncInputs(A, A, null, CloudExists: true, StateExists: false, LocalHasSessions: true));
        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void LocalIdenticalToCloud_IsInSync_EvenWhenStateIsStale()
    {
        var action = SyncDecision.Decide(new SyncInputs(B, B, A, CloudExists: true, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.None, action);
    }

    [Fact]
    public void StateExists_LocalDbMissing_Downloads()
    {
        var action = SyncDecision.Decide(new SyncInputs(null, B, A, CloudExists: true, StateExists: true, LocalHasSessions: false));
        Assert.Equal(SyncAction.Download, action);
    }

    [Fact]
    public void HashComparison_IgnoresCase()
    {
        var action = SyncDecision.Decide(new SyncInputs(A.ToUpperInvariant(), A, A, CloudExists: true, StateExists: true, LocalHasSessions: true));
        Assert.Equal(SyncAction.None, action);
    }
}
