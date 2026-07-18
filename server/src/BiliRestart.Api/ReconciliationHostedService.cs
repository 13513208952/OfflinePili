using BiliRestart.Core.Catalog;

namespace BiliRestart.Api;

// 每次服务端启动跑一次回填扫描，之后按配置的间隔定时重扫——archive.db/collect DB
// 可能是网络共享/挂载盘，不依赖 FileSystemWatcher，只靠轮询。
public sealed class ReconciliationHostedService(
    IServiceProvider services,
    ReconciliationOptions options,
    ILogger<ReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.SweepInterval);
        do
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
                var metaClient = scope.ServiceProvider.GetRequiredService<BilibiliMetadataClient>();
                var coverDir = scope.ServiceProvider.GetRequiredService<CoverCacheDir>();
                var svc = new ReconciliationService(db, metaClient, options, coverDir.Path);
                var run = await svc.RunSweepAsync(stoppingToken);
                logger.LogInformation(
                    "回填扫描完成: attempted={Attempted} succeeded={Succeeded} failed={Failed}",
                    run.ItemsAttempted, run.ItemsSucceeded, run.ItemsFailed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "回填扫描本轮异常，等下一轮再试");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
