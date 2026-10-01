using PomodoroTimer.Core.Names;
using Xunit;

namespace PomodoroTimer.Core.Tests;

public class NameListTests
{
    [Fact]
    public void Parse_TrimsSkipsBlanksAndKeepsFileOrder()
    {
        var names = NameList.Parse("Deep work\n\n  Reading  \r\nEmail\r\n   \n");
        Assert.Equal(new[] { "Deep work", "Reading", "Email" }, names);
    }

    [Fact]
    public void Parse_DropsCaseInsensitiveDuplicates_FirstSpellingWins()
    {
        var names = NameList.Parse("Reading\nREADING\nreading \nWriting");
        Assert.Equal(new[] { "Reading", "Writing" }, names);
    }

    [Fact]
    public void Parse_IgnoresByteOrderMark()
    {
        Assert.Equal(new[] { "Reading" }, NameList.Parse("﻿Reading\n"));
    }

    [Fact]
    public void Find_MatchesCaseInsensitively_AndReturnsTheListSpelling()
    {
        var names = new[] { "Deep work", "Reading" };
        Assert.Equal("Deep work", NameList.Find(names, "  deep WORK "));
        Assert.Null(NameList.Find(names, "Deep"));
        Assert.Null(NameList.Find(names, "   "));
        Assert.Null(NameList.Find(names, null));
    }

    [Fact]
    public void LoadOrCreate_CreatesAnEmptyFileWhenMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pomodoro-names-test-{Guid.NewGuid()}.txt");
        try
        {
            Assert.Empty(NameList.LoadOrCreate(path));
            Assert.True(File.Exists(path));

            File.WriteAllText(path, "Reading\nWriting\n");
            Assert.Equal(new[] { "Reading", "Writing" }, NameList.LoadOrCreate(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
