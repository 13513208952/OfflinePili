using BiliRestart.Core.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace BiliRestart.Core.Catalog;

// 每次服务端/管理面板启动都跑一遍的目录回填扫描：
// 发现本地新文件 -> 建Pending目录项 -> 对Pending/Failed的逐条尝试活取B站元数据
// -> 成功转Fetched、失败留在原地下次再试，单条失败不影响其它视频。
public sealed class ReconciliationService(
    CatalogDbContext db,
    BilibiliMetadataClient metadataClient,
    ReconciliationOptions options,
    string coverCacheDir)
{
    public async Task<ReconciliationRun> RunSweepAsync(CancellationToken ct = default)
    {
        var run = new ReconciliationRun { StartedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

        DiscoverLocalCatalogCandidates();

        var candidates = await db.CatalogEntries
            .Where(e => !e.IsHidden &&
                (e.MetadataStatus == MetadataStatus.Pending || e.MetadataStatus == MetadataStatus.Failed))
            .ToListAsync(ct);

        run.ItemsAttempted = candidates.Count;
        var semaphore = new SemaphoreSlim(options.MaxConcurrentFetches);
        var tasks = candidates.Select(async entry =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var ok = await TryFetchMetadataAsync(entry, ct);
                if (ok) Interlocked.Increment(ref succeeded); else Interlocked.Increment(ref failed);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
        await db.SaveChangesAsync(ct); // 必须先落盘，下面按cid查询才能查到刚拿到的值

        // 放在拉取元数据之后：这样cid刚拿到手的这一轮就能同步登记弹幕/评论来源，
        // 不用等下一次sweep才补上（cid在拉取之前还是null，放前面discover会扑空）。
        DiscoverDanmakuCommentSources();

        run.ItemsSucceeded = succeeded;
        run.ItemsFailed = failed;
        run.FinishedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        db.ReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    private int succeeded;
    private int failed;

    private void DiscoverLocalCatalogCandidates()
    {
        var reader = new ArchiveDbReader(options.ArchiveDbPath);
        foreach (var row in reader.ReadCompletedVideos())
        {
            foreach (var file in VideoFileLocator.Resolve(row))
            {
                var existing = db.CatalogEntries
                    .FirstOrDefault(e => e.AvNumber == row.AvNumber && e.PartIndex == file.PartIndex);
                if (existing != null)
                {
                    existing.LocalFilePath = file.FilePath; // 路径可能变了(比如换了盘)，跟着更新
                    continue;
                }

                db.CatalogEntries.Add(new CatalogEntry
                {
                    AvNumber = row.AvNumber,
                    Bvid = row.Bvid,
                    PartIndex = file.PartIndex,
                    EpisodeId = file.EpisodeId,
                    LocalFilePath = file.FilePath,
                    GroupTitle = row.Title,
                    PartTitle = file.PartTitle,
                    MetadataStatus = MetadataStatus.Pending
                });
            }
        }
        db.SaveChanges();
    }

    private void DiscoverDanmakuCommentSources()
    {
        if (string.IsNullOrEmpty(options.DanmuCommentDbRootDir)) return;
        var reader = new DanmuCommentDbReader(options.DanmuCommentDbRootDir);

        foreach (var (av, row) in reader.ReadDanmakuTasksPreferCurrent())
        {
            var archivePath = DanmuCommentArchiveLocator.FindDanmakuArchive(row);
            if (archivePath == null) continue;

            // 一个danmaku.7z里可能装着这个av所有分P/分集的弹幕文件，cid在读取时按条目名匹配，
            // 这里只登记"这个av的弹幕在这个归档里"，具体哪个cid对应哪个条目由读取时的
            // DanmuCommentArchiveLocator.DanmakuEntryMatchesCid 处理，不需要逐cid建行。
            var catalogCids = db.CatalogEntries.Where(e => e.AvNumber == av && e.Cid != null)
                .Select(e => e.Cid!.Value).ToList();
            foreach (var cid in catalogCids)
            {
                if (db.DanmakuSources.Any(s => s.AvNumber == av && s.Cid == cid)) continue;
                db.DanmakuSources.Add(new DanmakuSource { AvNumber = av, Cid = cid, ArchivePath = archivePath });
            }
        }

        foreach (var (av, row) in reader.ReadCommentTasks())
        {
            var archivePath = DanmuCommentArchiveLocator.FindCommentsArchive(row);
            if (archivePath == null) continue;
            if (db.CommentSources.Any(s => s.AvNumber == av)) continue;
            db.CommentSources.Add(new CommentSource { AvNumber = av, ArchivePath = archivePath });
        }
        db.SaveChanges();
    }

    private async Task<bool> TryFetchMetadataAsync(CatalogEntry entry, CancellationToken ct)
    {
        entry.LastFetchAttemptAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            if (entry.EpisodeId is { } epId)
            {
                var ep = await metadataClient.FetchBangumiEpisodeAsync(epId, ct);
                if (ep == null)
                {
                    entry.MetadataStatus = MetadataStatus.Failed;
                    entry.FailureReason = "番剧分集在B站已查不到(可能已下架)";
                    return false;
                }
                entry.PartTitle = ep.EpisodeTitle;
                entry.GroupTitle = ep.SeasonTitle;
                // PGC season接口不带普通视频那种分区字段，番剧统一归到B站的"番剧"分区(tid=13)
                entry.Tid = 13;
                entry.Tname = "番剧";
                entry.Cid = ep.Cid;
                entry.DurationSeconds = ep.DurationSeconds;
                entry.PublishedAtUnix = ep.PublishedAtUnix;
                if (ep.CoverBytes != null)
                {
                    entry.CoverImagePath = SaveCover(entry.Bvid + "_ep" + epId, ep.CoverBytes);
                }
            }
            else
            {
                var meta = await metadataClient.FetchAsync(entry.Bvid, ct);
                if (meta == null)
                {
                    entry.MetadataStatus = MetadataStatus.Failed;
                    entry.FailureReason = "视频在B站已查不到(可能已下架)";
                    return false;
                }
                var page = meta.Pages.FirstOrDefault(p => p.Page == entry.PartIndex);
                if (page.Cid == 0 && meta.Pages.Count > 0) page = meta.Pages[0];
                entry.GroupTitle = meta.Title;
                entry.PartTitle = page.Part is { Length: > 0 } ? page.Part : meta.Title;
                entry.Description = meta.Description;
                entry.TagsCsv = string.Join(',', meta.Tags);
                entry.Tid = meta.Tid;
                entry.Tname = meta.Tname;
                entry.UploaderName = meta.UploaderName;
                entry.UploaderMid = meta.UploaderMid;
                entry.PublishedAtUnix = meta.PublishedAtUnix;
                entry.ViewCountAtArchive = meta.ViewCount;
                entry.LikeCountAtArchive = meta.LikeCount;
                entry.Cid = page.Cid;
                entry.DurationSeconds = page.DurationSeconds > 0 ? page.DurationSeconds : (int)meta.Pages.Sum(p => (long)p.DurationSeconds);
                if (meta.CoverBytes != null)
                {
                    entry.CoverImagePath = SaveCover(entry.Bvid, meta.CoverBytes);
                }
            }

            entry.MetadataStatus = MetadataStatus.Fetched;
            entry.FailureReason = "";
            return true;
        }
        catch (Exception ex)
        {
            entry.MetadataStatus = MetadataStatus.Failed;
            entry.FailureReason = ex.Message;
            return false;
        }
    }

    private string SaveCover(string key, byte[] bytes)
    {
        Directory.CreateDirectory(coverCacheDir);
        var path = Path.Combine(coverCacheDir, $"{key}.jpg");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
