using System.Globalization;
using System.Xml.Linq;

namespace BiliRestart.Core.Danmaku;

// 解析 BiliDanmuComment 采集器产出的原生弹幕 XML：
// <d p="time,mode,fontsize,color,ctime,pool,midHash,dmid,attr">content</d>
public static class DanmakuXmlParser
{
    public static IReadOnlyList<DanmakuElem> Parse(Stream xmlStream)
    {
        var doc = XDocument.Load(xmlStream);
        var result = new List<DanmakuElem>();

        foreach (var d in doc.Descendants("d"))
        {
            var p = d.Attribute("p")?.Value;
            if (string.IsNullOrEmpty(p)) continue;

            var parts = p.Split(',');
            if (parts.Length < 9) continue;

            var timeSeconds = double.Parse(parts[0], CultureInfo.InvariantCulture);
            var mode = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var fontSize = int.Parse(parts[2], CultureInfo.InvariantCulture);
            var color = uint.Parse(parts[3], CultureInfo.InvariantCulture);
            var ctime = long.Parse(parts[4], CultureInfo.InvariantCulture);
            var pool = int.Parse(parts[5], CultureInfo.InvariantCulture);
            var midHash = parts[6];
            var dmidStr = parts[7];
            var attr = int.Parse(parts[8], CultureInfo.InvariantCulture);

            long.TryParse(dmidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dmid);

            result.Add(new DanmakuElem(
                Id: dmid,
                ProgressMs: (int)Math.Round(timeSeconds * 1000),
                Mode: mode,
                FontSize: fontSize,
                Color: color,
                MidHash: midHash,
                Content: d.Value,
                CTime: ctime,
                Pool: pool,
                IdStr: dmidStr,
                Attr: attr));
        }

        return result;
    }
}
