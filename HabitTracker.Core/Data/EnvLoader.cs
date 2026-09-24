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

    /// <summary>
    /// Loads the nearest .env at or above <paramref name="startDirectory"/> that names
    /// <see cref="ConnectionKey"/>, and returns the file used; null when none of them does.
    ///
    /// A file that never mentions the key is walked past rather than accepted, because both start
    /// directories exist so the app works from either the project folder or bin/Debug — a .env
    /// belonging to some other tool would otherwise count as a successful find and hide the real one.
    /// A value already in the environment wins over the file's, so an exported HABIT_CONNECTION is an
    /// override rather than something the file silently replaces.
    /// </summary>
    public static string? LoadFrom(string startDirectory)
    {
        var candidate = FindEnvFile(startDirectory);
        if (candidate is null)
        {
            return null;
        }

        Env.Load(candidate, new LoadOptions(clobberExistingVars: false));
        return candidate;
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
                if (LoadFrom(start) is not null)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The nearest .env at or above <paramref name="startDirectory"/> that names the connection key.
    /// Nothing here interprets the value — DotNetEnv parses it. This only decides which file is the
    /// one worth parsing, and a blank <c>HABIT_CONNECTION=</c> line counts as naming the key, so a
    /// half-filled file fails loudly instead of silently falling through to a different database.
    /// </summary>
    private static string? FindEnvFile(string startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate) && NamesConnectionKey(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool NamesConnectionKey(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.TrimStart();

            // dotenv allows the shell-style "export KEY=value", and skipped comments are what keep a
            // commented-out key from deciding which file wins.
            if (trimmed.StartsWith("export ", StringComparison.Ordinal))
            {
                trimmed = trimmed["export ".Length..].TrimStart();
            }

            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator > 0 && trimmed[..separator].Trim() == ConnectionKey)
            {
                return true;
            }
        }

        return false;
    }
}
