using Microsoft.Data.Sqlite;

namespace BiliRestart.Core.Ingestion;

// 只读打开视频归档的 archive.db，绝不写入——写入方是使用者自己的归档工具，
// 可能正在另一台机器/网络共享上并发运行。
public sealed class ArchiveDbReader(string archiveDbPath)
{
    public IReadOnlyList<ArchiveVideoRow> ReadCompletedVideos()
    {
        var result = new List<ArchiveVideoRow>();
        using var conn = OpenReadOnly(archiveDbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT av_number, bvid, status, title, quality, parts, file_path, cover, parsed
            FROM videos
            WHERE status = 'completed'
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ArchiveVideoRow(
                AvNumber: reader.GetInt64(0),
                Bvid: reader.GetString(1),
                Status: reader.GetString(2),
                Title: reader.GetString(3),
                Quality: reader.GetString(4),
                Parts: reader.GetInt32(5),
                FilePath: reader.GetString(6),
                Cover: reader.IsDBNull(7) ? null : (byte[])reader.GetValue(7),
                Parsed: reader.GetInt32(8) != 0));
        }
        return result;
    }

    internal static SqliteConnection OpenReadOnly(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        return conn;
    }
}
