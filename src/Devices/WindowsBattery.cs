using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BatteryMonitor
{
    internal static class WindowsBattery
    {
        public static BatteryReading FromReported(object value, bool? connected)
        {
            if (connected == false) return new BatteryReading(null, BatteryStatus.Disconnected, "Windows 报告设备未连接");
            // Do not turn null, booleans, fractions or sentinel 101/255 into a percentage.
            if (!(value is byte || value is ushort || value is uint || value is short || value is int || value is long))
                return new BatteryReading(null, BatteryStatus.Unknown, "系统未提供有效电量");
            long level = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            if (level < 0 || level > 100) return new BatteryReading(null, BatteryStatus.Unknown, "系统电量未知");
            return new BatteryReading((int)level, BatteryStatus.Reported, "系统报告；设备更新时间和连接状态可能未知");
        }
        private static object Invoke(object target, string member, BindingFlags operation, params object[] args)
        {
            return target.GetType().InvokeMember(member, operation, null, target, args);
        }
        private static object Property(object item, string name)
        {
            try { return Invoke(item, "ExtendedProperty", BindingFlags.InvokeMethod, name); }
            catch (TargetInvocationException) { return null; }
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }

        private static List<DeviceSample> ShellSamples()
        {
            var samples = new List<DeviceSample>();
            object shell = null, folder = null, items = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) throw new InvalidOperationException("Windows Shell unavailable");
                shell = Activator.CreateInstance(shellType);
                folder = Invoke(shell, "Namespace", BindingFlags.InvokeMethod, "shell:::{A8A91A66-3A7D-4424-8D24-04E180695C7A}");
                if (folder == null) throw new InvalidOperationException("Devices folder unavailable");
                items = Invoke(folder, "Items", BindingFlags.InvokeMethod);
                int count = Math.Min(4096, Convert.ToInt32(Invoke(items, "Count", BindingFlags.GetProperty)));
                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = Invoke(items, "Item", BindingFlags.InvokeMethod, i);
                        object value = Property(item, "System.Devices.BatteryLife");
                        // Devices without a percentage are not advertised as supported by this provider.
                        BatteryReading reading = FromReported(value, null);
                        if (!reading.Percentage.HasValue) continue;
                        object connected = Property(item, "System.Devices.Connected");
                        reading = FromReported(value, connected is bool ? (bool?)connected : null);
                        string path = Convert.ToString(Invoke(item, "Path", BindingFlags.GetProperty));
                        string name = Convert.ToString(Invoke(item, "Name", BindingFlags.GetProperty));
                        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name)) continue;
                        Guid container;
                        string containerId = Convert.ToString(Property(item, "System.Devices.ContainerId"));
                        containerId = Guid.TryParse(containerId, out container) && container != Guid.Empty ? container.ToString("D") : "";
                        string address = "shell:" + path;
                        samples.Add(new DeviceSample { Device = new BatteryDevice {
                            Id = BatteryDevice.MakeId(DeviceSource.Windows, address), Address = address, Name = name,
                            ContainerId = containerId, Kind = DeviceKind.Other, Source = DeviceSource.Windows }, Reading = reading });
                    }
                    catch (TargetInvocationException) { /* One inaccessible item must not hide the others. */ }
                    finally { Release(item); }
                }
            }
            finally { Release(items); Release(folder); Release(shell); }
            return samples;
        }
        private static List<DeviceSample> Samples(bool pnpOnly)
        {
            if (pnpOnly) return NativeDevices.ReadBluetoothDriverProperties();
            return ShellSamples();
        }
        public static List<BatteryDevice> Discover()
        {
            // Enumerated in separate processes so a Shell extension cannot block the driver fallback.
            var result = new List<BatteryDevice>();
            foreach (DeviceSample sample in Samples(false)) result.Add(sample.Device);
            return result;
        }
        public static List<BatteryDevice> DiscoverDriverProperties()
        {
            var result = new List<BatteryDevice>();
            foreach (DeviceSample sample in Samples(true)) result.Add(sample.Device);
            return result;
        }
        public static BatteryReading Read(BatteryDevice device)
        {
            foreach (DeviceSample sample in Samples(device.Address.StartsWith("pnp:", StringComparison.Ordinal)))
                if (sample.Device.Id == device.Id) return sample.Reading;
            return new BatteryReading(null, BatteryStatus.Unknown, "系统当前未提供该设备的有效电量");
        }
    }
}
