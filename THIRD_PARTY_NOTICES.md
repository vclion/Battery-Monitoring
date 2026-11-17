# 协议资料与致谢

此项目的 Windows 设备枚举、托盘界面、电量采集与图标绘制代码在本项目中实现。HID 电量命令和字段定义参考以下公开资料：

- [aipno/mchose-linux — 鼠标协议](https://github.com/aipno/mchose-linux/blob/b308d41335a4b6029b92a2c86fef9c0f3ce3f21b/docs/PROTOCOL.md)：鼠标 `0x11 / 0x06` 查询、异或编码、身份/电量/连接字段。
- [aipno/mchose-linux — V9 PRO 耳机协议](https://github.com/aipno/mchose-linux/blob/b308d41335a4b6029b92a2c86fef9c0f3ce3f21b/docs/HEADSET.md)：`55 65 01` 查询与耳机状态定义。
- [rafagfran/mchose-v9-pro-battery-tray](https://github.com/rafagfran/mchose-v9-pro-battery-tray/tree/214252279d2ae02294eb680ddfc85d267a5a5b68)：Windows 侧 V9 PRO HID 通道和电量帧的交叉核对。

0.2 版通用通道依据以下系统接口文档独立实现，不包含第三方二进制依赖：

- [Microsoft：System.Devices.BatteryLife](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-batterylife) 与 [System.Devices.Connected](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-connected)：系统百分比和连接属性。
- [Microsoft：Bluetooth LE overview](https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-low-energy-overview)、[BluetoothGATTGetCharacteristics](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothleapis/nf-bluetoothleapis-bluetoothgattgetcharacteristics) 与 [BluetoothGATTGetCharacteristicValue](https://learn.microsoft.com/en-us/windows/win32/api/bluetoothleapis/nf-bluetoothleapis-bluetoothgattgetcharacteristicvalue)：标准服务及设备直接读取。
- [Microsoft Q&A：Bluetooth battery tray](https://learn.microsoft.com/en-us/questions/1156194/bluetooth-device-battery-indicator-display-in-syst)：蓝牙驱动电量属性键的社区示例；该键仅为兼容回退，不作为公开稳定的系统契约。

本项目代码采用根目录 LICENSE 中的 MIT 许可证。以下 MIT 声明保留用于原有协议参考资料。

以上仓库采用 MIT License，相关许可声明保留如下。

## MIT License

Copyright (c) 2026 aipno

Copyright (c) 2026 Rafael

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
