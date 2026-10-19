using System;
using System.Collections.Generic;
using System.Linq;

namespace Lexi.Features.Learning;

/// <summary>
/// 计划选词的范围状态与辅助逻辑，保持与领域模型和存储契约严格解耦。
/// </summary>
public sealed class PlanCreationState
{
    public sealed record ScheduleEstimate(
        int Days,
        string ChineseDescription,
        string EnglishDescription);

    public DailyStudyPlanSource Source { get; }
    public HashSet<string> SelectedIds { get; } = new(StringComparer.Ordinal);
    public string PlanName { get; set; } = "";
    public int DailyWordCount { get; set; } = 20;
    public bool RandomOrder { get; set; }
    public string SearchText { get; set; } = "";
    public bool OnlyShowSelected { get; set; }
    public int PageIndex { get; set; }
    public const int PageSize = 10;

    public PlanCreationState(DailyStudyPlanSource source, string defaultName)
    {
        Source = source;
        PlanName = defaultName;
    }

    /// <summary>
    /// 计算预计天数与尾日剩余词数说明。
    /// </summary>
    public static ScheduleEstimate CalculateSchedule(int totalWords, int dailyQuota)
    {
        if (totalWords <= 0)
        {
            return new ScheduleEstimate(
                0,
                "请至少选择 1 个词条",
                "Select at least one entry.");
        }

        if (dailyQuota <= 0)
        {
            return new ScheduleEstimate(
                0,
                "每日词数需大于 0",
                "The daily target must be greater than zero.");
        }

        var days = (totalWords + dailyQuota - 1) / dailyQuota;
        if (dailyQuota >= totalWords)
        {
            return new ScheduleEstimate(
                1,
                $"每日目标超过或等于选词总数，预计 1 天完成（首日学习全部 {totalWords} 词）。",
                $"The daily target is at least the selected total; completion is expected in 1 day (all {totalWords} words on day 1).");
        }

        var remainder = totalWords % dailyQuota;
        if (remainder == 0)
        {
            return new ScheduleEstimate(
                days,
                $"预计 {days} 天完成（每天学习 {dailyQuota} 词）。",
                $"Completion is expected in {days} days ({dailyQuota} words per day).");
        }

        return new ScheduleEstimate(
            days,
            $"预计 {days} 天完成（前 {days - 1} 天每天 {dailyQuota} 词，最后 1 天学习剩余 {remainder} 词）。",
            $"Completion is expected in {days} days ({dailyQuota} words per day for the first {days - 1} days, then {remainder} words on the final day).");
    }

    public static (int Days, string Description) CalculateEstimate(int totalWords, int dailyQuota)
    {
        var estimate = CalculateSchedule(totalWords, dailyQuota);
        return (estimate.Days, estimate.ChineseDescription);
    }
}

/// <summary>
/// 计划冲突明细展示模型。
/// </summary>
public sealed record PlanConflictInfo(string PlanName, IReadOnlyList<string> ConflictingWords);
