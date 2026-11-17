using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BatteryMonitor.Tests
{
    internal static class DeviceTests
    {
        private static BatteryDevice Device(DeviceSource source, string address, DeviceKind kind)
        {
            return new BatteryDevice { Id = BatteryDevice.MakeId(source, address), Name = "测试设备|含中文\n名称",
                Address = address, ContainerId = "", Source = source, Kind = kind };
        }
        public static int Worker(string[] args)
        {
            string mode = Environment.GetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER");
            if (mode == null) return Program.Main(args);
            if (args[0] == "--discover")
            {
                if (mode == "catalog" && args[1] == "windows") return 1;
                if (args[1] == "mchose")
                {
                    Console.WriteLine(Device(DeviceSource.MchoseMouse, "mouse", DeviceKind.Mouse).Encode());
                    Console.WriteLine(Device(DeviceSource.MchoseHeadset, "headset", DeviceKind.Audio).Encode());
                    Console.WriteLine(Device(DeviceSource.BluetoothLe, "keyboard", DeviceKind.Keyboard).Encode());
                }
                return 0;
            }
            BatteryDevice device = BatteryDevice.Decode(Console.ReadLine());
            if (mode == "catalog" && device.Address == "headset") System.Threading.Thread.Sleep(60000);
            Console.WriteLine(ReadingWire.Encode(new BatteryReading(device.Address == "mouse" ? 57 : 80, BatteryStatus.Online, "在线")));
            return 0;
        }
        public static void Run(Action<bool, string> check, string artifacts)
        {
            check(WindowsBattery.FromReported((byte)0, true).Percentage == 0, "reported zero is valid");
            check(WindowsBattery.FromReported(100, null).Status == BatteryStatus.Reported, "unknown connection is explicitly system reported");
            check(WindowsBattery.FromReported(80, null).Display(DateTime.UtcNow).Contains("系统报告"), "cached source is visible");
            check(!WindowsBattery.FromReported(80, false).Percentage.HasValue, "disconnected cache is hidden");
            foreach (object value in new object[] { null, true, "50", 50.5, -1, 101, (byte)255 })
                check(!WindowsBattery.FromReported(value, null).Percentage.HasValue, "invalid Windows percentage is rejected");
            check(NativeBluetooth.ParseValue(new byte[] { 0 }).Percentage == 0, "BLE zero is valid");
            check(NativeBluetooth.ParseValue(new byte[] { 100 }).Percentage == 100, "BLE full percentage");
            foreach (byte[] value in new byte[][] { null, new byte[0], new byte[] { 101 }, new byte[] { 255 }, new byte[] { 10, 20 } })
                check(!NativeBluetooth.ParseValue(value).Percentage.HasValue, "invalid BLE byte count or range");
            check(Marshal.SizeOf(typeof(NativeBluetooth.Uuid)) == 20 && Marshal.SizeOf(typeof(NativeBluetooth.Service)) == 24 &&
                Marshal.SizeOf(typeof(NativeBluetooth.Characteristic)) == 36, "Windows BLE ABI structure sizes");
            check(Marshal.OffsetOf(typeof(NativeBluetooth.Characteristic), "Uuid").ToInt32() == 4 &&
                Marshal.OffsetOf(typeof(NativeBluetooth.Characteristic), "Readable").ToInt32() == 29, "Windows BLE ABI field offsets");
            check(new NativeBluetooth.Uuid { IsShort = 1, Short = 0x180F }.Is(0x180F), "short BLE battery UUID");
            check(new NativeBluetooth.Uuid { Long = new Guid("00002a19-0000-1000-8000-00805f9b34fb") }.Is(0x2A19), "long BLE battery UUID");
            BatteryDevice mouse = Device(DeviceSource.MchoseMouse, "mouse", DeviceKind.Mouse);
            BatteryDevice audio = Device(DeviceSource.MchoseHeadset, "headset", DeviceKind.Audio);
            BatteryDevice clone = BatteryDevice.Decode(mouse.Encode());
            check(clone.Id == mouse.Id && clone.Name == mouse.Name && clone.Address == mouse.Address, "descriptor delimiters and Unicode roundtrip");
            check(mouse.Id == BatteryDevice.MakeId(mouse.Source, "MOUSE"), "Windows path identity ignores casing");
            foreach (string line in new[] { "", mouse.Encode().Replace("D1|0|2", "D1|99|2"), mouse.Encode() + "|extra", "D1|0|2|?|||" })
            {
                bool rejected = false;
                try { BatteryDevice.Decode(line); } catch (InvalidDataException) { rejected = true; }
                check(rejected, "invalid device descriptor is rejected");
            }
            BatterySnapshot snapshot = new BatterySnapshot();
            snapshot.Devices.Add(new DeviceSample { Device = mouse, Reading = new BatteryReading(50, BatteryStatus.Online, "在线") });
            snapshot.Devices.Add(new DeviceSample { Device = audio, Reading = new BatteryReading(80, BatteryStatus.Online, "在线") });
            snapshot.Devices.Add(snapshot.Devices[0]);
            snapshot.Deduplicate();
            check(snapshot.Devices.Count == 2, "duplicate descriptors merge while identical model names remain separate");
            string left, right;
            BatteryPair pair = snapshot.Pair("", "", out left, out right);
            check(pair.Left.Percentage == 80 && pair.Right.Percentage == 50, "automatic audio-left and mouse-right defaults");
            pair = snapshot.Pair("", audio.Id, out left, out right);
            check(pair.Left.Percentage == 50 && pair.Right.Percentage == 80, "automatic selection reserves explicit other side");
            pair = snapshot.Pair(BatteryDevice.MakeId(DeviceSource.Windows, "missing"), "", out left, out right);
            check(!pair.Left.Percentage.HasValue, "explicit missing device is never substituted");
            snapshot.Devices.RemoveAt(1);
            pair = snapshot.Pair("", "", out left, out right);
            check(!pair.Left.Percentage.HasValue && pair.Right.Percentage.HasValue, "single mouse keeps the right slot and is not duplicated");

            string settingsPath = Path.Combine(artifacts, "selection-tests", "selection.txt");
            SelectionStore store = new SelectionStore(settingsPath) { LeftId = audio.Id, RightId = mouse.Id };
            store.Save();
            string settings = File.ReadAllText(settingsPath);
            check(!settings.Contains("headset") && !settings.Contains("mouse"), "saved choices contain hashes instead of paths");
            SelectionStore restored = new SelectionStore(settingsPath);
            restored.Load();
            check(restored.LeftId == audio.Id && restored.RightId == mouse.Id, "selection persistence roundtrip");
            File.WriteAllText(settingsPath, "BatteryMonitor.Selection.1\nnot-a-device-id\n");
            restored = new SelectionStore(settingsPath);
            restored.Load();
            check(restored.LeftId == "" && restored.RightId == "", "corrupt settings fall back to automatic selection");

            string previous = Environment.GetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER");
            try
            {
                Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", "catalog");
                string runner = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                snapshot = DeviceQueries.Read(runner);
                check(snapshot.Devices.Count == 3 && snapshot.Warnings.Count == 1, "failed provider does not hide other devices");
                check(snapshot.Devices.Find(s => s.Device.Address == "headset").Reading.Percentage == null &&
                    snapshot.Devices.Find(s => s.Device.Address == "mouse").Reading.Percentage == 57 &&
                    snapshot.Devices.Find(s => s.Device.Address == "keyboard").Reading.Percentage == 80,
                    "hung device preserves earlier results and queries subsequent devices");
                check(timer.ElapsedMilliseconds >= 5000 && timer.ElapsedMilliseconds < 12000, "per-device native query timeout is bounded");
            }
            finally { Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", previous); }
        }
    }
}
