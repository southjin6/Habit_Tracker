using HabitTracker.Core.Data;

namespace HabitTracker.Tests;

/// <summary>
/// The .env lookup. Both rules pinned here are ones where a launch from the wrong directory used to
/// turn into a wrong answer: a .env that never mentions the connection key counted as a successful
/// find and hid the real one, and a value already exported into the environment was replaced by
/// whatever the file said.
/// </summary>
public class EnvLoaderTests
{
    private const string Sentinel = "Server=from-the-environment;Database=ignored";

    [Fact]
    public void LoadFrom_WalksPastAFileThatNeverNamesTheKey_ToTheOneThatDoes()
    {
        var tree = new ScratchTree();
        try
        {
            tree.Write("near/.env", "SOMETHING_ELSE=1");
            var expected = tree.Write(".env", $"HABIT_CONNECTION={Sentinel}");

            WithCleanEnvironment(() =>
                Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(EnvLoader.LoadFrom(tree.Subdirectory("near"))!)));
        }
        finally
        {
            tree.Delete();
        }
    }

    [Fact]
    public void LoadFrom_TreatsABlankKeyAsNamingIt_SoTheSearchStopsThere()
    {
        var tree = new ScratchTree();
        try
        {
            var halfFilled = tree.Write("near/.env", "HABIT_CONNECTION=");
            tree.Write(".env", $"HABIT_CONNECTION={Sentinel}");

            WithCleanEnvironment(() =>
            {
                // The nearest file wins even though its value is blank. Falling through to the file
                // above would connect the app to a database the user is not looking at, while the file
                // they just edited appears to be ignored.
                Assert.Equal(
                    Path.GetFullPath(halfFilled),
                    Path.GetFullPath(EnvLoader.LoadFrom(tree.Subdirectory("near"))!));

                // With nothing in the environment either, the loader is left with nothing — which the
                // caller turns into "HABIT_CONNECTION is not set", not a silent connection elsewhere.
                Assert.True(string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(EnvLoader.ConnectionKey)));
            });
        }
        finally
        {
            tree.Delete();
        }
    }

    [Fact]
    public void LoadFrom_LeavesAnAlreadySetEnvironmentValueAlone()
    {
        var tree = new ScratchTree();
        try
        {
            var file = tree.Write(".env", "HABIT_CONNECTION=Server=from-the-file;Database=file");

            WithCleanEnvironment(() =>
            {
                Environment.SetEnvironmentVariable(EnvLoader.ConnectionKey, Sentinel);

                // The file is still the one that gets read; it simply cannot overwrite what the
                // environment already supplies.
                Assert.Equal(Path.GetFullPath(file), Path.GetFullPath(EnvLoader.LoadFrom(tree.RootPath)!));
                Assert.Equal(Sentinel, Environment.GetEnvironmentVariable(EnvLoader.ConnectionKey));
            });
        }
        finally
        {
            tree.Delete();
        }
    }

    [Fact]
    public void LoadFrom_WithNoStartDirectory_ReturnsNull()
    {
        Assert.Null(EnvLoader.LoadFrom(string.Empty));
        Assert.Null(EnvLoader.LoadFrom("   "));
    }

    /// <summary>
    /// Runs a body with the connection key removed from the environment and puts the original value
    /// back afterwards, so a test that asserts about it cannot leak into the next one — or into the
    /// other test classes, which share this process.
    /// </summary>
    private static void WithCleanEnvironment(Action body)
    {
        var original = Environment.GetEnvironmentVariable(EnvLoader.ConnectionKey);
        Environment.SetEnvironmentVariable(EnvLoader.ConnectionKey, null);
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvLoader.ConnectionKey, original);
        }
    }

    /// <summary>
    /// A directory tree beside the test assembly rather than in a shared temp folder, so a run only
    /// writes inside its own build output and leaves nothing behind.
    /// </summary>
    private sealed class ScratchTree
    {
        public ScratchTree()
        {
            RootPath = Path.Combine(AppContext.BaseDirectory, $"env-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string Subdirectory(string relative)
        {
            var path = Path.Combine(RootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Writes a file, creating its folder, and returns the full path.</summary>
        public string Write(string relative, string contents)
        {
            var path = Path.Combine(RootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Delete()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
                // A leftover scratch tree in the build output is noise, not a test failure.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
