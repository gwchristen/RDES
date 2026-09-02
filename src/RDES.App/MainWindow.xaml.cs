using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using RDES.App.ViewModels;

namespace RDES.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            ApplyAppIcon();

            Loaded += async (s, e) =>
            {
                await _viewModel.InitializeAsync();
            };

            // Global Keybindings
            KeyDown += MainWindow_KeyDown;
        }

        private void ApplyAppIcon()
        {
            try
            {
                string iconName = _viewModel.IsClientMode ? "RDESClient.ico" : "RDESServer.ico";
                var iconUri = new Uri($"pack://application:,,,/Assets/{iconName}", UriKind.RelativeOrAbsolute);
                Icon = BitmapFrame.Create(iconUri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set window icon: {ex.Message}");
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+S to save in entry tab
            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (_viewModel.SelectedTabIndex == 0)
                {
                    _viewModel.EntryVM.SaveCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // Ctrl+N to clear/new in entry tab
            else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (_viewModel.SelectedTabIndex == 0)
                {
                    _viewModel.EntryVM.ClearFormCommand.Execute(null);
                    e.Handled = true;
                }
            }
            // F5 to refresh all
            else if (e.Key == Key.F5)
            {
                _viewModel.RefreshAllCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}