using System.Reflection;
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
                // 补丁自带一堆依赖(BiliArchive 及其库)在 plugins/ 里，用带依赖解析器的
                // 独立上下文加载；而 BiliRestart.Core(含本接口)等共享程序集仍回退到宿主
                // 已加载的 Default 上下文，保证接口类型身份一致、is 判定成立。
                var ctx = new PluginLoadContext(Path.GetFullPath(dll));
                var asm = ctx.LoadFromAssemblyPath(Path.GetFullPath(dll));
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

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: false)
            => _resolver = new AssemblyDependencyResolver(pluginPath);

        protected override Assembly? Load(AssemblyName name)
        {
            // 补丁自己的依赖(在其 deps.json 里)从 plugins/ 解析；解析不到的(如
            // BiliRestart.Core，已 ExcludeAssets=runtime 不入补丁包)返回 null，
            // 交回 Default 上下文用宿主那份——避免共享类型被加载两遍。
            var path = _resolver.ResolveAssemblyToPath(name);
            return path != null ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }
}
