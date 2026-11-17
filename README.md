# Battery Monitor 电量监控

Windows 系统托盘电量工具：用一个全黑圆形图标显示两台可选设备的电量，右键查看其他设备及数据来源。支持现有迈从专用协议、Windows 系统电量与标准蓝牙 BLE 电量服务。

**跨品牌支持有条件**：设备必须公开系统电量或可读的 BLE 标准服务。私有 2.4G 协议仍需单独适配；当前实际硬件验证仅覆盖迈从 L7 PRO 和 V9 PRO。[完整兼容范围](docs/COMPATIBILITY.md)。

## 功能

- 左右半圆分别显示所选设备；黑色轮廓与填充之间留白，中间有细缝，空白透明。
- 左键单击或右键“查询电量”，发现设备并读取一次。启动、定时、接收器拔插和系统唤醒均不自动查询。
- “设备电量”菜单查看全部发现结果；“左半圆显示 / 右半圆显示”选择设备，选择保存在本机。
- 悬停显示两侧读数及本次查询时间。Windows 缓存值注明“系统”，设备更新时间可能未知。
- 未知或失败显示短横线；失败会清除旧成功值，真实 0% 不会与未知混淆。
- 每台设备独立限时查询；单台设备超时后继续查询其他设备，托盘仍可操作。
- 提供开机启动开关、单实例与退出；无主窗口、电量历史、弹窗提醒或网络服务。

## 支持的读取方式

| 来源 | 范围 | 验证状态 |
| --- | --- | --- |
| 迈从专用 HID | L7 PRO 鼠标、V9 PRO 耳机，2.4G 接收器 | 本项目有实机结果 |
| Windows 系统报告 | 通过系统设备属性提供百分比的设备，不限品牌 | 已实现与自动检查，新增型号待实测 |
| 标准 BLE 服务 | 提供可读 Battery Service `180F` / Battery Level `2A19` 的已配对设备 | 已实现与自动检查，待 BLE 真机验收 |

Windows 通道还包含蓝牙驱动电量属性兼容回退；该属性不属于稳定的跨设备契约。系统读数可能是缓存，不能当作设备刚刚应答。耳机、鼠标、键盘等能否使用取决于设备、固件、驱动和连接方式，而不是品牌名称。

## 使用

便携版本：解压后运行 `BatteryMonitor.exe`。首次启动显示未知，点击“查询电量”后再选择左右设备。
如果图标在任务栏的折叠区，可以拖到常显区域，或在 Windows 设置中调整系统托盘图标。

需要编译时，在 **Windows x64 PowerShell** 的项目根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test
.\dist\BatteryMonitor.exe
```

使用 Windows 自带 .NET Framework 4.x 编译器、WinForms、GDI+、Shell 与系统 HID/BLE API，不下载 NuGet 包，也不要求安装 .NET SDK。
面向 Windows 10/11 x64；本机验证环境为 Windows 11。源码可存放在 WSL 共享目录，构建与运行必须在 Windows 侧完成。

可选：安装到当前用户的本地目录并创建桌面快捷方式：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1
# 如需同时开启登录启动：
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1 -EnableStartup
```

安装默认保留现有开机启动状态，也可在托盘菜单切换。“退出”只结束本次运行。

## 构建与发布

```powershell
# 构建并执行测试，再生成便携 ZIP
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1

# 查询并输出设备名、来源与读数，不输出设备路径
& .\dist\BatteryMonitor.exe --list | Out-Host
```

输出位于 `dist/`，测试及诊断产物位于 `tests/artifacts/`，均已加入忽略规则。
发布 ZIP 包含 EXE、ICO、README、许可证、第三方声明和文档，不包含本机配置或测试采集。
另外生成 `dist/BatteryMonitor-source.zip`，包含整理后的源码、脚本、文档、测试源码及 GitHub 配置，可解压后作为仓库上传；不包含 EXE 或本机测试产物。
GitHub Actions 配置了 Windows 构建、合成数据测试和便携 ZIP 产物；[查看构建状态](https://github.com/vclion/Battery-Monitoring/actions/workflows/windows.yml)。

## 项目目录与验证

- `src/Core/`：设备模型、查询编排、进程通信和左右选择。
- `src/Devices/`：设备适配与协议；`src/Interop/`：Windows 原生调用；`src/UI/`：托盘与图标。
- `scripts/`：构建、安装、打包；`tests/`：自动测试与交互检查；`docs/`：说明和验证记录。

[架构](docs/ARCHITECTURE.md) · [测试入口](tests/README.md) · [验证记录](docs/VALIDATION.md) · [贡献指南](CONTRIBUTING.md)

## 许可与致谢

采用 [MIT License](LICENSE)。公开协议资料与原有许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
本项目与迈从或其他设备厂商无隶属关系。
