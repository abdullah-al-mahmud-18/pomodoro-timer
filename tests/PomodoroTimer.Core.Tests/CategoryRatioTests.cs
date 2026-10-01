using PomodoroTimer.Core.Models;
using Xunit;

namespace PomodoroTimer.Core.Tests;

public class CategoryRatioTests
{
    private static CategoryRatio? Ratio(int work, int study, int brk) =>
        CategoryRatio.From(new CategoryTotals { WorkSeconds = work, StudySeconds = study, BreakSeconds = brk });

    [Fact]
    public void ExactShares_AreReportedAsIs()
    {
        var ratio = Ratio(500, 200, 300)!;

        Assert.Equal(50, ratio.WorkPercent);
        Assert.Equal(20, ratio.StudyPercent);
        Assert.Equal(30, ratio.BreakPercent);
    }

    [Fact]
    public void ThirdsStillSumTo100()
    {
        var ratio = Ratio(100, 100, 100)!;

        Assert.Equal(100, ratio.WorkPercent + ratio.StudyPercent + ratio.BreakPercent);
        Assert.All(new[] { ratio.WorkPercent, ratio.StudyPercent, ratio.BreakPercent }, p => Assert.InRange(p, 33, 34));
    }

    [Fact]
    public void LeftoverGoesToLargestRemainder()
    {
        // 66.67% / 33.33% / 0% → floors 66 / 33 / 0, and the missing 1% goes to Work
        var ratio = Ratio(200, 100, 0)!;

        Assert.Equal(67, ratio.WorkPercent);
        Assert.Equal(33, ratio.StudyPercent);
        Assert.Equal(0, ratio.BreakPercent);
    }

    [Fact]
    public void SingleCategory_Is100Percent()
    {
        var ratio = Ratio(0, 1234, 0)!;

        Assert.Equal(0, ratio.WorkPercent);
        Assert.Equal(100, ratio.StudyPercent);
        Assert.Equal(0, ratio.BreakPercent);
    }

    [Fact]
    public void NoTimeRecorded_ReturnsNull()
    {
        Assert.Null(Ratio(0, 0, 0));
    }

    [Fact]
    public void LargeTotals_DoNotOverflow()
    {
        var ratio = Ratio(int.MaxValue, int.MaxValue, 0)!;

        Assert.Equal(50, ratio.WorkPercent);
        Assert.Equal(50, ratio.StudyPercent);
    }
}
