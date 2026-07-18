using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using BiliRestart.Core.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BiliRestart.Admin.ViewModels;

// 管理面板主ViewModel：纯本机桌面使用，进程内直接拿API宿主的DI容器调Core服务，
// 不走任何网络管理接口、不做认证——这是设计原则，不是偷懒。
public sealed class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly string _configPath;
    private readonly ReconciliationOptions _liveOptions;

    public MainViewModel(IServiceProvider services, string configPath)
    {
        _services = services;
        _configPath = configPath;
        _liveOptions = services.GetRequiredService<ReconciliationOptions>();

        _archiveDbPath = _liveOptions.ArchiveDbPath;
        _danmuCommentDbRootDir = _liveOptions.DanmuCommentDbRootDir;
        _maxConcurrentFetches = _liveOptions.MaxConcurrentFetches.ToString();
        _sweepIntervalMinutes = _liveOptions.SweepInterval.TotalMinutes.ToString("0.##");

        SaveConfigCommand = new AsyncRelayCommand(_ => SaveConfigAsync());
        SweepNowCommand = new AsyncRelayCommand(_ => SweepNowAsync());
        ReloadCommand = new AsyncRelayCommand(_ => ReloadAsync());
        RetryCommand = new AsyncRelayCommand(p => RetryAsync(p as CatalogEntryRowViewModel));
        ToggleHideCommand = new AsyncRelayCommand(p => ToggleHideAsync(p as CatalogEntryRowViewModel));
        SaveMetadataCommand = new AsyncRelayCommand(_ => SaveMetadataAsync());

        _ = ReloadAsync();
    }

    // ---- 状态条 ----
    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => Set(ref _statusMessage, value); }

    // ---- 路径配置页 ----
    private string _archiveDbPath;
    public string ArchiveDbPath { get => _archiveDbPath; set => Set(ref _archiveDbPath, value); }

    private string _danmuCommentDbRootDir;
    public string DanmuCommentDbRootDir { get => _danmuCommentDbRootDir; set => Set(ref _danmuCommentDbRootDir, value); }

    private string _maxConcurrentFetches;
    public string MaxConcurrentFetches { get => _maxConcurrentFetches; set => Set(ref _maxConcurrentFetches, value); }

    private string _sweepIntervalMinutes;
    public string SweepIntervalMinutes { get => _sweepIntervalMinutes; set => Set(ref _sweepIntervalMinutes, value); }

    public AsyncRelayCommand SaveConfigCommand { get; }

    private async Task SaveConfigAsync()
    {
        try
        {
            // 写回定位到的同一份 appsettings.json(读-改-写，保留其它配置节)。
            // 面板里填的是绝对路径就存绝对路径，语义最直白。
            JsonNode root;
            try
            {
                root = JsonNode.Parse(await File.ReadAllTextAsync(_configPath)) ?? new JsonObject();
            }
            catch (FileNotFoundException)
            {
                root = new JsonObject();
            }
            var section = root["Reconciliation"] as JsonObject ?? new JsonObject();
            section["ArchiveDbPath"] = ArchiveDbPath;
            section["DanmuCommentDbRootDir"] = DanmuCommentDbRootDir;
            if (int.TryParse(MaxConcurrentFetches, out var fetches) && fetches > 0)
                section["MaxConcurrentFetches"] = fetches;
            if (double.TryParse(SweepIntervalMinutes, out var minutes) && minutes > 0)
                section["SweepIntervalMinutes"] = minutes;
            root["Reconciliation"] = section;
            await File.WriteAllTextAsync(
                _configPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // 路径/并发数当场生效(下一次扫描就用新值)；扫描间隔的定时器
            // 在宿主启动时就建好了，改间隔要重启进程才生效。
            _liveOptions.ArchiveDbPath = ArchiveDbPath;
            _liveOptions.DanmuCommentDbRootDir = DanmuCommentDbRootDir;
            if (fetches > 0) _liveOptions.MaxConcurrentFetches = fetches;

            StatusMessage = $"配置已保存到 {_configPath}（扫描间隔改动需重启生效）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"配置保存失败: {ex.Message}";
        }
    }

    // ---- 目录状态页 ----
    public ObservableCollection<CatalogEntryRowViewModel> Entries { get; } = [];

    private bool _showHiddenOnly;
    public bool ShowHiddenOnly
    {
        get => _showHiddenOnly;
        set { if (Set(ref _showHiddenOnly, value)) _ = ReloadAsync(); }
    }

    private bool _showFailedOnly;
    public bool ShowFailedOnly
    {
        get => _showFailedOnly;
        set { if (Set(ref _showFailedOnly, value)) _ = ReloadAsync(); }
    }

    private CatalogEntryRowViewModel? _selectedEntry;
    public CatalogEntryRowViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (Set(ref _selectedEntry, value))
            {
                _ = LoadEditFieldsAsync(value);
            }
        }
    }

    public AsyncRelayCommand SweepNowCommand { get; }
    public AsyncRelayCommand ReloadCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public AsyncRelayCommand ToggleHideCommand { get; }

    private async Task ReloadAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

            var query = db.CatalogEntries.AsNoTracking();
            if (ShowHiddenOnly) query = query.Where(e => e.IsHidden);
            if (ShowFailedOnly) query = query.Where(e => e.MetadataStatus == MetadataStatus.Failed);
            var entries = await query
                .OrderBy(e => e.AvNumber).ThenBy(e => e.PartIndex)
                .ToListAsync();

            Entries.Clear();
            foreach (var e in entries) Entries.Add(new CatalogEntryRowViewModel(e));

            var runs = await db.ReconciliationRuns.AsNoTracking()
                .OrderByDescending(r => r.Id).Take(50).ToListAsync();
            Runs.Clear();
            foreach (var r in runs) Runs.Add(new ReconciliationRunRowViewModel(r));

            StatusMessage = $"目录共 {Entries.Count} 个可播放单元（筛选后）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载目录失败: {ex.Message}";
        }
    }

    private async Task SweepNowAsync()
    {
        StatusMessage = "回填扫描进行中…";
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var metaClient = scope.ServiceProvider.GetRequiredService<BilibiliMetadataClient>();
            var coverDir = scope.ServiceProvider.GetRequiredService<CoverCacheDir>();
            var svc = new ReconciliationService(db, metaClient, _liveOptions, coverDir.Path);
            var run = await svc.RunSweepAsync();
            StatusMessage = $"扫描完成: 尝试{run.ItemsAttempted} 成功{run.ItemsSucceeded} 失败{run.ItemsFailed}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败: {ex.Message}";
        }
        await ReloadAsync();
    }

    private async Task RetryAsync(CatalogEntryRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var entity = await db.CatalogEntries.FirstOrDefaultAsync(e => e.Id == row.Id);
            if (entity is null) return;
            entity.MetadataStatus = MetadataStatus.Pending;
            entity.FailureReason = "";
            await db.SaveChangesAsync();
            row.Status = nameof(MetadataStatus.Pending);
            row.FailureReason = "";
            StatusMessage = $"已把 av{row.AvNumber} P{row.PartIndex} 标记为待重试，点\"立即扫描\"或等下轮自动扫描";
        }
        catch (Exception ex)
        {
            StatusMessage = $"重试标记失败: {ex.Message}";
        }
    }

    private async Task ToggleHideAsync(CatalogEntryRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var entity = await db.CatalogEntries.FirstOrDefaultAsync(e => e.Id == row.Id);
            if (entity is null) return;
            entity.IsHidden = !entity.IsHidden;
            await db.SaveChangesAsync();
            row.IsHidden = entity.IsHidden;
            StatusMessage = entity.IsHidden
                ? $"av{row.AvNumber} P{row.PartIndex} 已永久隐藏(不进可服务目录)"
                : $"av{row.AvNumber} P{row.PartIndex} 已恢复展示";
        }
        catch (Exception ex)
        {
            StatusMessage = $"隐藏切换失败: {ex.Message}";
        }
    }

    // ---- 选中条目的手动元数据覆盖 ----
    private string _editTitle = "";
    public string EditTitle { get => _editTitle; set => Set(ref _editTitle, value); }

    private string _editPartTitle = "";
    public string EditPartTitle { get => _editPartTitle; set => Set(ref _editPartTitle, value); }

    private string _editDescription = "";
    public string EditDescription { get => _editDescription; set => Set(ref _editDescription, value); }

    private string _editTags = "";
    public string EditTags { get => _editTags; set => Set(ref _editTags, value); }

    private string _editUploader = "";
    public string EditUploader { get => _editUploader; set => Set(ref _editUploader, value); }

    public AsyncRelayCommand SaveMetadataCommand { get; }

    private async Task LoadEditFieldsAsync(CatalogEntryRowViewModel? row)
    {
        if (row is null)
        {
            EditTitle = EditPartTitle = EditDescription = EditTags = EditUploader = "";
            return;
        }
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var entity = await db.CatalogEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == row.Id);
            if (entity is null) return;
            EditTitle = entity.GroupTitle;
            EditPartTitle = entity.PartTitle;
            EditDescription = entity.Description;
            EditTags = entity.TagsCsv;
            EditUploader = entity.UploaderName;
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取条目失败: {ex.Message}";
        }
    }

    private async Task SaveMetadataAsync()
    {
        var row = SelectedEntry;
        if (row is null)
        {
            StatusMessage = "先在目录表里选中一个条目";
            return;
        }
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var entity = await db.CatalogEntries.FirstOrDefaultAsync(e => e.Id == row.Id);
            if (entity is null) return;
            entity.GroupTitle = EditTitle;
            entity.PartTitle = EditPartTitle;
            entity.Description = EditDescription;
            entity.TagsCsv = EditTags;
            entity.UploaderName = EditUploader;
            // 手动覆盖=人工确认这条能服务：Failed(比如已下架抓不到)也转成Fetched。
            // 已Fetched的条目不会被后续扫描重抓，所以手动改动不会被覆盖；
            // 但对它点"重试"会重新活取并覆盖手动内容——这是重试的语义，面板上有提示。
            entity.MetadataStatus = MetadataStatus.Fetched;
            entity.FailureReason = "";
            await db.SaveChangesAsync();
            row.Status = nameof(MetadataStatus.Fetched);
            row.FailureReason = "";
            StatusMessage = $"av{row.AvNumber} P{row.PartIndex} 元数据已手动覆盖并标记为可服务";
        }
        catch (Exception ex)
        {
            StatusMessage = $"元数据保存失败: {ex.Message}";
        }
    }

    // ---- 回填历史页 ----
    public ObservableCollection<ReconciliationRunRowViewModel> Runs { get; } = [];
}

public sealed class ReconciliationRunRowViewModel(ReconciliationRun r)
{
    public string StartedAt { get; } = DateTimeOffset
        .FromUnixTimeSeconds(r.StartedAtUnix).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Duration { get; } = $"{Math.Max(0, r.FinishedAtUnix - r.StartedAtUnix)}s";
    public int ItemsAttempted { get; } = r.ItemsAttempted;
    public int ItemsSucceeded { get; } = r.ItemsSucceeded;
    public int ItemsFailed { get; } = r.ItemsFailed;
}
