using Microsoft.EntityFrameworkCore.Design;

namespace HabitTracker.Core.Data;

/// <summary>Lets `dotnet ef` build a context for this class library without a startup host.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HabitDbContext>
{
    public HabitDbContext CreateDbContext(string[] args) => new(DbContextFactory.CreateOptions());
}
