namespace BiliRestart.Core.Plugins;

// 视频获取插件契约。公开主代码只定义此接口 + 加载器(见 PluginLoader)；
// 真正的"下载指定 av/bv → 合并 → 追加写入 archive.db"实现放在 plugins/ 目录下的
// 补丁 DLL 里(闭源/私有仓)。没有补丁 DLL 时此能力不存在——主程序不含任何下载代码。
public interface IVideoAcquisitionPlugin
{
    string Name { get; }

    // 下载指定视频(常规/分P/番剧都支持)到归档目录，并把结果按标准格式追加写入
    // archive.db(status='completed' 等)，使服务端后续回填/服务时与其它视频完全同源。
    Task<VideoAcquisitionResult> AcquireAsync(
        VideoAcquisitionRequest request,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

// 目标视频号 + 落盘/入库位置。由主程序从当前配置提供，插件据此下载并写库。
public sealed record VideoAcquisitionRequest(
    string AvOrBvid,
    string ArchiveDbPath,
    string VideoOutputDir);

public sealed record VideoAcquisitionResult(
    bool Success,
    string Message,
    long AvNumber);
