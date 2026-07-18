using BiliRestart.Core.Catalog;
using BiliRestart.Core.Comments;
using BiliRestart.Core.Danmaku;
using BiliRestart.Core.Ingestion;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using PbDanmakuElem = BiliRestart.Core.Protos.Dm.DanmakuElem;
using PbDmSegMobileReply = BiliRestart.Core.Protos.Dm.DmSegMobileReply;

namespace BiliRestart.Api;

// 资源服务的完整组装：BiliRestart.Api 独立跑(headless)和 BiliRestart.Admin.Host
// (同进程 Kestrel+Avalonia管理面板)都调这里，保证两种宿主行为一致。
public static class ApiBootstrap
{
    /// 配置文件定位：优先当前工作目录(dotnet run 时=项目目录)，
    /// 找不到再用exe所在目录(发布部署场景)。appsettings 里的相对路径
    /// 一律相对于配置文件所在目录解析，管理面板改配置也写回这个文件。
    public static (string ConfigPath, string BaseDir) LocateConfig()
    {
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (File.Exists(cwdPath)) return (cwdPath, Directory.GetCurrentDirectory());
        return (Path.Combine(AppContext.BaseDirectory, "appsettings.json"), AppContext.BaseDirectory);
    }

    public static WebApplication Build(string[] args)
    {
        var (configPath, baseDir) = LocateConfig();

        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile(configPath, optional: true, reloadOnChange: false);

        var reconSection = builder.Configuration.GetSection("Reconciliation");
        var dataDir = Path.GetFullPath(Path.Combine(baseDir, reconSection["DataDir"] ?? "data"));
        Directory.CreateDirectory(dataDir);
        var coverCacheDir = Path.Combine(dataDir, "covers");

        var reconOptions = new ReconciliationOptions
        {
            ArchiveDbPath = Path.GetFullPath(Path.Combine(baseDir, reconSection["ArchiveDbPath"] ?? "")),
            DanmuCommentDbRootDir = Path.GetFullPath(Path.Combine(baseDir, reconSection["DanmuCommentDbRootDir"] ?? "")),
            MaxConcurrentFetches = reconSection.GetValue<int?>("MaxConcurrentFetches") ?? 4,
            SweepInterval = TimeSpan.FromMinutes(reconSection.GetValue<double?>("SweepIntervalMinutes") ?? 30)
        };

        builder.Services.AddSingleton(reconOptions);
        builder.Services.AddSingleton(new CoverCacheDir(coverCacheDir));
        builder.Services.AddDbContext<CatalogDbContext>(opt =>
            opt.UseSqlite($"Data Source={Path.Combine(dataDir, "catalog.db")}"));
        builder.Services.AddHttpClient<BilibiliMetadataClient>(c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 BiliRestartServer/0.1"));
        builder.Services.AddHostedService<ReconciliationHostedService>();
        builder.Services.AddHostedService<UsbLinkHostedService>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.EnsureCreated();
        }

        MapEndpoints(app);
        return app;
    }

    // 纯资源服务器：整套 API 只有 GET，没有任何写入路由——不是"收到写入请求再忽略"，
    // 是压根没有这条路由，这样更彻底地体现"服务端不接受除请求以外的任何数据流"。
    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/api/v1/videos", async (CatalogDbContext db, HttpRequest req, int page = 1, int pageSize = 30) =>
        {
            var fetched = await db.CatalogEntries
                .Where(e => e.MetadataStatus == MetadataStatus.Fetched && !e.IsHidden)
                .ToListAsync();

            var baseUrl = $"{req.Scheme}://{req.Host}";
            var items = fetched
                .GroupBy(e => e.AvNumber)
                .Select(g => g.OrderBy(e => e.PartIndex).First())
                .OrderByDescending(e => e.PublishedAtUnix)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new
                {
                    aid = e.AvNumber,
                    bvid = e.Bvid,
                    // 首P的cid直接带上，客户端点卡片就能直接进播放页，
                    // 不会退回去调B站的 ab2c 在线接口查cid。
                    cid = e.Cid,
                    title = e.GroupTitle,
                    pic = $"{baseUrl}/api/v1/covers/{e.Bvid}",
                    pubdate = e.PublishedAtUnix,
                    duration = e.DurationSeconds,
                    tags = e.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
                    tid = e.Tid,
                    tname = e.Tname,
                    view_count = e.ViewCountAtArchive,
                    like_count = e.LikeCountAtArchive,
                    owner = new { mid = e.UploaderMid, name = e.UploaderName }
                });

