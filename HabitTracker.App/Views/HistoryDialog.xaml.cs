using System.Windows;
using HabitTracker.App.ViewModels;
using HabitTracker.Core.Data;

namespace HabitTracker.App.Views;

public partial class HistoryDialog : Window
{
    private readonly HistoryViewModel _viewModel;

    public HistoryDialog(ItemViewModel item, IHabitRepository repository)
    {
        InitializeComponent();
        _viewModel = new HistoryViewModel(item, repository);
        DataContext = _viewModel;
        DarkTitleBar.ApplyTo(this);
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }
}
