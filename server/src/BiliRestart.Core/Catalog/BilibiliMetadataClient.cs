using System.Text.Json;

namespace BiliRestart.Core.Catalog;

public sealed record FetchedVideoMetadata(
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    int Tid,
    string Tname,
    string UploaderName,
    long UploaderMid,
    long PublishedAtUnix,
    long ViewCount,
    long LikeCount,
    byte[]? CoverBytes,
    IReadOnlyList<(long Cid, int Page, string Part, int DurationSeconds)> Pages);

// 回填扫描用：只读调用B站公开的view/tags接口，拿归档时缺失的元数据。
// 这些是B站官方公开、不需要登录也能查的接口，用于"我们已经下载好的历史视频"
// 回填标题/简介/标签/发布日期等元数据，不涉及绕过任何access限制。
public sealed class BilibiliMetadataClient(HttpClient httpClient)
{
    public async Task<FetchedVideoMetadata?> FetchAsync(string bvid, CancellationToken ct = default)
    {
        var viewJson = await httpClient.GetStringAsync(
            $"https://api.bilibili.com/x/web-interface/view?bvid={bvid}", ct);
        using var viewDoc = JsonDocument.Parse(viewJson);
        var root = viewDoc.RootElement;
        if (root.GetProperty("code").GetInt32() != 0) return null;

        var data = root.GetProperty("data");
        var aid = data.GetProperty("aid").GetInt64();
        var owner = data.GetProperty("owner");
        var stat = data.GetProperty("stat");

        var pages = new List<(long, int, string, int)>();
        if (data.TryGetProperty("pages", out var pagesEl) && pagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in pagesEl.EnumerateArray())
            {
                pages.Add((
                    p.GetProperty("cid").GetInt64(),
                    p.GetProperty("page").GetInt32(),
                    p.TryGetProperty("part", out var partEl) ? partEl.GetString() ?? "" : "",
                    p.TryGetProperty("duration", out var durEl) ? durEl.GetInt32() : 0));
            }
        }
        else
        {
            // 没有 pages 字段的老接口/单P视频，退回顶层 cid/duration
            pages.Add((
                data.GetProperty("cid").GetInt64(),
                1,
                data.GetProperty("title").GetString() ?? "",
                data.TryGetProperty("duration", out var d) ? d.GetInt32() : 0));
        }

        var tags = new List<string>();
        try
        {
            var tagsJson = await httpClient.GetStringAsync(
                $"https://api.bilibili.com/x/tag/archive/tags?aid={aid}", ct);
            using var tagsDoc = JsonDocument.Parse(tagsJson);
            if (tagsDoc.RootElement.GetProperty("code").GetInt32() == 0 &&
                tagsDoc.RootElement.TryGetProperty("data", out var tagsData) &&
                tagsData.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tagsData.EnumerateArray())
                {
                    if (t.TryGetProperty("tag_name", out var name))
                    {
                        tags.Add(name.GetString() ?? "");
                    }
                }
            }
        }
        catch
        {
            // 标签拉不到不影响主流程，留空即可
        }

        byte[]? coverBytes = null;
        try
        {
            var picUrl = data.GetProperty("pic").GetString();
            if (!string.IsNullOrEmpty(picUrl))
            {
                coverBytes = await httpClient.GetByteArrayAsync(picUrl, ct);
            }
        }
        catch
        {
            // 封面拉不到不影响主流程
        }

        return new FetchedVideoMetadata(
            Title: data.GetProperty("title").GetString() ?? "",
            Description: data.TryGetProperty("desc", out var descEl) ? descEl.GetString() ?? "" : "",
            Tags: tags,
            // B站正在从老分区(tid/tname)切到新分区体系(tid_v2/tname_v2)，
            // 老视频可能只剩tid有值、tname已清空，按 老值优先、v2兜底 取。
            Tid: data.TryGetProperty("tid", out var tidEl) && tidEl.GetInt32() != 0
                ? tidEl.GetInt32()
                : data.TryGetProperty("tid_v2", out var tidV2El) ? tidV2El.GetInt32() : 0,
            Tname: data.TryGetProperty("tname", out var tnameEl) && !string.IsNullOrEmpty(tnameEl.GetString())
                ? tnameEl.GetString()!
                : data.TryGetProperty("tname_v2", out var tnameV2El) ? tnameV2El.GetString() ?? "" : "",
            UploaderName: owner.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
            UploaderMid: owner.TryGetProperty("mid", out var midEl) ? midEl.GetInt64() : 0,
            PublishedAtUnix: data.TryGetProperty("pubdate", out var pubEl) ? pubEl.GetInt64() : 0,
            ViewCount: stat.TryGetProperty("view", out var viewEl) ? viewEl.GetInt64() : 0,
            LikeCount: stat.TryGetProperty("like", out var likeEl) ? likeEl.GetInt64() : 0,
            CoverBytes: coverBytes,
            Pages: pages);
    }

    // 番剧分集：archive.db 不存每集的bvid，只有文件名里的epId，所以走 PGC 的
    // season接口(公开、不需要bvid)按ep_id查，season.episodes里找到对应那一集。
    public async Task<FetchedBangumiEpisode?> FetchBangumiEpisodeAsync(long episodeId, CancellationToken ct = default)
    {
        var json = await httpClient.GetStringAsync(
            $"https://api.bilibili.com/pgc/view/web/season?ep_id={episodeId}", ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("code").GetInt32() != 0) return null;

        var result = root.GetProperty("result");
        var seasonTitle = result.TryGetProperty("title", out var st) ? st.GetString() ?? "" : "";

        byte[]? seasonCover = null;
        try
        {
            if (result.TryGetProperty("cover", out var coverEl))
            {
                var coverUrl = coverEl.GetString();
                if (!string.IsNullOrEmpty(coverUrl))
                {
                    seasonCover = await httpClient.GetByteArrayAsync(coverUrl, ct);
                }
            }
        }
        catch { /* 封面拉不到不影响主流程 */ }

        foreach (var ep in result.GetProperty("episodes").EnumerateArray())
        {
            if (ep.GetProperty("id").GetInt64() != episodeId) continue;

            return new FetchedBangumiEpisode(
                SeasonTitle: seasonTitle,
                EpisodeTitle: ep.TryGetProperty("long_title", out var lt) && !string.IsNullOrEmpty(lt.GetString())
                    ? lt.GetString()!
                    : (ep.TryGetProperty("title", out var t) ? t.GetString() ?? "" : ""),
                Cid: ep.GetProperty("cid").GetInt64(),
                DurationSeconds: ep.TryGetProperty("duration", out var d) ? (int)(d.GetInt64() / 1000) : 0,
                PublishedAtUnix: ep.TryGetProperty("pub_time", out var pt) ? pt.GetInt64() : 0,
                CoverBytes: seasonCover);
        }
        return null;
    }
}

public sealed record FetchedBangumiEpisode(
    string SeasonTitle,
    string EpisodeTitle,
    long Cid,
    int DurationSeconds,
    long PublishedAtUnix,
    byte[]? CoverBytes);
