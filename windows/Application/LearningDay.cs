using Lexi.Social;

namespace Lexi;

/// <summary>Single source of truth for the product's fixed UTC+8 learning day.</summary>
public static class LearningDay
{
    public static DateOnly TodayBjt(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Learning day input must be UTC.", nameof(utcNow));
        return SocialDayRules.DateAt(new DateTimeOffset(utcNow));
    }

    public static DateOnly TodayBjt(DateTimeOffset now) => SocialDayRules.DateAt(now);
}
