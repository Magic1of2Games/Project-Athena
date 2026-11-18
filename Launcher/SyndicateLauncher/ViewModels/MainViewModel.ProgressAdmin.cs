using System.Windows;
using System.Windows.Input;
using SyndicateLauncher.Views;

namespace SyndicateLauncher.ViewModels;

/// <summary>Settings > Launcher: the progress admin panel (Views/ProgressAdminWindow.cs).</summary>
public sealed partial class MainViewModel
{
    private ICommand? _openProgressAdminCommand;
    private ProgressAdminWindow? _progressAdminWindow;

    public ICommand OpenProgressAdminCommand => _openProgressAdminCommand ??= new RelayCommand(OpenProgressAdmin);

    private void OpenProgressAdmin()
    {
        if (_progressAdminWindow is not null)
        {
            _progressAdminWindow.Activate();
            return;
        }
        if (SelectedProfile is null)
        {
            MessageBox.Show("Choose a launcher profile first.", "Progress admin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var window = new ProgressAdminWindow(SelectedProfile.Name, ActiveSeason?.SeasonNumber ?? 0, EnsureBackendForHomeAsync);
        if (Application.Current?.MainWindow is { IsVisible: true } owner) window.Owner = owner;
        window.Closed += (_, _) => _progressAdminWindow = null;
        _progressAdminWindow = window;
        window.Show();
    }
}
