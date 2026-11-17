# 参与贡献

## 开发与验证

在 Windows x64 上执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test`。
仅使用 Windows 自带 .NET Framework 编译器与系统 API，不引入 NuGet 包或本地模型。
托盘交互检查见 [tests/README.md](tests/README.md)。测试输出、截图和诊断文件放入 `tests/artifacts/`，不提交真实设备路径、序列号、蓝牙地址或个人配置。

## 反馈兼容性

请提供品牌、完整型号、连接方式、Windows 版本、程序显示的来源与错误。
USB VID/PID 可以提供；MAC 地址、设备实例路径、用户名及序列号请删去。
`BatteryMonitor.exe --list` 可以输出名称、来源和读数；设备名称可能包含个人自定义内容，发布前请检查。
不要将“Windows 设置显示电量”“BLE 标准服务可用”“厂商 2.4G 协议可用”混为一种连接方式。

## 增加适配

1. 在 `src/Devices/` 增加协议逻辑，必要的 Windows 调用放入 `src/Interop/`，接入 `DeviceCatalog` 与 `DeviceQueries`。
2. 厂商 HID 命令必须有公开协议或实机证据，并校验 VID/PID、usage page、usage、报告长度和应答字段。不要向未知厂商接口试发命令。
3. 只查询电量，不修改设置或固件；未知、休眠、断开和超时使用空电量，真实 0% 保留。
4. 补充畸形帧和失败路径测试。使用合成帧，不提交带身份信息的原始采集。
5. 更新 [兼容列表](docs/COMPATIBILITY.md)，标明公开来源、连接方式、是否本项目实测；补充必要的第三方许可。

GitHub Actions 仅验证构建与合成数据测试，不代表真实硬件兼容性验收。提交代码即按本项目 MIT 许可证许可你的贡献。
