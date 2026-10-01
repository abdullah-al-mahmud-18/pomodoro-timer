namespace PomodoroTimer.Core.Models;

/// <summary>
/// Each <see cref="SessionCategory"/>'s share of the total time in a <see cref="CategoryTotals"/>, as whole percentages
/// that always add up to exactly 100.
/// </summary>
public class CategoryRatio
{
    public int WorkPercent { get; init; }
    public int StudyPercent { get; init; }
    public int BreakPercent { get; init; }

    /// <summary>
    /// Rounds with the largest-remainder method so the three percentages sum to 100. Returns null when no time was
    /// recorded, since there's no ratio to show.
    /// </summary>
    public static CategoryRatio? From(CategoryTotals totals)
    {
        long[] seconds = { totals.WorkSeconds, totals.StudySeconds, totals.BreakSeconds };
        var total = seconds.Sum();
        if (total <= 0)
        {
            return null;
        }

        var percents = seconds.Select(s => (int)(s * 100 / total)).ToArray();
        var leftover = 100 - percents.Sum();
        foreach (var i in Enumerable.Range(0, seconds.Length).OrderByDescending(i => seconds[i] * 100 % total).Take(leftover))
        {
            percents[i]++;
        }

        return new CategoryRatio { WorkPercent = percents[0], StudyPercent = percents[1], BreakPercent = percents[2] };
    }
}
