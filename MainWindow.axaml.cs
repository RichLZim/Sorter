using Avalonia.Controls;
using Sorter.ViewModels;

namespace Sorter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var vm = new MainViewModel();
        DataContext = vm;

        // Give the ViewModel access to the folder picker dialog.
        // We do this here (code-behind) because StorageProvider is a View concern.
        Loaded += (_, _) => vm.StorageProvider = StorageProvider;
    }
}
