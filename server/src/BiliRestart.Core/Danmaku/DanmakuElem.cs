namespace BiliRestart.Core.Danmaku;

public sealed record DanmakuElem(
    long Id,
    int ProgressMs,
    int Mode,
    int FontSize,
    uint Color,
    string MidHash,
    string Content,
    long CTime,
    int Pool,
    string IdStr,
    int Attr);
