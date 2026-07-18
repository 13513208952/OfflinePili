namespace BiliRestart.Core.Ingestion;

// CollectTaskRow.OutputDir 就是 BiliDanmuComment 采集器记录的该视频输出根目录，
// 不用再按标题脱敏规则去猜路径拼接——直接信任它自己写的 output_dir 最可靠。
public static class DanmuCommentArchiveLocator
{
    public static string? FindDanmakuArchive(CollectTaskRow row)
    {
        var path = Path.Combine(row.OutputDir, "danmaku.7z");
        if (File.Exists(path)) return path;
        var zipFallback = Path.Combine(row.OutputDir, "danmaku.zip");
        return File.Exists(zipFallback) ? zipFallback : null;
    }

    public static string? FindCommentsArchive(CollectTaskRow row)
    {
        var path = Path.Combine(row.OutputDir, "comments.7z");
        if (File.Exists(path)) return path;
        var zipFallback = Path.Combine(row.OutputDir, "comments.zip");
        return File.Exists(zipFallback) ? zipFallback : null;
    }

    // 归档内条目名按 cid 匹配：p{page}_cid{cid}.xml（普通）或 ep{epId:D4}_cid{cid}.xml（番剧）
    public static bool DanmakuEntryMatchesCid(string entryName, long cid) =>
        entryName.Contains($"_cid{cid}.xml", StringComparison.OrdinalIgnoreCase);

    // 评论条目名：av{N}.json（普通）或 ep{epId:D4}_aid{aid}.json（番剧）
    public static bool CommentEntryMatchesAid(string entryName, long aid) =>
        entryName.EndsWith($"av{aid}.json", StringComparison.OrdinalIgnoreCase) ||
        entryName.Contains($"_aid{aid}.json", StringComparison.OrdinalIgnoreCase);
}
