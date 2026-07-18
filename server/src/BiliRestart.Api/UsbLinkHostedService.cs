using System.Diagnostics;

namespace BiliRestart.Api;

// USB直连支持：轮询 adb 检测数据线连接的安卓设备，自动布置
// `adb reverse tcp:{port} tcp:{port}` 隧道——之后手机上访问 127.0.0.1:{port}
// 就等于直连本机服务端，与WiFi/热点/防火墙完全无关。
// 客户端设置里开"USB直连"后会把 127.0.0.1 纳入候选地址探测。
// 前置条件：本机有 adb(platform-tools)、手机开USB调试并授权过本机。
// adb不存在时本服务安静退化为无操作(只在启动时提示一次)，不影响其它功能。
public sealed class UsbLinkHostedService(
    IConfiguration configuration,
    ILogger<UsbLinkHostedService> logger) : BackgroundService
{
    private readonly HashSet<string> _armedSerials = [];
    private string? _adbPath;
    private bool _warnedNoAdb;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var section = configuration.GetSection("UsbLink");
        if (!(section.GetValue<bool?>("Enabled") ?? true))
        {
            logger.LogInformation("USB直连支持已在配置中禁用");
            return;
        }
        var port = section.GetValue<int?>("Port") ?? 5299;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                _adbPath ??= LocateAdb(section["AdbPath"]);
                if (_adbPath is null)
                {
                    if (!_warnedNoAdb)
                    {
                        _warnedNoAdb = true;
                        logger.LogInformation(
                            "未找到 adb，USB直连不可用(装 Android platform-tools 或在配置 UsbLink:AdbPath 指定路径后重启)");
                    }
                    continue;
                }

                foreach (var serial in await ListDevicesAsync(_adbPath, stoppingToken))
                {
                    // 每轮都重新布置：设备重插/adb重启后 reverse 会丢，重复执行是幂等的
                    var ok = await RunAdbAsync(_adbPath,
                        $"-s {serial} reverse tcp:{port} tcp:{port}", stoppingToken);
                    if (ok && _armedSerials.Add(serial))
                    {
                        logger.LogInformation("USB直连隧道已布置: 设备 {Serial} ← tcp:{Port}", serial, port);
                    }
                    else if (!ok && _armedSerials.Remove(serial))
                    {
                        logger.LogWarning("USB直连隧道布置失败: 设备 {Serial}", serial);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "USB直连轮询异常(忽略,下轮再试)");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static string? LocateAdb(string? configured)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        var exe = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        // 优先随包自带的(发布输出 tools/adb/)，开箱即用
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "adb", exe);
        if (File.Exists(bundled)) return bundled;
        // PATH 里找
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = Path.Combine(dir.Trim(), exe);
            if (File.Exists(p)) return p;
        }
        // Windows 默认 SDK 位置
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var common = Path.Combine(local, "Android", "Sdk", "platform-tools", exe);
        return File.Exists(common) ? common : null;
    }

    private static async Task<List<string>> ListDevicesAsync(string adb, CancellationToken ct)
    {
        var (ok, stdout) = await RunAdbCaptureAsync(adb, "devices", ct);
        var list = new List<string>();
        if (!ok) return list;
        foreach (var line in stdout.Split('\n').Skip(1))
        {
            var parts = line.Trim().Split('\t');
            if (parts.Length == 2 && parts[1] == "device") list.Add(parts[0]);
        }
        return list;
    }

    private static async Task<bool> RunAdbAsync(string adb, string args, CancellationToken ct)
        => (await RunAdbCaptureAsync(adb, args, ct)).Ok;

    private static async Task<(bool Ok, string Stdout)> RunAdbCaptureAsync(
        string adb, string args, CancellationToken ct)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = adb,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc is null) return (false, "");
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return (proc.ExitCode == 0, stdout);
        }
        catch
        {
            return (false, "");
        }
    }
}
