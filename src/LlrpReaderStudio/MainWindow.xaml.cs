using System.ComponentModel;
using System.Windows;

namespace LlrpReaderStudio;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel = new();
    private bool disposalComplete;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (disposalComplete)
        {
            return;
        }

        args.Cancel = true;
        try
        {
            await viewModel.DisposeAsync();
        }
        catch
        {
            // The window must remain closable if a reader has already lost its transport.
        }
        finally
        {
            disposalComplete = true;
            Close();
        }
    }
}
