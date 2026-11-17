# 验证入口

在项目根目录的 Windows PowerShell 中执行：

```powershell
# 构建、合成帧、来源/选择逻辑、子进程故障隔离、图标检查
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test

# 实际 WinForms 消息循环 + 合成设备工作进程，无须硬件
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\ui-smoke.ps1

# 真实迈从接收器检查：需要设备连接、唤醒；无设备或休眠可能返回非零
.\tests\TestRunner.exe --hardware

# 查询所有来源；管道确保 PowerShell 等待 GUI 子系统程序结束
& .\dist\BatteryMonitor.exe --list | Out-Host
```

托盘检查使用 `tests/artifacts/tray-selection.txt`，不会更改用户的左右设备设置，也不切换开机启动。
检查包含启动不查询、手动查询、选择菜单保存、31 秒不自动查询、再次查询与退出清理。测试期间会出现临时托盘图标。

`tests/artifacts/` 存放所有测试输出、图标预览和诊断文件，默认忽略提交。工作进程超时测试使用合成数据，无效帧或超时结果不代表具体硬件行为。
当前结果见 [验证记录](../docs/VALIDATION.md)，历史结果见 [旧版记录](../docs/VALIDATION-LEGACY.md)。
