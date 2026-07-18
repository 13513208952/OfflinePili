namespace BiliRestart.Core.Ingestion;

// 对应视频归档 archive.db 的 videos 表（格式见 docs/归档格式规范.md），字段名/含义与之一致。
public sealed record ArchiveVideoRow(
    long AvNumber,
    string Bvid,
    string Status,
    string Title,
    string Quality,
    int Parts,
    string FilePath,
    byte[]? Cover,
    bool Parsed);

// 对应 BiliDanmuComment 采集器 collect_{mode}.db 的 tasks 表（格式见 docs/归档格式规范.md）。
public sealed record CollectTaskRow(
    long AvNumber,
    string InputToken,
    string VideoType, // "normal" | "bangumi"
    string Title,
    string Status,
    int DanmakuCount,
    int CommentCount,
    string OutputDir);
