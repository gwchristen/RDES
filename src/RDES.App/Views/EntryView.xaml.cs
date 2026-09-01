using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RDES.App.Models;
using RDES.App.ViewModels;

namespace RDES.App.Views
{
    public partial class EntryView : UserControl
    {
        public EntryView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                TxtSerialNumber.Focus();
                if (DataContext is EntryViewModel vm)
                {
                    vm.RequestFocusSerial += () =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            TxtSerialNumber.Focus();
                            TxtSerialNumber.SelectAll();
                        });
                    };
                }
            };
        }

        private void TxtSerialNumber_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Scanners often suffix Enter - advance focus to Module # field rather than auto-submitting
                e.Handled = true;
                TxtModuleNumber.Focus();
                TxtModuleNumber.SelectAll();
            }
        }

        private void TxtFormField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is EntryViewModel vm)
                {
                    vm.SaveCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void RecentEntries_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dg && dg.SelectedItem is DeviceRecord record)
            {
                if (DataContext is EntryViewModel vm)
                {
                    vm.LoadRecordForEdit(record);
                }
            }
        }
    }
}
