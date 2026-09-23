namespace HabitTracker.Core.Domain;

/// <summary>Result of evaluating a daily against its completion history.</summary>
public readonly record struct StreakInfo(int Streak, int MissedDays, ItemFlag Flag);
