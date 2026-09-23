using Microsoft.EntityFrameworkCore;

namespace HabitTracker.Core.Data;

/// <summary>Builds the single DbContext configuration used by both the app and the EF tooling.</summary>
public static class DbContextFactory
{
    // Pinned rather than ServerVersion.AutoDetect so that `dotnet ef migrations add` works without
    // a live database connection. AutoDetect would open one just to build the options.
    private static readonly MySqlServerVersion ServerVersion = new(new Version(8, 0, 46));

    /// <summary>
    /// The one provider configuration, shared by <see cref="CreateOptions"/> and by the DI
    /// registration of the context factory so neither can drift from the other.
    /// </summary>
    public static void Configure(DbContextOptionsBuilder builder) =>
        builder.UseMySql(EnvLoader.GetConnectionString(), ServerVersion);

    public static DbContextOptions<HabitDbContext> CreateOptions()
    {
        var builder = new DbContextOptionsBuilder<HabitDbContext>();
        Configure(builder);
        return builder.Options;
    }
}
