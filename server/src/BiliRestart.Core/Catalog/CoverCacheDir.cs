namespace BiliRestart.Core.Catalog;

// 封面缓存目录的DI包装：Api宿主和Avalonia管理面板都要从容器里拿它
// 来构造 ReconciliationService，所以放在Core里两边共用。
public sealed record CoverCacheDir(string Path);
