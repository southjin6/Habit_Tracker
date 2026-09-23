using System.Windows;
using HabitTracker.App.ViewModels;
using HabitTracker.Core.Data;

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
}
