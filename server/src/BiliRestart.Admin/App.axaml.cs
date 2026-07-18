using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BiliRestart.Admin.ViewModels;

namespace BiliRestart.Admin;

public partial class App : Application
{
    // 由 BiliRestart.Admin.Host 在启动Avalonia前注入：同进程API宿主的DI容器
    // 和定位到的配置文件路径。面板进程内直接调Core服务，不走网络。
    public static IServiceProvider? Services { get; set; }
    public static string? ConfigPath { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            if (Services is not null && ConfigPath is not null)
            {
                window.DataContext = new MainViewModel(Services, ConfigPath);
            }
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
