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
        Loaded += async (_, _) =>
        {
            // LoadAsync reports its own failures, so this catch has no test behind it. It is here
            // because this lambda is async void and the app registers no DispatcherUnhandledException
            // handler, so anything unforeseen escaping it would end the process rather than show a
            // message — the failure mode this dialog was reported for.
            try
            {
                await _viewModel.LoadAsync();
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage = $"Could not load the history: {ex.Message}";
            }
        };
    }
}
