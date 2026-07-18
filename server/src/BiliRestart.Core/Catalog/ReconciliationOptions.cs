namespace BiliRestart.Core.Catalog;

// 运维在管理面板里配置的路径（Phase 3 之前先用一个简单的配置类占位，
// Phase 3 的 Avalonia 面板会提供真正的路径选择UI来改这些值）。
public sealed class ReconciliationOptions
{
    public string ArchiveDbPath { get; set; } = "";
    public string DanmuCommentDbRootDir { get; set; } = "";
    public int MaxConcurrentFetches { get; set; } = 4;
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(30);
}
