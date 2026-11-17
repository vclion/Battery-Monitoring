using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace BatteryMonitor
{
    internal static class DeviceCatalog
    {
        public static List<BatteryDevice> Discover(string provider)
        {
            if (provider == "windows") return WindowsBattery.Discover();
            if (provider == "pnp") return WindowsBattery.DiscoverDriverProperties();
            if (provider == "ble") return NativeDevices.EnumerateBluetoothLe();
            if (provider != "mchose") throw new ArgumentException("Unknown provider");
            var result = new List<BatteryDevice>();
            foreach (HidDevice hid in NativeHid.Enumerate())
            {
                bool headset = DeviceReader.IsHeadset(hid);
                if (!headset && !DeviceReader.IsMouse(hid)) continue;
                DeviceSource source = headset ? DeviceSource.MchoseHeadset : DeviceSource.MchoseMouse;
                result.Add(new BatteryDevice { Id = BatteryDevice.MakeId(source, hid.Path), Address = hid.Path,
                    Name = headset ? "迈从 V9 PRO" : "迈从 L7 PRO", Source = source,
                    Kind = headset ? DeviceKind.Audio : DeviceKind.Mouse, ContainerId = "" });
            }
            return result;
        }

        public static BatteryReading Read(BatteryDevice device)
        {
            try
            {
                if (device.Source == DeviceSource.Windows) return WindowsBattery.Read(device);
                if (device.Source == DeviceSource.BluetoothLe)
                {
                    // Re-discover to validate the input and to avoid opening arbitrary paths.
                    BatteryDevice current = NativeDevices.EnumerateBluetoothLe().Find(d => d.Id == device.Id);
                    return current == null ? new BatteryReading(null, BatteryStatus.Disconnected, "蓝牙设备未发现") : NativeBluetooth.Read(current);
                }
                var candidates = NativeHid.Enumerate();
                candidates.RemoveAll(d => !string.Equals(d.Path, device.Address, StringComparison.OrdinalIgnoreCase));
                return DeviceReader.Read(candidates, device.Source == DeviceSource.MchoseHeadset);
            }
            catch (TimeoutException) { return new BatteryReading(null, BatteryStatus.Error, "设备查询超时"); }
            catch (Win32Exception) { return new BatteryReading(null, BatteryStatus.Error, "无法读取设备通道"); }
            catch (InvalidOperationException) { return new BatteryReading(null, BatteryStatus.Error, "设备电量应答无效"); }
        }
    }
}
