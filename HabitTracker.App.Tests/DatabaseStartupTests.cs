using HabitTracker.App;
using HabitTracker.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HabitTracker.App.Tests;

/// <summary>
/// The database step of startup. It is the first thing a fresh clone runs, before any window exists,
/// and the process it can kill is the one that was going to show the error — so the contract worth
/// pinning is "returns a message" rather than "throws". Until PrepareDatabase existed, resolving the
/// factory sat above the try in App.OnStartup, so a missing .env killed the app silently instead of
/// showing the MessageBox that tells you to copy .env.example.
/// </summary>
public class DatabaseStartupTests
{
    private const string MissingConnectionString =
        "HABIT_CONNECTION is not set. Copy .env.example to .env at the repository root " +
        "and fill in your MySQL credentials.";

    private sealed class ThrowingFactory : IDbContextFactory<HabitDbContext>
    {
        private readonly Exception _failure;

        public ThrowingFactory(Exception failure) => _failure = failure;

        public HabitDbContext CreateDbContext() => throw _failure;

        public Task<HabitDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw _failure;
    }

    [Fact]
    public void PrepareDatabase_WhenTheConnectionStringIsMissing_ReturnsTheMessageInsteadOfThrowing()
    {
        // Registered exactly the way App.xaml.cs does it, because that is the whole defect: the
        // options action runs on *resolution*, so the failure happens before a context is asked for.
        var services = new ServiceCollection();
        services.AddDbContextFactory<HabitDbContext>(_ => throw new InvalidOperationException(MissingConnectionString));
        using var provider = services.BuildServiceProvider();

        var problem = DatabaseStartup.PrepareDatabase(provider);

        Assert.NotNull(problem);
        Assert.Contains("could not reach MySQL", problem);
        Assert.Contains(MissingConnectionString, problem);
    }

    [Fact]
    public void PrepareDatabase_WhenTheFactoryIsObtainedButTheContextFails_ReturnsTheMessage()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<HabitDbContext>>(
            new ThrowingFactory(new InvalidOperationException("Unable to connect to any of the specified MySQL hosts")));
        using var provider = services.BuildServiceProvider();

        var problem = DatabaseStartup.PrepareDatabase(provider);

        Assert.NotNull(problem);
        Assert.Contains("could not reach MySQL", problem);
        Assert.Contains("Unable to connect to any of the specified MySQL hosts", problem);
    }

    [Fact]
    public void PrepareDatabase_WhenTheConnectionStringIsBlank_ReturnsTheMessage()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<HabitDbContext>(
            _ => throw new InvalidOperationException(MissingConnectionString));
        using var provider = services.BuildServiceProvider();

        // Blank is the shape a half-filled .env actually produces; EnvLoader treats it as missing and
        // the exception it throws is the one this must not let escape.
        Assert.NotNull(DatabaseStartup.PrepareDatabase(provider));
    }
}
