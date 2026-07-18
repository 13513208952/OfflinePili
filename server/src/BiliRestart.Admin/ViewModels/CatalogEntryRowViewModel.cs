using BiliRestart.Core.Catalog;

namespace BiliRestart.Admin.ViewModels;

// 目录状态表里的一行。直接持有实体快照的展示字段，操作(重试/隐藏/改元数据)
// 由 MainViewModel 按 Id 回数据库执行，避免长期持有跨线程的EF实体。
public sealed class CatalogEntryRowViewModel(CatalogEntry e) : ViewModelBase
{
    public int Id { get; } = e.Id;
    public long AvNumber { get; } = e.AvNumber;
    public string Bvid { get; } = e.Bvid;
    public int PartIndex { get; } = e.PartIndex;
    public string DisplayTitle { get; } =
        e.PartTitle is { Length: > 0 } && e.PartTitle != e.GroupTitle
            ? $"{e.GroupTitle} / {e.PartTitle}"
            : e.GroupTitle;
    public string Uploader { get; } = e.UploaderName;
    public string Zone { get; } = e.Tname;
    public string LocalFilePath { get; } = e.LocalFilePath;

    private string _status = e.MetadataStatus.ToString();
    public string Status { get => _status; set => Set(ref _status, value); }

    private string _failureReason = e.FailureReason;
    public string FailureReason { get => _failureReason; set => Set(ref _failureReason, value); }

    private bool _isHidden = e.IsHidden;
    public bool IsHidden { get => _isHidden; set { if (Set(ref _isHidden, value)) Raise(nameof(HideButtonText)); } }

    public string HideButtonText => IsHidden ? "取消隐藏" : "隐藏";

    public bool CanRetry => Status == nameof(MetadataStatus.Failed);
}
