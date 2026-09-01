using System.Windows.Controls;
using System.Windows.Input;
using RDES.App.ViewModels;

namespace RDES.App.Views
{
    public partial class RecordsView : UserControl
    {
        public RecordsView()
        {
            InitializeComponent();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (DataContext is RecordsViewModel vm)
                {
                    vm.SearchCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is RecordsViewModel vm)
            {
                vm.SearchCommand.Execute(null);
            }
        }

        private void DgRecords_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RecordsViewModel vm && vm.SelectedRecord != null)
            {
                vm.EditSelectedCommand.Execute(null);
            }
        }
    }
}
