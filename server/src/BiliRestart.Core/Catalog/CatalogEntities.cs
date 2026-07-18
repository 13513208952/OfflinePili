namespace BiliRestart.Core.Catalog;

public enum MetadataStatus
{
    Pending,
    Fetched,
    Failed
}

// 一行 = 一个"可播放单元"(单P视频本身/某一分P/番剧某一集)，不是一行=一个av，
// 这样分P和番剧才能各自有自己的cid/时长/文件路径。
public sealed class CatalogEntry
{
    public int Id { get; set; }
    public long AvNumber { get; set; }
    public string Bvid { get; set; } = "";
    public int PartIndex { get; set; }
    public int? EpisodeId { get; set; }
    public long? Cid { get; set; }
    public string LocalFilePath { get; set; } = "";
    public string GroupTitle { get; set; } = ""; // av级别标题（season标题/视频标题）
    public string PartTitle { get; set; } = "";
    public string Description { get; set; } = "";
    public string TagsCsv { get; set; } = "";
    public int Tid { get; set; }          // B站分区id，客户端推荐画像的特征之一
    public string Tname { get; set; } = ""; // 分区名（如"音乐"/"游戏"/"番剧"）
    public string UploaderName { get; set; } = "";
    public long UploaderMid { get; set; }
    public long PublishedAtUnix { get; set; }
    public int DurationSeconds { get; set; }
    public long ViewCountAtArchive { get; set; }
    public long LikeCountAtArchive { get; set; } // 客户端贝叶斯质量先验(IMDB公式)需要赞/播两个量
    public string CoverImagePath { get; set; } = "";
    public MetadataStatus MetadataStatus { get; set; } = MetadataStatus.Pending;
    public long LastFetchAttemptAtUnix { get; set; }
    public string FailureReason { get; set; } = "";
    // 管理面板"永久隐藏"：本地文件在、元数据也可能抓到了，但运维不想让它出现在
    // 可服务目录里。隐藏的条目不进任何API结果、回填扫描也不再碰它。
    public bool IsHidden { get; set; }
}

public sealed class DanmakuSource
{
    public int Id { get; set; }
    public long AvNumber { get; set; }
    public long Cid { get; set; }
    public string ArchivePath { get; set; } = "";
}

public sealed class CommentSource
{
    public int Id { get; set; }
    public long AvNumber { get; set; }
    public string ArchivePath { get; set; } = "";
}

public sealed class ReconciliationRun
{
    public int Id { get; set; }
    public long StartedAtUnix { get; set; }
    public long FinishedAtUnix { get; set; }
    public int ItemsAttempted { get; set; }
    public int ItemsSucceeded { get; set; }
    public int ItemsFailed { get; set; }
}
