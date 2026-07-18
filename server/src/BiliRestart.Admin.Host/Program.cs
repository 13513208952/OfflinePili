using Avalonia;
using BiliRestart.Admin;
using BiliRestart.Api;
using Microsoft.Extensions.Hosting;

namespace BiliRestart.Admin.Host;

// 组合入口：同一个进程里跑 Kestrel 资源服务 + Avalonia 管理面板。
//   dotnet run                → API + 管理面板窗口（服务器本机桌面用）
//   dotnet run -- --headless  → 纯API+后台回填，无GUI（无桌面会话的Linux盒子用，
//                               等价于直接跑 BiliRestart.Api）
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var headless = args.Contains("--headless");
        var app = ApiBootstrap.Build(args);

        if (headless)
        {
            app.Run();
            return 0;
        }

        // API 在后台跑，Avalonia 占据主线程(桌面UI框架的要求)；
        // 关掉面板窗口 = 停止整个服务进程，这就是"本机桌面会话"模式的语义。
        app.Start();
        App.Services = app.Services;
        App.ConfigPath = ApiBootstrap.LocateConfig().ConfigPath;
        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        app.StopAsync().GetAwaiter().GetResult();
        return exitCode;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