            return Results.Ok(items);
        });

        app.MapGet("/api/v1/videos/{avOrBv}", async (CatalogDbContext db, HttpRequest req, string avOrBv) =>
        {
            var entries = await FindEntriesAsync(db, avOrBv);
            if (entries.Count == 0) return Results.NotFound();

            var head = entries.OrderBy(e => e.PartIndex).First();
            var baseUrl = $"{req.Scheme}://{req.Host}";

            return Results.Ok(new
            {
                aid = head.AvNumber,
                bvid = head.Bvid,
                // 顶层cid/videos是PiliPlus的VideoDetailData必备字段：
                // cid=首P，videos=分P数，缺了客户端进详情页会再去查B站补
                cid = head.Cid,
                videos = entries.Count,
                title = head.GroupTitle,
                desc = head.Description,
                // PiliPlus详情页只渲染desc_v2富文本段落、不回退纯desc(真机实测简介空白)，
                // 这里把纯文本简介包成type=1(普通文本)的单段desc_v2
                desc_v2 = string.IsNullOrEmpty(head.Description)
                    ? null
                    : new[] { new { raw_text = head.Description, type = 1, biz_id = 0 } },
                pubdate = head.PublishedAtUnix,
                duration = entries.Sum(e => e.DurationSeconds),
                owner = new { mid = head.UploaderMid, name = head.UploaderName, face = "" },
                pic = $"{baseUrl}/api/v1/covers/{head.Bvid}",
                tags = head.TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries),
                tid = head.Tid,
                tname = head.Tname,
                view_count = head.ViewCountAtArchive,
                like_count = head.LikeCountAtArchive,
                // stat必须发全字段：PiliPlus详情页对share做了非空断言(stat!.share!)，
                // 缺字段会在Obx重建时崩掉整个页面(真机灰屏实测)。没归档的历史快照置0。
                stat = new
                {
                    view = head.ViewCountAtArchive,
                    like = head.LikeCountAtArchive,
                    danmaku = 0,
                    reply = 0,
                    favorite = 0,
                    coin = 0,
                    share = 0
                },
                pages = entries.OrderBy(e => e.PartIndex).Select(e => new
                {
                    cid = e.Cid,
                    page = e.PartIndex,
                    part = e.PartTitle,
                    duration = e.DurationSeconds
                })
            });
        });

        app.MapGet("/api/v1/videos/{avOrBv}/pages", async (CatalogDbContext db, string avOrBv) =>
        {
            var entries = await FindEntriesAsync(db, avOrBv);
            if (entries.Count == 0) return Results.NotFound();

            return Results.Ok(entries.OrderBy(e => e.PartIndex).Select(e => new
            {
                cid = e.Cid,
                page = e.PartIndex,
                part = e.PartTitle,
                duration = e.DurationSeconds
            }));
        });

        app.MapGet("/api/v1/videos/{avOrBv}/playurl", async (CatalogDbContext db, HttpRequest req, string avOrBv, long? cid) =>
        {
            var entries = await FindEntriesAsync(db, avOrBv);
            if (entries.Count == 0) return Results.NotFound();

            var target = cid is { } c
                ? entries.FirstOrDefault(e => e.Cid == c)
                : entries.OrderBy(e => e.PartIndex).First();
            if (target is null) return Results.NotFound();

            var fileInfo = new FileInfo(target.LocalFilePath);
            var baseUrl = $"{req.Scheme}://{req.Host}";

            return Results.Ok(new
            {
                quality = 120,
                format = "mp4",
                timelength = target.DurationSeconds * 1000,
                accept_format = "mp4",
                accept_description = new[] { "怀旧模式归档画质" },
                accept_quality = new[] { 120 },
                durl = new[]
                {
                    new
                    {
                        order = 1,
                        length = target.DurationSeconds * 1000,
                        size = fileInfo.Exists ? fileInfo.Length : 0,
                        url = $"{baseUrl}/api/v1/stream/{target.Bvid}/{target.Cid}"
                    }
                }
            });
        });

        app.MapGet("/api/v1/stream/{avOrBv}/{cid}", async (CatalogDbContext db, string avOrBv, long cid) =>
        {
            var entries = await FindEntriesAsync(db, avOrBv);
            var target = entries.FirstOrDefault(e => e.Cid == cid);
            if (target is null || !File.Exists(target.LocalFilePath)) return Results.NotFound();

            return Results.File(target.LocalFilePath, "video/mp4", enableRangeProcessing: true);
        });

        app.MapGet("/api/v1/danmaku/{cid}", async (CatalogDbContext db, ILogger<PbDmSegMobileReply> logger, long cid) =>
        {
            var reply = new PbDmSegMobileReply();

            var source = await db.DanmakuSources.FirstOrDefaultAsync(s => s.Cid == cid);
            if (source is not null)
            {
                try
                {
                    var xmlBytes = ArchiveReader.ReadFirstMatchingEntry(
                        source.ArchivePath, name => DanmuCommentArchiveLocator.DanmakuEntryMatchesCid(name, cid));

                    using var xmlStream = new MemoryStream(xmlBytes);
                    foreach (var e in DanmakuXmlParser.Parse(xmlStream))
                    {
                        reply.Elems.Add(new PbDanmakuElem
                        {
                            Id = e.Id,
                            Progress = e.ProgressMs,
                            Mode = e.Mode,
                            Fontsize = e.FontSize,
                            Color = e.Color,
                            MidHash = e.MidHash,
                            Content = e.Content,
                            Ctime = e.CTime,
                            Pool = e.Pool,
                            IdStr = e.IdStr,
                            Attr = e.Attr
                        });
                    }
                }
                catch (Exception ex)
                {
                    // 归档里找不到这个cid对应的条目、或归档本身读取失败：单条视频的弹幕缺失
                    // 不该拖垮整个请求，原样吐一份空弹幕列表，客户端就是"这个视频暂时没弹幕"。
                    logger.LogWarning(ex, "弹幕读取失败 cid={Cid} archivePath={Path}", cid, source.ArchivePath);
                }
            }

            // 不管有没有弹幕来源、有没有读取成功，统一走 Results.Bytes 走raw字节，
            // 跟客户端 OfflineDanmakuHttp 用 ResponseType.bytes 收的形状保持一致，
            // 不能有的分支走 Results.Ok(byte[]) 被序列化成JSON字符串。
            return Results.Bytes(reply.ToByteArray(), "application/octet-stream");
        });

        app.MapGet("/api/v1/replies/{avOrBv}", async (CatalogDbContext db, ILogger<CommentPage> logger, string avOrBv) =>
        {
            var entries = await FindEntriesAsync(db, avOrBv);
            if (entries.Count == 0) return Results.NotFound();
            var avNumber = entries[0].AvNumber;

            static object EmptyReplies() => new { cursor = new { is_end = true, all_count = 0 }, replies = Array.Empty<object>() };

            var source = await db.CommentSources.FirstOrDefaultAsync(s => s.AvNumber == avNumber);
            if (source is null) return Results.Ok(EmptyReplies());

            CommentPage page;
            try
            {
                var jsonBytes = ArchiveReader.ReadFirstMatchingEntry(
                    source.ArchivePath, name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                page = System.Text.Json.JsonSerializer.Deserialize<CommentPage>(jsonBytes)!;
            }
            catch (Exception ex)
            {
                // 评论归档读取失败(条目缺失/文件损坏)不该让整个请求500，视为"暂时没评论"。
                logger.LogWarning(ex, "评论读取失败 av={Av} archivePath={Path}", avNumber, source.ArchivePath);
                return Results.Ok(EmptyReplies());
            }

            // PiliPlus 的 ReplyData/ReplyItemModel/ReplyMember 期望的JSON形状和
            // BiliDanmuComment 原始归档形状不完全一样（顶层是 replies 不是 aid+total，
            // 楼中楼字段叫 replies 不是 sub_replies，member.mid 要求是字符串不是数字），
            // 这里按 PiliPlus 实际 fromJson 代码原样重整形，而不是猜的。
            object ReshapeReply(CommentReply r) => new
            {
                rpid = r.Rpid,
                oid = r.Oid,
                mid = r.Mid,
                root = r.Root,
                parent = r.Parent,
                rcount = r.Rcount,
                like = r.Like,
                ctime = r.Ctime,
                content = new { message = r.Content.Message },
                member = new
                {
                    mid = r.Member.Mid.ToString(),
                    uname = r.Member.Uname,
                    sex = r.Member.Sex,
                    level_info = new { current_level = r.Member.LevelInfo.CurrentLevel },
                    avatar = r.Member.Face
                },
                reply_control = new { location = r.ReplyControl.Location },
                replies = r.SubReplies.Select(ReshapeReply).ToArray()
            };

            return Results.Ok(new
            {
                cursor = new { is_end = true, all_count = page.Total },
                replies = page.Replies.Select(ReshapeReply).ToArray()
            });
        });

        app.MapGet("/api/v1/covers/{avOrBv}", async (CatalogDbContext db, string avOrBv) =>
        {
            var entries = await FindEntriesAsync(db, avOrBv);
            var withCover = entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.CoverImagePath) && File.Exists(e.CoverImagePath));
            if (withCover is null) return Results.NotFound();
            return Results.File(withCover.CoverImagePath, "image/jpeg");
        });

        app.MapGet("/api/v1/config", () => Results.Ok(new { aiSubtitles = false, vipGate = false, chargeGate = false }));
    }

    private static async Task<List<CatalogEntry>> FindEntriesAsync(CatalogDbContext db, string avOrBv)
    {
        var byBvid = await db.CatalogEntries
            .Where(e => e.Bvid == avOrBv && e.MetadataStatus == MetadataStatus.Fetched && !e.IsHidden)
            .ToListAsync();
        if (byBvid.Count > 0) return byBvid;

        if (long.TryParse(avOrBv, out var aid))
        {
            return await db.CatalogEntries
                .Where(e => e.AvNumber == aid && e.MetadataStatus == MetadataStatus.Fetched && !e.IsHidden)
                .ToListAsync();
        }
        return [];
    }
}
