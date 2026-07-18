# OfflinePili

一套自托管的本地视频归档播放系统。由三个部分组成：

| 组件 | 位置 | 作用 |
|---|---|---|
| 服务端 (BiliRestart) | 本仓库 `server/` | 在存放归档文件的电脑上运行，向局域网提供视频、弹幕、评论和元数据的只读服务 |
| 客户端 (OfflinePili-Client) | [OfflinePili-Client](https://github.com/13513208952/OfflinePili-Client) | 基于 PiliPlus 修改的播放客户端，支持 Android 与 Windows 桌面 |
| 采集器 (BiliDanmuComment) | [BiliDanmuComment](https://github.com/13513208952/BiliDanmuComment) | 采集视频对应的弹幕与评论存档 |

本项目不提供、也不下载任何视频内容。视频文件需要使用者自行准备，并整理成
`docs/归档格式规范.md` 所描述的存放格式。技术设计的说明见 `docs/技术说明.md`。

## 使用步骤

### 一、准备视频文件

使用你自己的下载工具获取视频，按照 `docs/归档格式规范.md` 整理：
单个 MP4 文件按 `av{号}.mp4` 命名平铺在下载目录，并维护一个记录状态的
`archive.db`（SQLite，单表，建表语句见规范文档）。规范同时覆盖多分P和
番剧的目录布局。

### 二、采集弹幕与评论（可选）

弹幕评论是可选层，缺失时视频照常播放。使用 BiliDanmuComment：

1. 从本仓库 Releases 下载 BiliDanmuComment，解压运行（需要 Windows 与 .NET 9 桌面运行时）
2. 在设置中填入 Cookie（登录后浏览器可取得；采集历史弹幕需要）
3. 输入 av 号列表开始采集，产物为每个视频一个目录，内含 `danmaku.7z` 与 `comments.7z`

### 三、部署服务端

1. 从 Releases 下载服务端包，解压到存放归档文件的 Windows 电脑上
2. 运行 `BiliRestart.Admin.Host.exe` 打开管理面板（面板只在本机使用，没有网络管理接口）
3. 在「路径配置」页填入 `archive.db` 的路径和采集器的输出根目录，保存
4. 在「目录状态」页点「立即扫描」。服务端会向公开接口回填标题、简介、封面等
   元数据（只读请求，不需要登录）；已失效的条目会标记失败原因，可在面板中
   手动补录或隐藏
5. 防火墙放行 TCP 5299 端口
6. 无桌面环境可用 `BiliRestart.Admin.Host.exe --headless` 纯服务运行，
   配置直接编辑同目录的 `appsettings.json`

服务端也可在 Linux 上以 headless 方式构建运行（.NET 9）。

### 四、配置客户端

1. Android 安装对应架构的 APK（不确定就选 arm64-v8a）；Windows 解压便携包运行
2. 设置 → 单机怀旧模式：打开开关，填服务端地址与端口（默认 5299），测试连接后保存
3. 重启客户端，首页会出现「怀旧推荐」标签页，内容全部来自你的归档
4. 主地址与备用地址可分别填内网与公网地址，客户端逐个探测自动选用
5. USB 直连：手机用数据线连接服务端电脑并开启 USB 调试后，服务端会自动
   建立隧道；客户端把「USB 直连」设为启用即可在无网络时使用。服务端包内
   已附带所需的 adb

客户端中的点赞、投币、收藏、关注、弹幕、评论等互动只写入本机存储，
不会发送到服务端或任何第三方。

## 与 PiliPlus 的关系

客户端是 [PiliPlus](https://github.com/bggRGjQaUbCoE/PiliPlus) 的派生作品，
依 GPL-3.0 发布。改动集中在新增的离线模式上，所有触及上游代码的位置均以
注释标记，清单见客户端仓库的 `FORK_DIFF.md`。感谢 PiliPlus 及其上游项目的工作。

## 许可证

服务端与客户端使用 GPL-3.0，采集器使用 MIT。详见各自仓库的 LICENSE 文件。

## 声明

本项目仅用于个人对自有存档内容的整理与回放。请遵守当地法律法规及相关平台的
服务条款，不要将本项目用于内容的公开分发或其他侵权用途。
