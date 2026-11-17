using System;
using System.Collections.Generic;
using System.IO;

namespace BatteryMonitor
{
    internal static class DeviceQueries
    {
        public static BatterySnapshot Read(string executable)
        {
            var result = new BatterySnapshot();
            var found = new List<BatteryDevice>();
            foreach (string provider in new[] { "mchose", "windows", "pnp", "ble" })
            {
                try
                {
                    string text = PollProcess.Run(executable, "--discover " + provider, null);
                    foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        found.Add(BatteryDevice.Decode(line));
                }
                catch (Exception e)
                {
                    if (!Recoverable(e)) throw;
                    result.Warnings.Add(provider + "：设备发现失败或超时");
                }
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (BatteryDevice device in found)
            {
                if (!unique.Add(device.Id)) continue;
                if (result.Devices.Count >= 32) { result.Warnings.Add("本次最多查询 32 个设备"); break; }
                BatteryReading reading;
                try { reading = ReadingWire.Decode(PollProcess.Run(executable, "--read-device", device.Encode()).Trim()); }
                catch (Exception e)
                {
                    if (!Recoverable(e)) throw;
                    reading = new BatteryReading(null, BatteryStatus.Error, e is TimeoutException ? "设备读取超时" : "设备读取失败");
                }
                result.Devices.Add(new DeviceSample { Device = device, Reading = reading });
            }
            result.Deduplicate();
            result.QueriedAtUtc = DateTime.UtcNow;
            return result;
        }
        private static bool Recoverable(Exception e)
        {
            return e is IOException || e is InvalidDataException || e is TimeoutException || e is InvalidOperationException ||
                e is System.ComponentModel.Win32Exception || e is AggregateException;
        }
    }
}
