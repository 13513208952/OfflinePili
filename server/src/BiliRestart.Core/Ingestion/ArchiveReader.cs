using SharpCompress.Archives;

namespace BiliRestart.Core.Ingestion;

// 读取 BiliDanmuComment 采集器产出的 danmaku.7z / comments.7z（LZMA2，缺 7zr.exe 时退化为 .zip）。
// 只读，不修改归档本身。
public static class ArchiveReader
{
    // entryNamePredicate 为空时返回归档内第一个非目录条目。
    public static byte[] ReadFirstMatchingEntry(string archivePath, Func<string, bool>? entryNamePredicate = null)
    {
        using var archive = OpenArchive(archivePath);
        var entry = archive.Entries.FirstOrDefault(e =>
            !e.IsDirectory && (entryNamePredicate == null || entryNamePredicate(e.Key ?? string.Empty)));

        if (entry is null)
        {
            throw new FileNotFoundException($"归档 {archivePath} 内没有找到匹配的条目");
        }

        using var entryStream = entry.OpenEntryStream();
        using var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        return ms.ToArray();
    }

    private static IArchive OpenArchive(string archivePath) => ArchiveFactory.OpenArchive(archivePath);
}
