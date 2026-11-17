using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryMonitor
{
    internal sealed class HidDevice
    {
        public string Path;
        public ushort VendorId, ProductId, UsagePage, Usage;
        public int InputLength, OutputLength, FeatureLength;
        public override string ToString()
        {
            return String.Format("{0:X4}:{1:X4} page={2:X4} usage={3:X4} input={4} output={5} feature={6}",
                VendorId, ProductId, UsagePage, Usage, InputLength, OutputLength, FeatureLength);
        }
    }

    internal static class NativeHid
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterface
        {
            public int Size;
            public Guid ClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Attributes
        {
            public int Size;
            public ushort VendorId, ProductId, Version;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Caps
        {
            public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkNodes, InputButtons, InputValues, InputData;
            public ushort OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Overlapped
        {
            public IntPtr Internal, InternalHigh;
            public uint Offset, OffsetHigh;
            public IntPtr Event;
        }

        public static List<HidDevice> Enumerate()
        {
            Guid guid;
            HidD_GetHidGuid(out guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
            if (set == new IntPtr(-1)) throw new Win32Exception();
            List<HidDevice> devices = new List<HidDevice>();
            try
            {
                for (uint index = 0; ; index++)
                {
                    DeviceInterface info = new DeviceInterface();
                    info.Size = Marshal.SizeOf(typeof(DeviceInterface));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref info))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 259) break;
                        throw new Win32Exception(error);
                    }
                    uint size;
                    SetupDiGetDeviceInterfaceDetail(set, ref info, IntPtr.Zero, 0, out size, IntPtr.Zero);
                    if (size < 6 || size > 65536) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref info, detail, size, out size, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                        // Never open keyboard/mouse input collections or unrelated peripherals.
                        if (path == null || !(path.IndexOf("vid_5253&pid_1020", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              path.IndexOf("vid_291d&pid_385d", StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        using (SafeFileHandle handle = Open(path, false))
                        {
                            if (handle.IsInvalid) continue;
                            Attributes attr = new Attributes();
                            attr.Size = Marshal.SizeOf(typeof(Attributes));
                            if (!HidD_GetAttributes(handle, ref attr)) continue;
                            IntPtr data;
                            if (!HidD_GetPreparsedData(handle, out data)) continue;
                            try
                            {
                                Caps caps;
                                if (HidP_GetCaps(data, out caps) != 0x110000) continue;
                                devices.Add(new HidDevice { Path = path, VendorId = attr.VendorId, ProductId = attr.ProductId,
                                    UsagePage = caps.UsagePage, Usage = caps.Usage, InputLength = caps.InputLength,
                                    OutputLength = caps.OutputLength, FeatureLength = caps.FeatureLength });
                            }
                            finally { HidD_FreePreparsedData(data); }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return devices;
        }

        public static SafeFileHandle Open(string path, bool readWrite)
        {
            return CreateFile(path, readWrite ? 0xC0000000u : 0u, 3, IntPtr.Zero, 3,
                readWrite ? 0x40000000u : 0u, IntPtr.Zero);
        }

        // A native operation owns its unmanaged buffer until completion, including cancellation.
        public static byte[] Transfer(SafeFileHandle handle, byte[] output, int inputLength, int timeoutMs)
        {
            bool writing = output != null;
            int length = writing ? output.Length : inputLength;
            IntPtr buffer = Marshal.AllocHGlobal(length);
            IntPtr native = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlapped)));
            using (EventWaitHandle completed = new EventWaitHandle(false, EventResetMode.ManualReset))
            {
                try
                {
                    if (writing) Marshal.Copy(output, 0, buffer, length);
                    Marshal.StructureToPtr(new Overlapped { Event = completed.SafeWaitHandle.DangerousGetHandle() }, native, false);
                    bool started = writing ? WriteFile(handle, buffer, (uint)length, IntPtr.Zero, native)
                                           : ReadFile(handle, buffer, (uint)length, IntPtr.Zero, native);
                    if (!started && Marshal.GetLastWin32Error() != 997) throw new Win32Exception();
                    uint count;
                    if (!GetOverlappedResultEx(handle, native, out count, (uint)Math.Max(0, timeoutMs), false))
                    {
                        int error = Marshal.GetLastWin32Error();
                        CancelIoEx(handle, native);
                        GetOverlappedResult(handle, native, out count, true);
                        if (error == 258 || error == 996 || error == 995) throw new TimeoutException("设备未在时限内应答");
                        throw new Win32Exception(error);
                    }
                    if (writing)
                    {
                        if (count != length) throw new InvalidOperationException("设备查询未完整发送");
                        return null;
                    }
                    byte[] result = new byte[count];
                    Marshal.Copy(buffer, result, 0, result.Length);
                    return result;
                }
                finally { Marshal.FreeHGlobal(native); Marshal.FreeHGlobal(buffer); }
            }
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
        [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
        [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
        [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
        [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] report, int length);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr dev, ref Guid guid, uint index, ref DeviceInterface info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterface info, IntPtr detail, uint size, out uint needed, IntPtr dev);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint length, IntPtr count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, uint length, IntPtr count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResultEx(SafeFileHandle handle, IntPtr overlapped, out uint count, uint timeout, bool alertable);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlapped, out uint count, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    }
}
