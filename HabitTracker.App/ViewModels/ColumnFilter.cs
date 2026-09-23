namespace HabitTracker.App.ViewModels;

/// <summary>Column tabs for dailies. "Due" means not yet completed today.</summary>
public enum DailyFilter
{
    All,
    Due,
    NotDue,
}

/// <summary>Column tabs for one-off tasks, which have no schedule to be due against.</summary>
public enum TaskFilter
{
    Active,
    Complete,
}
