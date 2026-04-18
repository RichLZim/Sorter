using Avalonia;
using Avalonia.Controls;
using Sorter.ViewModels;

namespace Sorter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Pass the StorageProvider to the ViewModel once the DataContext is injected
        this.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(DataContext) && DataContext is MainViewModel vm)
            {
                vm.StorageProvider = StorageProvider;
            }
        };
    }
}