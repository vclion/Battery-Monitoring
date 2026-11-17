using System;
using System.Threading;
using System.Windows.Forms;

namespace BatteryMonitor
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
                try
                {
                    if (args.Length == 2 && args[0] == "--discover")
                    {
                        foreach (BatteryDevice device in DeviceCatalog.Discover(args[1])) Console.WriteLine(device.Encode());
                        return 0;
                    }
                    if (args.Length == 1 && args[0] == "--read-device")
                    {
                        Console.WriteLine(ReadingWire.Encode(DeviceCatalog.Read(BatteryDevice.ReadRequest(Console.OpenStandardInput()))));
                        return 0;
                    }
                    if (args.Length == 1 && args[0] == "--list")
                    {
                        BatterySnapshot snapshot = DeviceQueries.Read(Application.ExecutablePath);
                        foreach (DeviceSample sample in snapshot.Devices)
                            Console.WriteLine(sample.Device.Name.Replace("\r", " ").Replace("\n", " ") + " | " +
                                sample.Device.SourceName + " | " + sample.Reading.Display(DateTime.UtcNow));
                        foreach (string warning in snapshot.Warnings) Console.WriteLine("提示：" + warning);
                        if (snapshot.Devices.Count == 0) Console.WriteLine("未发现支持电量读取的设备。");
                        return snapshot.Warnings.Count == 0 ? 0 : 1;
                    }
                    if (args.Length == 2 && args[0] == "--export-icon")
                    {
                        using (var icon = RingIcon.Create(BatteryPair.Unavailable("点击查询电量"), true, DateTime.UtcNow, 64))
                        using (var file = System.IO.File.Create(args[1])) icon.Save(file);
                        return 0;
                    }
                    if (!(args.Length == 1 && args[0] == "--read")) return 2;
                }
                catch { return 1; }
            }
            if (args.Length == 1 && args[0] == "--read")
            {
                Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
                try
                {
                    var devices = NativeHid.Enumerate();
                    Console.WriteLine(ReadingWire.Encode(DeviceReader.Read(devices, true)));
                    Console.WriteLine(ReadingWire.Encode(DeviceReader.Read(devices, false)));
                    return 0;
                }
                catch
                {
                    var error = new BatteryReading(null, BatteryStatus.Error, "无法查询设备");
                    Console.WriteLine(ReadingWire.Encode(error));
                    Console.WriteLine(ReadingWire.Encode(error));
                    return 1;
                }
            }
            bool first;
            using (Mutex instance = new Mutex(true, @"Local\BatteryMonitor.DualRing.01", out first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (TrayApplication tray = new TrayApplication()) Application.Run(tray);
                instance.ReleaseMutex();
            }
            return 0;
        }
    }
}
