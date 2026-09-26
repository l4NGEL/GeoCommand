using System.Windows;
using GeoCommand.Desktop.ViewModels;
using Microsoft.Extensions.Options;

namespace GeoCommand.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel, IOptions<MapOptions> mapOptions)
    {
        InitializeComponent();
        DataContext = viewModel;
        Map.Configure(mapOptions.Value);
        Loaded += (_, _) => viewModel.Initialize();
    }
}
