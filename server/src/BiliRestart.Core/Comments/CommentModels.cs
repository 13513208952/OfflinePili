using System.Text.Json.Serialization;

namespace BiliRestart.Core.Comments;

// 与 BiliDanmuComment 采集器的 CommentPage/CommentReply 形状一致（snake_case），
// 直接反序列化归档产出的 comments/av{N}.json，再原样（或轻微重整形）转发给客户端。
public sealed class CommentPage
{
    [JsonPropertyName("aid")] public long Aid { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("replies")] public List<CommentReply> Replies { get; set; } = [];
}

public sealed class CommentReply
{
    [JsonPropertyName("rpid")] public long Rpid { get; set; }
    [JsonPropertyName("oid")] public long Oid { get; set; }
    [JsonPropertyName("mid")] public long Mid { get; set; }
    [JsonPropertyName("root")] public long Root { get; set; }
    [JsonPropertyName("parent")] public long Parent { get; set; }
    [JsonPropertyName("rcount")] public int Rcount { get; set; }
    [JsonPropertyName("like")] public int Like { get; set; }
    [JsonPropertyName("ctime")] public long Ctime { get; set; }
    [JsonPropertyName("content")] public CommentContent Content { get; set; } = new();
    [JsonPropertyName("member")] public CommentMember Member { get; set; } = new();
    [JsonPropertyName("reply_control")] public CommentReplyControl ReplyControl { get; set; } = new();
    [JsonPropertyName("sub_replies")] public List<CommentReply> SubReplies { get; set; } = [];
}

public sealed class CommentContent
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

public sealed class CommentMember
{
    [JsonPropertyName("mid")] public long Mid { get; set; }
    [JsonPropertyName("uname")] public string Uname { get; set; } = "";
    [JsonPropertyName("sex")] public string Sex { get; set; } = "";
    [JsonPropertyName("level_info")] public CommentLevelInfo LevelInfo { get; set; } = new();
    [JsonPropertyName("face")] public string Face { get; set; } = "";
}

public sealed class CommentLevelInfo
{
    [JsonPropertyName("current_level")] public int CurrentLevel { get; set; }
}

public sealed class CommentReplyControl
{
    [JsonPropertyName("location")] public string Location { get; set; } = "";
}
