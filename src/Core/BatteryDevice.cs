using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BatteryMonitor
{
    internal enum DeviceKind { Other, Audio, Mouse, Keyboard }
    internal enum DeviceSource { MchoseMouse, MchoseHeadset, Windows, BluetoothLe }

    internal sealed class BatteryDevice
    {
        public string Id, Name, Address, ContainerId;
        public DeviceKind Kind;
        public DeviceSource Source;

        public static string MakeId(DeviceSource source, string address)
        {
            using (SHA256 hash = SHA256.Create())
                return ((int)source).ToString() + ":" + BitConverter.ToString(hash.ComputeHash(
                    Encoding.UTF8.GetBytes(address.ToUpperInvariant()))).Replace("-", "").Substring(0, 32);
        }

        public string SourceName
        {
            get
            {
                switch (Source)
                {
                    case DeviceSource.Windows: return "Windows 系统报告";
                    case DeviceSource.BluetoothLe: return "蓝牙 BLE 标准服务";
                    default: return "迈从 2.4G";
                }
            }
        }

        // Internal worker protocol only. Device paths never enter the settings or public list.
        public string Encode()
        {
            return string.Join("|", new[] { "D1", ((int)Source).ToString(), ((int)Kind).ToString(),
                Pack(Id), Pack(Name), Pack(Address), Pack(ContainerId ?? "") });
        }

        public static BatteryDevice Decode(string line)
        {
            if (line == null || line.Length > 32768) throw new InvalidDataException();
            string[] fields = line.Split('|');
            int source, kind;
            if (fields.Length != 7 || fields[0] != "D1" || !int.TryParse(fields[1], out source) ||
                !int.TryParse(fields[2], out kind) || !Enum.IsDefined(typeof(DeviceSource), source) ||
                !Enum.IsDefined(typeof(DeviceKind), kind)) throw new InvalidDataException();
            try
            {
                BatteryDevice device = new BatteryDevice { Source = (DeviceSource)source, Kind = (DeviceKind)kind,
                    Id = Unpack(fields[3]), Name = Unpack(fields[4]), Address = Unpack(fields[5]), ContainerId = Unpack(fields[6]) };
                if (device.Address.Length == 0 || device.Name.Length == 0 ||
                    device.Id != MakeId(device.Source, device.Address)) throw new InvalidDataException();
                return device;
            }
            catch (FormatException) { throw new InvalidDataException(); }
        }

        private static string Pack(string text) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
        private static string Unpack(string text) { return Encoding.UTF8.GetString(Convert.FromBase64String(text)); }
    }

    internal sealed class DeviceSample
    {
        public BatteryDevice Device;
        public BatteryReading Reading;
    }

    internal sealed class BatterySnapshot
    {
        public readonly List<DeviceSample> Devices = new List<DeviceSample>();
        public readonly List<string> Warnings = new List<string>();
        public DateTime? QueriedAtUtc;

        public BatteryPair Pair(string leftId, string rightId, out string leftName, out string rightName)
        {
            // Reserve both preferred types before filling empty automatic slots.
            DeviceSample left = string.IsNullOrEmpty(leftId)
                ? Devices.Find(s => s.Device.Kind == DeviceKind.Audio && s.Device.Id != rightId)
                : Devices.Find(s => s.Device.Id == leftId);
            DeviceSample right = string.IsNullOrEmpty(rightId)
                ? Devices.Find(s => s.Device.Kind == DeviceKind.Mouse && (left == null || s.Device.Id != left.Device.Id))
                : Devices.Find(s => s.Device.Id == rightId);
            if (left == null && string.IsNullOrEmpty(leftId))
                left = Devices.Find(s => s.Device.Id != rightId && (right == null || s.Device.Id != right.Device.Id));
            if (right == null && string.IsNullOrEmpty(rightId))
                right = Devices.Find(s => left == null || s.Device.Id != left.Device.Id);
            leftName = left == null ? "左侧设备" : left.Device.Name;
            rightName = right == null ? "右侧设备" : right.Device.Name;
            string missing = QueriedAtUtc.HasValue ? "未发现设备，请重新查询" : "点击查询电量";
            return new BatteryPair {
                Left = left == null ? new BatteryReading(null, BatteryStatus.Unknown, missing) : left.Reading,
                Right = right == null ? new BatteryReading(null, BatteryStatus.Unknown, missing) : right.Reading };
        }

        // Keep different sources selectable: a cached property and a direct BLE sample can
        // disagree. Their persistent IDs must not disappear when another source succeeds.
        // Never merge merely by name; multiple identical models must remain selectable.
        public void Deduplicate()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Devices.RemoveAll(s => !ids.Add(s.Device.Id));
            Devices.Sort((a, b) => { int result = a.Device.Source.CompareTo(b.Device.Source);
                if (result == 0) result = StringComparer.OrdinalIgnoreCase.Compare(a.Device.Name, b.Device.Name);
                return result != 0 ? result : StringComparer.Ordinal.Compare(a.Device.Id, b.Device.Id); });
        }
    }
}
