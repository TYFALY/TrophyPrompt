using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TrophyPrompt.ViewModels;
using TrophyPrompt.Views;

namespace TrophyPrompt;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // NOTE: do NOT assign DataContext here. MainWindow's constructor
            // creates the MainViewModel and wires HostWindow + handlers;
            // replacing it would orphan every command (HostWindow == null).
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}