using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryMonitor
{
    internal static class NativeBluetooth
    {
        // BOOLEAN is one byte; GUID is four-byte aligned inside the native union.
        [StructLayout(LayoutKind.Explicit, Size = 20)]
        internal struct Uuid
        {
            [FieldOffset(0)] public byte IsShort;
            [FieldOffset(4)] public ushort Short;
            [FieldOffset(4)] public Guid Long;
            public bool Is(ushort value)
            { return IsShort != 0 ? Short == value : Long == new Guid("0000" + value.ToString("x4") + "-0000-1000-8000-00805f9b34fb"); }
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Service { public Uuid Uuid; public ushort Handle; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Characteristic
        {
            public ushort ServiceHandle;
            public Uuid Uuid;
            public ushort AttributeHandle, ValueHandle;
            public byte Broadcastable, Readable, Writable, WritableWithoutResponse, SignedWritable, Notifiable, Indicatable, Extended;
        }
        private const int MoreData = unchecked((int)0x800700EA);
        private const uint ForceReadDevice = 4;

        public static BatteryReading ParseValue(byte[] value)
        {
            return value != null && value.Length == 1 && value[0] <= 100
                ? new BatteryReading(value[0], BatteryStatus.Online, "BLE 标准电量；设备直接应答")
                : new BatteryReading(null, BatteryStatus.Unknown, "BLE 电量数据无效");
        }
        private static Service[] Services(SafeFileHandle handle)
        {
            ushort count, actual;
            int hr = BluetoothGATTGetServices(handle, 0, IntPtr.Zero, out count, 0);
            if (hr == 0 && count == 0) return new Service[0];
            if (hr != MoreData || count == 0 || count > 256) Marshal.ThrowExceptionForHR(hr == 0 ? unchecked((int)0x8007000D) : hr);
            int size = Marshal.SizeOf(typeof(Service));
            IntPtr buffer = Marshal.AllocHGlobal(size * count);
            try
            {
                Marshal.ThrowExceptionForHR(BluetoothGATTGetServices(handle, count, buffer, out actual, 0));
                if (actual > count) throw new InvalidOperationException();
                Service[] services = new Service[actual];
                for (int i = 0; i < actual; i++) services[i] = (Service)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(Service));
                return services;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        private static Characteristic[] Characteristics(SafeFileHandle handle, Service service, bool serviceHandle)
        {
            ushort count, actual;
            int hr = serviceHandle ? BluetoothGATTGetServiceCharacteristics(handle, IntPtr.Zero, 0, IntPtr.Zero, out count, 0) :
                BluetoothGATTGetCharacteristics(handle, ref service, 0, IntPtr.Zero, out count, 0);
            if (hr == 0 && count == 0) return new Characteristic[0];
            if (hr != MoreData || count == 0 || count > 256) Marshal.ThrowExceptionForHR(hr == 0 ? unchecked((int)0x8007000D) : hr);
            int size = Marshal.SizeOf(typeof(Characteristic));
            IntPtr buffer = Marshal.AllocHGlobal(size * count);
            try
            {
                hr = serviceHandle ? BluetoothGATTGetServiceCharacteristics(handle, IntPtr.Zero, count, buffer, out actual, 0) :
                    BluetoothGATTGetCharacteristics(handle, ref service, count, buffer, out actual, 0);
                Marshal.ThrowExceptionForHR(hr);
                if (actual > count) throw new InvalidOperationException();
                Characteristic[] chars = new Characteristic[actual];
                for (int i = 0; i < actual; i++) chars[i] = (Characteristic)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(Characteristic));
                return chars;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        private static BatteryReading Value(SafeFileHandle handle, Characteristic characteristic)
        {
            ushort required;
            int hr = BluetoothGATTGetCharacteristicValue(handle, ref characteristic, 0, IntPtr.Zero, out required, ForceReadDevice);
            if (hr != MoreData || required < 5 || required > 512) return new BatteryReading(null, BatteryStatus.Unknown, "BLE 电量不可读取");
            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                uint capacity = required;
                Marshal.ThrowExceptionForHR(BluetoothGATTGetCharacteristicValue(handle, ref characteristic, capacity, buffer, out required, ForceReadDevice));
                int length = Marshal.ReadInt32(buffer);
                if (length != 1 || required > capacity) return ParseValue(null);
                return ParseValue(new[] { Marshal.ReadByte(buffer, 4) });
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public static BatteryReading Read(BatteryDevice device)
        {
            try
            {
                string servicePath = NativeDevices.FindBatteryService(device.ContainerId);
                string path = servicePath ?? device.Address;
                using (SafeFileHandle handle = CreateFile(path, 0x80000000u, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid) throw new Win32Exception();
                    Service[] services = servicePath == null ? Services(handle) : new[] { new Service() };
                    foreach (Service service in services)
                    {
                        if (servicePath == null && !service.Uuid.Is(0x180F)) continue;
                        foreach (Characteristic characteristic in Characteristics(handle, service, servicePath != null))
                            if (characteristic.Uuid.Is(0x2A19) && characteristic.Readable != 0) return Value(handle, characteristic);
                    }
                    return new BatteryReading(null, BatteryStatus.Unknown, "设备未公开可读的标准 BLE 电量服务");
                }
            }
            catch (COMException) { return new BatteryReading(null, BatteryStatus.Error, "BLE 查询失败；请确认设备已连接"); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("BluetoothAPIs.dll")] private static extern int BluetoothGATTGetServices(SafeFileHandle handle, ushort count, IntPtr services, out ushort actual, uint flags);
        [DllImport("BluetoothAPIs.dll")] private static extern int BluetoothGATTGetCharacteristics(SafeFileHandle handle, ref Service service, ushort count, IntPtr characteristics, out ushort actual, uint flags);
        [DllImport("BluetoothAPIs.dll", EntryPoint = "BluetoothGATTGetCharacteristics")]
        private static extern int BluetoothGATTGetServiceCharacteristics(SafeFileHandle handle, IntPtr service, ushort count, IntPtr characteristics, out ushort actual, uint flags);
        [DllImport("BluetoothAPIs.dll")] private static extern int BluetoothGATTGetCharacteristicValue(SafeFileHandle handle, ref Characteristic characteristic, uint size, IntPtr value, out ushort required, uint flags);
    }
}
