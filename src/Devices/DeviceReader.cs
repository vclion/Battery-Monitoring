using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace BatteryMonitor
{
    internal static class DeviceReader
    {
        public static bool IsMouse(HidDevice d)
        {
            return d.VendorId == 0x5253 && d.ProductId == 0x1020 && d.UsagePage == 0xFF01 && d.Usage == 1 && d.FeatureLength >= 21;
        }
        public static bool IsHeadset(HidDevice d)
        {
            return d.VendorId == 0x291D && d.ProductId == 0x385D && d.UsagePage == 0xFF90 && d.Usage == 1 && d.InputLength == 64 && d.OutputLength == 64;
        }
        public static BatteryReading Read(List<HidDevice> devices, bool headset)
        {
            foreach (HidDevice device in devices)
            {
                if (!(headset ? IsHeadset(device) : IsMouse(device))) continue;
                try { return headset ? ReadHeadset(device) : ReadMouse(device); }
                catch (TimeoutException) { return new BatteryReading(null, BatteryStatus.Error, "电量读取超时"); }
                catch (Win32Exception e)
                {
                    return new BatteryReading(null, BatteryStatus.Error, e.NativeErrorCode == 5 || e.NativeErrorCode == 32 ? "设备通道被占用" : "设备读取失败");
                }
                catch (InvalidOperationException) { return new BatteryReading(null, BatteryStatus.Error, "电量应答无效"); }
            }
            return new BatteryReading(null, BatteryStatus.Disconnected, "接收器未连接");
        }
        private static BatteryReading ReadMouse(HidDevice device)
        {
            using (SafeFileHandle handle = NativeHid.Open(device.Path, true))
            {
                if (handle.IsInvalid) throw new Win32Exception();
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    byte[] request = BatteryProtocol.MouseRequest(device.FeatureLength);
                    if (!NativeHid.HidD_SetFeature(handle, request, request.Length)) throw new Win32Exception();
                    Thread.Sleep(100 + attempt * 50);
                    byte[] response = new byte[device.FeatureLength];
                    response[0] = 0x11;
                    if (!NativeHid.HidD_GetFeature(handle, response, response.Length)) throw new Win32Exception();
                    BatteryReading reading = BatteryProtocol.ParseMouse(response);
                    if (reading != null) return reading;
                    Thread.Sleep(150);
                }
                return new BatteryReading(null, BatteryStatus.Unknown, "鼠标休眠或未响应");
            }
        }
        private static BatteryReading ReadHeadset(HidDevice device)
        {
            using (SafeFileHandle handle = NativeHid.Open(device.Path, true))
            {
                if (handle.IsInvalid) throw new Win32Exception();
                Stopwatch deadline = Stopwatch.StartNew();
                NativeHid.Transfer(handle, BatteryProtocol.HeadsetRequest(), 0, 500);
                while (deadline.ElapsedMilliseconds < 1200)
                {
                    byte[] response = NativeHid.Transfer(handle, null, device.InputLength, (int)(1200 - deadline.ElapsedMilliseconds));
                    BatteryReading reading = BatteryProtocol.ParseHeadset(response);
                    if (reading != null) return reading;
                }
                throw new TimeoutException();
            }
        }
    }
}
