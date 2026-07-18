namespace BiliRestart.Core.Ingestion;

// 只读打开 BiliDanmuComment 采集器的 collect_{mode}.db（history/current/comment
// 三种模式各一个独立库文件，互斥产出）。默认优先用 current，只有某个 av 没有
// current 数据时才回退用 history —— 这是 04号计划文档里定下的策略。
public sealed class DanmuCommentDbReader(string collectDbRootDir)
{
    private const string HistoryDbFileName = "collect_history.db";
    private const string CurrentDbFileName = "collect_current.db";
    private const string CommentDbFileName = "collect_comment.db";

    public IReadOnlyDictionary<long, CollectTaskRow> ReadDanmakuTasksPreferCurrent()
    {
        var history = ReadTasksFrom(Path.Combine(collectDbRootDir, HistoryDbFileName));
        var current = ReadTasksFrom(Path.Combine(collectDbRootDir, CurrentDbFileName));

        var merged = new Dictionary<long, CollectTaskRow>(history);
        foreach (var (av, row) in current)
        {
            merged[av] = row; // current 优先，覆盖 history 里的同 av 记录
        }
        return merged;
    }

    public IReadOnlyDictionary<long, CollectTaskRow> ReadCommentTasks() =>
        ReadTasksFrom(Path.Combine(collectDbRootDir, CommentDbFileName));

    private static Dictionary<long, CollectTaskRow> ReadTasksFrom(string dbPath)
    {
        var result = new Dictionary<long, CollectTaskRow>();
        if (!File.Exists(dbPath)) return result; // 该模式尚无产出，属正常情况

        using var conn = ArchiveDbReader.OpenReadOnly(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT av_number, input_token, video_type, title, status, danmaku_count, comment_count, output_dir
            FROM tasks
            WHERE status = 'completed'
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new CollectTaskRow(
                AvNumber: reader.GetInt64(0),
                InputToken: reader.GetString(1),
                VideoType: reader.GetString(2),
                Title: reader.GetString(3),
                Status: reader.GetString(4),
                DanmakuCount: reader.GetInt32(5),
                CommentCount: reader.GetInt32(6),
                OutputDir: reader.GetString(7));
            result[row.AvNumber] = row;
        }
        return result;
    }
}
