# tools/adb

「USB 直连」功能会调用此目录下的 `adb.exe` 为数据线连接的设备布置
`adb reverse` 隧道。为保持仓库精简，这些第三方二进制不纳入版本管理
（见仓库根 `.gitignore`），但会随发布包一起附带。

从源码自行构建时，若需要 USB 直连，请任选其一：

- 从 Google Android SDK Platform-Tools 下载 `adb.exe`、`AdbWinApi.dll`、
  `AdbWinUsbApi.dll` 放入本目录；或
- 将系统已安装的 adb 加入 PATH，或在 `appsettings.json` 的
  `UsbLink:AdbPath` 中指定其路径。

不提供 adb 时，服务端其余功能不受影响，仅 USB 直连不可用（仍可用局域网
或公网地址连接）。adb 依 Apache License 2.0 分发，见 NOTICE.txt。
