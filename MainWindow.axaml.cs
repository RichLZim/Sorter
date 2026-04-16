using Avalonia.Controls;
using Sorter.ViewModels;

namespace Sorter;

public partial class MainWindow : Window
{
    /// <summary>
    /// The constructor initializes the Avalonia components and 
    /// sets up the DataContext to link the View (XAML) with the ViewModel (Logic).
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // This is the most important line for MVVM. 
        // It tells the XAML: "Whenever you see a {Binding}, look inside MainViewModel."
        DataContext = new MainViewModel();
    }
}