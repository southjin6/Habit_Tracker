using DotNetEnv;

namespace HabitTracker.Core.Data;

/// <summary>
/// Resolves the MySQL connection string from a .env file at the repository root.
///
/// The search walks upward from both the process working directory and the assembly location,
/// so the app finds .env whether it is launched via `dotnet run` from the project folder or by
/// double-clicking the exe inside bin/Debug.
/// </summary>
public static class EnvLoader
{
    public const string ConnectionKey = "HABIT_CONNECTION";

    private static readonly object LoadLock = new();
    private static bool _loaded;

    public static string GetConnectionString()
    {
        EnsureLoaded();

        var value = Environment.GetEnvironmentVariable(ConnectionKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{ConnectionKey} is not set. Copy .env.example to .env at the repository root " +
                "and fill in your MySQL credentials.");
        }

        return value;
    }

    private static void EnsureLoaded()
    {
        lock (LoadLock)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                if (TryLoadFrom(start))
                {
                    return;
                }
            }
        }
    }

    private static bool TryLoadFrom(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return false;
        }

        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
            {
                Env.Load(candidate);
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }
}
