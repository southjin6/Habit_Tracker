using HabitTracker.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HabitTracker.App;

/// <summary>
/// The database half of startup, kept out of App.xaml.cs so it can be tested.
///
/// Named DatabaseStartup rather than Startup because inside App the simple name binds to
/// Application.Startup, the inherited event, and every call site dies with CS0079.
///
/// Resolving IDbContextFactory runs the options action registered by AddDbContextFactory, and that
/// action reads the connection string, so resolving it is itself a way to fail — a missing or blank
/// .env throws there. Every step therefore lives inside the try; returning the message rather than
/// throwing is what lets startup show it and still exit quietly.
/// </summary>
public static class DatabaseStartup
{
    public static string? PrepareDatabase(IServiceProvider services)
    {
        try
        {
            using var db = services.GetRequiredService<IDbContextFactory<HabitDbContext>>().CreateDbContext();
            db.Database.Migrate();
            db.Database.Migrate();
            return null;
        }
        catch (Exception ex)
        {
            return "Habit Tracker could not reach MySQL, or its migrations failed to apply." +
                   Environment.NewLine + Environment.NewLine +
                   "Check that the MySQL service is running and that .env has valid credentials." +
                   Environment.NewLine + Environment.NewLine + ex.Message;
        }
    }
}
