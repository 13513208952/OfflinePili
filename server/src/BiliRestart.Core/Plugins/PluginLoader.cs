using System.Runtime.Loader;

namespace BiliRestart.Core.Plugins;

// 扫描 plugins/ 目录、加载实现了 IVideoAcquisitionPlugin 的补丁 DLL。
// 无目录 / 无 DLL / 无实现 → 返回 null，视频获取能力视为不存在(主程序不含下载逻辑)。
// 这是"功能性代码在补丁、主程序只留可选接口"这一约束的落点。
public static class PluginLoader
{
    public static IVideoAcquisitionPlugin? LoadVideoAcquisition(string pluginsDir)
    {
        if (!Directory.Exists(pluginsDir)) return null;

        foreach (var dll in Directory.GetFiles(pluginsDir, "*.dll"))
        {
            try
            {
                var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
                var type = asm.GetTypes().FirstOrDefault(t =>
                    typeof(IVideoAcquisitionPlugin).IsAssignableFrom(t)
                    && t is { IsAbstract: false, IsInterface: false });
                if (type != null && Activator.CreateInstance(type) is IVideoAcquisitionPlugin plugin)
                {
                    return plugin;
                }
            }
            catch
            {
                // 某个 DLL 加载/实例化失败不影响主程序，跳过继续找
            }
        }
        return null;
    }
}
