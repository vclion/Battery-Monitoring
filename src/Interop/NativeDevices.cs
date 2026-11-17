using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace BatteryMonitor
{
    internal static class NativeDevices
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceInfo
        {
            public int Size;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct InterfaceInfo
        {
            public int Size;
            public Guid ClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
            public PropertyKey(string format, uint id) { Format = new Guid(format); Id = id; }
        }
        private static readonly Guid BleInterface = new Guid("781AEE18-7733-4CE4-ADD0-91F41C67B592");
        private const string DeviceProperties = "A45C254E-DF1C-4EFD-8020-67D146A850E0";

        private static byte[] Property(IntPtr set, ref DeviceInfo info, string format, uint id, uint expectedType)
        {
            PropertyKey key = new PropertyKey(format, id);
            uint type, required;
            byte[] buffer = new byte[4096];
            if (!SetupDiGetDeviceProperty(set, ref info, ref key, out type, buffer, (uint)buffer.Length, out required, 0)
                || type != expectedType || required > buffer.Length) return null;
            Array.Resize(ref buffer, (int)required);
            return buffer;
        }
        private static string TextProperty(IntPtr set, ref DeviceInfo info, uint id)
        {
            byte[] bytes = Property(set, ref info, DeviceProperties, id, 0x12);
            return bytes == null ? "" : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        private static string Container(IntPtr set, ref DeviceInfo info)
        {
            byte[] bytes = Property(set, ref info, "8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2, 0x0D);
            return bytes == null || bytes.Length != 16 || new Guid(bytes) == Guid.Empty ? "" : new Guid(bytes).ToString("D");
        }
        private static string Name(IntPtr set, ref DeviceInfo info)
        {
            string name = TextProperty(set, ref info, 14);
            if (name.Length == 0) name = TextProperty(set, ref info, 2);
            return name.Length == 0 ? "蓝牙设备" : name;
        }
        public static List<BatteryDevice> EnumerateBluetoothLe()
        {
            return EnumerateInterfaces(BleInterface);
        }
        public static string FindBatteryService(string containerId)
        {
            if (string.IsNullOrEmpty(containerId)) return null;
            // Windows also registers a service interface under its Bluetooth service UUID.
            foreach (BatteryDevice service in EnumerateInterfaces(new Guid("0000180F-0000-1000-8000-00805F9B34FB")))
                if (string.Equals(service.ContainerId, containerId, StringComparison.OrdinalIgnoreCase)) return service.Address;
            return null;
        }
        private static List<BatteryDevice> EnumerateInterfaces(Guid guid)
        {
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            var result = new List<BatteryDevice>();
            try
            {
                for (uint index = 0; index < 256; index++)
                {
                    InterfaceInfo iface = new InterfaceInfo { Size = Marshal.SizeOf(typeof(InterfaceInfo)) };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref iface))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        throw new Win32Exception();
                    }
                    uint size;
                    DeviceInfo info = new DeviceInfo { Size = Marshal.SizeOf(typeof(DeviceInfo)) };
                    SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out size, ref info);
                    if (size < 6 || size > 32768) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, size, out size, ref info)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        if (string.IsNullOrEmpty(path)) continue;
                        result.Add(new BatteryDevice { Address = path, Id = BatteryDevice.MakeId(DeviceSource.BluetoothLe, path),
                            Name = Name(set, ref info), ContainerId = Container(set, ref info),
                            Kind = DeviceKind.Other, Source = DeviceSource.BluetoothLe });
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        // Compatibility fallback used only when a Bluetooth driver publishes this property.
        // This property is not a public cross-device Windows contract; the Shell property is primary.
        public static List<DeviceSample> ReadBluetoothDriverProperties()
        {
            Guid bluetooth = new Guid("E0CBF06C-CD8B-4647-BB8A-263B43F0F974");
            IntPtr set = SetupDiGetClassDevs(ref bluetooth, null, IntPtr.Zero, 2);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            var result = new List<DeviceSample>();
            try
            {
                for (uint index = 0; index < 4096; index++)
                {
                    DeviceInfo info = new DeviceInfo { Size = Marshal.SizeOf(typeof(DeviceInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref info))
                    {
                        if (Marshal.GetLastWin32Error() == 259) break;
                        throw new Win32Exception();
                    }
                    byte[] level = Property(set, ref info, "104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2, 3);
                    if (level == null || level.Length != 1 || level[0] > 100) continue;
                    StringBuilder instance = new StringBuilder(2048);
                    uint required;
                    if (!SetupDiGetDeviceInstanceId(set, ref info, instance, instance.Capacity, out required)) continue;
                    string address = "pnp:" + instance;
                    result.Add(new DeviceSample { Device = new BatteryDevice { Address = address,
                        Id = BatteryDevice.MakeId(DeviceSource.Windows, address), Name = Name(set, ref info),
                        ContainerId = Container(set, ref info), Source = DeviceSource.Windows, Kind = DeviceKind.Other },
                        Reading = WindowsBattery.FromReported(level[0], null) });
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceInfo iface, IntPtr detail, uint size, out uint required, ref DeviceInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDevicePropertyW")]
        private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInstanceIdW")]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DeviceInfo info, StringBuilder id, int size, out uint required);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
