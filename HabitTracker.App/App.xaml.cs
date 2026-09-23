using System.Windows;
using HabitTracker.App.ViewModels;
using HabitTracker.App.Views;
using HabitTracker.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HabitTracker.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // A factory rather than a singleton context: the repository hands every operation its own
        // DbContext, so two overlapping async chains cannot abort each other.
        services.AddDbContextFactory<HabitDbContext>(DbContextFactory.Configure);
        services.AddSingleton<IHabitRepository, HabitRepository>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();

        var database = _services.GetRequiredService<IDbContextFactory<HabitDbContext>>();

        try
        {
            // Brings the schema up to date on launch, so a fresh clone needs no manual ef command.
            using var db = database.CreateDbContext();
            db.Database.Migrate();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Habit Tracker could not reach MySQL, or its migrations failed to apply." +
                Environment.NewLine + Environment.NewLine +
                "Check that the MySQL service is running and that .env has valid credentials." +
                Environment.NewLine + Environment.NewLine + ex.Message,
                "Habit Tracker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
