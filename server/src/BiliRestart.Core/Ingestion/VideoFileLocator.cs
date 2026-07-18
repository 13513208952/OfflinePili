using System.Text.RegularExpressions;

namespace BiliRestart.Core.Ingestion;

// 每个可播放单元(单P/分P/番剧分集)解析出的实际文件信息。
public sealed record ResolvedVideoFile(string FilePath, int PartIndex, int? EpisodeId, string PartTitle);

// 按 docs/归档格式规范.md 约定的落盘规则解析 archive.db 的 file_path 列：
// parts<=1 时 file_path 本身就是可播放的 mp4 文件（普通单P，或番剧某一集恰好也单独入队）；
// parts>1 时 file_path 是目录——普通多P目录下是 av{N}_p{idx}.mp4，番剧季目录下是
// ep{epId:D4}_{title}.mp4。archive.db 本身不存per集epId，只能扫目录按文件名规则区分。
public static class VideoFileLocator
{
    public static IReadOnlyList<ResolvedVideoFile> Resolve(ArchiveVideoRow row)
    {
        if (row.Parts <= 1)
        {
            return File.Exists(row.FilePath)
                ? [new ResolvedVideoFile(row.FilePath, 1, null, row.Title)]
                : [];
        }

        if (!Directory.Exists(row.FilePath)) return [];

        var bangumiFiles = Directory.GetFiles(row.FilePath, "ep*.mp4")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (bangumiFiles.Count > 0)
        {
            return bangumiFiles
                .Select((f, i) => new ResolvedVideoFile(
                    f, i + 1, ExtractEpisodeId(Path.GetFileName(f)), Path.GetFileNameWithoutExtension(f)))
                .ToList();
        }

        var partFiles = Directory.GetFiles(row.FilePath, $"av{row.AvNumber}_p*.mp4")
            .OrderBy(ExtractPartIndex)
            .ToList();
        return partFiles
            .Select((f, i) => new ResolvedVideoFile(f, i + 1, null, $"{row.Title} P{i + 1}"))
            .ToList();
    }

    private static int? ExtractEpisodeId(string fileName)
    {
        var match = Regex.Match(fileName, @"^ep(\d+)_");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static int ExtractPartIndex(string filePath)
    {
        var match = Regex.Match(Path.GetFileName(filePath), @"_p(\d+)\.mp4$");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
}
