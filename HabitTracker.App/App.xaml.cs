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

        var problem = DatabaseStartup.PrepareDatabase(_services);
        if (problem is not null)
        {
            MessageBox.Show(problem, "Habit Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
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
