using System.IO;
using System.Windows;
using HabitTracker.App.ViewModels;
using HabitTracker.Core.Data;
using HabitTracker.Core.Domain;

namespace HabitTracker.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IHabitRepository _repository;

    public MainWindow(MainViewModel viewModel, IHabitRepository repository)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _repository = repository;
        DataContext = viewModel;
        DarkTitleBar.ApplyTo(this);

        viewModel.HistoryRequested += OnHistoryRequested;
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }

    private async void OnHistoryRequested(ItemViewModel item)
    {
        var dialog = new HistoryDialog(item, _repository) { Owner = this };
        dialog.ShowDialog();

        // Backdating changes history, so the streaks in the main list have to be recomputed.
        await _viewModel.RefreshAsync();
    }

    /// <summary>
    /// The file picker and the replace confirmation live here rather than in the view model: a modal
    /// blocks the thread that shows it, so keeping them in the view is what lets the backup methods
    /// be driven from a test. Both cancel silently — closing a dialog is not an error worth a
    /// banner, and the list on screen is still what the database holds.
    /// </summary>
    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export saved data",
            Filter = "JSON backup (*.json)|*.json",
            DefaultExt = ".json",
            FileName = $"habit-tracker-backup-{Utc8Clock.Today:yyyy-MM-dd}.json",
        };

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        await _viewModel.ExportAsync(picker.FileName);
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a JSON backup",
            Filter = "JSON backup (*.json)|*.json",
            DefaultExt = ".json",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        var plan = await _viewModel.PreviewImportAsync(picker.FileName);
        if (plan is null)
        {
            return;
        }

        // No owner, like the delete prompt: closing the main window destroys an owned message box,
        // and a native box torn down that way comes back as IDOK — which MessageBoxResult spells
        // Yes. An unanswered replace would then have run.
        var confirmed = MessageBox.Show(
            $"Replace what is saved now — {plan.CurrentItems} item{(plan.CurrentItems == 1 ? "" : "s")}, " +
            $"{plan.CurrentCompletions} completed day{(plan.CurrentCompletions == 1 ? "" : "s")} — " +
            $"with {plan.Backup.Items.Count} item{(plan.Backup.Items.Count == 1 ? "" : "s")} " +
            $"from \"{Path.GetFileName(picker.FileName)}\"?"
            + Environment.NewLine + Environment.NewLine +
            "Everything currently in MySQL is deleted first, and this cannot be undone. " +
            "Export the current data first if you are not sure.",
            "Import backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        await _viewModel.CommitImportAsync(plan);
    }
}
