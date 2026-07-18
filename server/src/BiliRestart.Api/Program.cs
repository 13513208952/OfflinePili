// 独立(headless)资源服务入口：纯API+后台回填，无GUI，
// 适合跑在无桌面会话的Linux盒子上。带管理面板的组合入口见 BiliRestart.Admin.Host。
BiliRestart.Api.ApiBootstrap.Build(args).Run();
