using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using BatteryMonitor;

namespace BatteryMonitor.Tests
{
    internal static class TestRunner
    {
        private static int checks;
        private static void Check(bool condition, string name)
        {
            checks++;
            if (!condition) throw new Exception("FAIL: " + name);
        }
        private static byte[] Mouse(int level, int flags, int charge)
        {
            byte[] reply = new byte[65];
            for (int i = 1; i < reply.Length; i++) reply[i] = 0xFF;
            reply[0] = 0x11; reply[1] = 0xF9;
            reply[2] = 0xAC; reply[3] = 0xAD;
            reply[10] = (byte)(flags ^ 0xFF); reply[11] = (byte)(level ^ 0xFF); reply[12] = (byte)(charge ^ 0xFF);
            return reply;
        }
        [STAThread]
        public static int Main(string[] args)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            try
            {
                if (args.Length == 1 && args[0] == "--hardware") return Hardware();
                if (args.Length == 1 && args[0] == "--tray-smoke") return TraySmoke();
                if (args.Length > 0 && (args[0] == "--discover" || args[0] == "--read-device"))
                    return DeviceTests.Worker(args);
                if (args.Length == 1 && args[0] == "--read")
                {
                    string mode = Environment.GetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER");
                    if (mode == null)
                    {
                        var devices = NativeHid.Enumerate();
                        Console.WriteLine(ReadingWire.Encode(DeviceReader.Read(devices, true)));
                        Console.WriteLine(ReadingWire.Encode(DeviceReader.Read(devices, false)));
                        return 0;
                    }
                    if (mode == "hang") System.Threading.Thread.Sleep(60000);
                    if (mode == "malformed") { Console.WriteLine("not a battery sample"); return 0; }
                    Console.WriteLine(ReadingWire.Encode(new BatteryReading(65, BatteryStatus.Online, "在线")));
                    Console.WriteLine(ReadingWire.Encode(new BatteryReading(57, BatteryStatus.Online, "在线")));
                    return 0;
                }
                DateTime now = DateTime.UtcNow;
                Check(BatteryProtocol.ParseHeadset(null) == null, "null frame");
                Check(BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 50}) == null, "short headset frame");
                Check(BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x11, 50, 2}) == null, "unrelated report");
                Check(BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 101, 2}) == null, "out-of-range battery");
                Check(BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 0, 2}).Percentage == 0, "zero is valid");
                Check(BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 65, 3}).Status == BatteryStatus.Charging, "charging flag");
                Check(BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 100, 4}).Status == BatteryStatus.Full, "full flag");
                Check(!BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 70, 26}).Percentage.HasValue, "sleep must not expose old battery");
                Check(!BatteryProtocol.ParseHeadset(new byte[] {0x55, 0x65, 70, 0}).Percentage.HasValue, "unknown status must not expose battery");
                Check(BatteryProtocol.ParseMouse(null) == null, "null mouse frame");
                Check(BatteryProtocol.ParseMouse(new byte[65]) == null, "empty response");
                Check(BatteryProtocol.ParseMouse(Mouse(85, 9, 0)).Percentage == 85, "mouse battery offset");
                Check(BatteryProtocol.ParseMouse(Mouse(0, 9, 0)).Percentage == 0, "zero mouse battery");
                Check(BatteryProtocol.ParseMouse(Mouse(35, 9, 1)).Status == BatteryStatus.Charging, "mouse charging offset");
                Check(!BatteryProtocol.ParseMouse(Mouse(80, 1, 0)).Percentage.HasValue, "disconnected mouse must hide battery");
                Check(BatteryProtocol.ParseMouse(Mouse(255, 9, 0)) == null, "invalid mouse level");
                byte[] echo = Mouse(80, 9, 0); echo[1] = 0xFC;
                Check(BatteryProtocol.ParseMouse(echo) == null, "wrong mouse command echo");
                echo = Mouse(80, 9, 0); echo[2] = 0xFF;
                Check(BatteryProtocol.ParseMouse(echo) == null, "wrong vendor identity");
                byte[] request = BatteryProtocol.MouseRequest(65);
                Check(request.Length == 65 && request[0] == 0x11 && request[1] == 0xF9 && request[64] == 0xFF, "read-only mouse command");
                request = BatteryProtocol.HeadsetRequest();
                Check(request.Length == 64 && request[0] == 0x55 && request[1] == 0x65 && request[2] == 1 && request[63] == 0, "read-only headset command");
                BatteryReading old = new BatteryReading(65, BatteryStatus.Online, "在线", now.AddMinutes(-3));
                Check(old.IsValid(now) && old.Display(now) == "65%", "manual mode retains the last queried sample");
                BatteryReading fresh = new BatteryReading(65, BatteryStatus.Online, "在线", now);
                Check(fresh.IsValid(now) && fresh.Display(now) == "65%", "fresh battery");
                Check(!new BatteryReading(65, BatteryStatus.Online, "在线", now.AddMinutes(10)).IsValid(now), "future timestamps are not fresh samples");
                Check(!new BatteryReading(null, BatteryStatus.Unknown, "未知").Display(now).Contains("0%"), "unknown is not empty battery");
                BatteryReading roundtrip = ReadingWire.Decode(ReadingWire.Encode(fresh));
                Check(roundtrip.Percentage == fresh.Percentage && roundtrip.ObservedAtUtc == fresh.ObservedAtUtc && roundtrip.Detail == "在线", "unicode wire roundtrip");
                foreach (string bad in new[] { "", "101|1|x|1", "0|99|x|1", "0|1|x|-1", "bad|1|x|1", "1|1|x|9999999999999999999" })
                {
                    bool rejected = false;
                    try { ReadingWire.Decode(bad); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected, "malformed worker output");
                }
                Check(RingIcon.LevelColor(old, false, now) == RingIcon.LevelColor(fresh, false, now), "retained sample keeps its ring until the next query");
                Check(RingIcon.LevelColor(new BatteryReading(10, BatteryStatus.Online, "在线"), false, DateTime.UtcNow) == Color.Black &&
                    RingIcon.LevelColor(new BatteryReading(90, BatteryStatus.Online, "在线"), true, DateTime.UtcNow) == Color.Black, "all battery levels and themes use black");
                HidDevice wrong = new HidDevice { VendorId = 0x291D, ProductId = 0x385D, UsagePage = 0xFF82, Usage = 1, InputLength = 64, OutputLength = 64 };
                Check(!DeviceReader.IsHeadset(wrong), "do not touch other HID collections");
                string artifacts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
                Directory.CreateDirectory(artifacts);
                Preview(artifacts, now);
                using (Icon desktopIcon = RingIcon.Create(new BatteryPair { Left = fresh, Right = fresh }, true, now, 64))
                using (FileStream iconFile = File.Create(Path.Combine(artifacts, "BatteryMonitor.ico"))) desktopIcon.Save(iconFile);
                using (Bitmap bitmap = RingIcon.Render(64, new BatteryPair { Left = fresh, Right = fresh }, false, now))
                    Check(bitmap.GetPixel(32, 32).A == 0 && bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(18, 38).A > 200 && bitmap.GetPixel(46, 38).A > 200, "black device fills have a transparent middle gap and outer corners");
                BatteryPair pair = new BatteryPair { Left = fresh, Right = new BatteryReading(10, BatteryStatus.Online, "在线") };
                foreach (int size in new[] {16, 20, 24, 32, 40, 48, 64})
                    using (Icon icon = RingIcon.Create(pair, false, DateTime.UtcNow, size))
                        Check(icon.Width == size && icon.Height == size && icon.Handle != IntPtr.Zero, "ICO load " + size);
                string oldMode = Environment.GetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER");
                DeviceTests.Run(Check, artifacts);
                try
                {
                    string runner = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", "valid");
                    BatteryPair result = PollProcess.Read(runner);
                    Check(result.Left.Percentage == 65 && result.Right.Percentage == 57 && result.Left.Detail == "在线", "real worker pipe and UTF-8 decoding");
                    result = PollProcess.Read(runner);
                    Check(result.Left.Percentage == 65 && result.Right.Percentage == 57, "completed worker releases slot for the next poll");
                    Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", "malformed");
                    Check(!PollProcess.Read(runner).Left.Percentage.HasValue, "invalid worker output is unavailable");
                    Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", "hang");
                    var deadline = System.Diagnostics.Stopwatch.StartNew();
                    result = PollProcess.Read(runner);
                    Check(!result.Left.Percentage.HasValue && deadline.ElapsedMilliseconds >= 5000 && deadline.ElapsedMilliseconds < 8500, "hung worker is bounded and not shown as zero");
                    PollProcess.Stop();
                    deadline.Restart();
                    result = PollProcess.Read(runner);
                    Check(!result.Left.Percentage.HasValue && deadline.ElapsedMilliseconds < 1000, "shutdown prevents another worker from starting");
                }
                finally { Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", oldMode); }
                PollProcess.Stop();
                Check(!PollProcess.Read(System.Reflection.Assembly.GetExecutingAssembly().Location).Left.Percentage.HasValue, "shutdown prevents new query processes");
                Console.WriteLine("PASS: " + checks + " checks. Icon preview: tests/artifacts/icon-preview.png");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }

        private static int TraySmoke()
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            Exception failure = null;
            string previousMode = Environment.GetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER");
            Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", "ui-catalog");
            string selectionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts", "tray-selection.txt");
            using (TrayApplication context = new TrayApplication(new SelectionStore(selectionPath)))
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var tray = (System.Windows.Forms.NotifyIcon)typeof(TrayApplication).GetField("tray", flags).GetValue(context);
                var menu = (System.Windows.Forms.ContextMenuStrip)typeof(TrayApplication).GetField("menu", flags).GetValue(context);
                Check(tray.Visible && tray.Icon != null, "real notification icon is registered");
                Check(menu.Items.Count == 7, "query, device list, left/right selection, startup and exit menu commands");
                var query = (System.Windows.Forms.ToolStripMenuItem)menu.Items[0];
                var pairField = typeof(TrayApplication).GetField("pair", flags);
                var snapshotField = typeof(TrayApplication).GetField("snapshot", flags);
                BatterySnapshot first = null;
                int stage = 0;
                long sampledAt = 0;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine("Manual tray test started; checking query button and no periodic polling...");
                timer.Interval = 500;
                timer.Tick += delegate
                {
                    try
                    {
                        BatteryPair current = (BatteryPair)pairField.GetValue(context);
                        if (stage == 0)
                        {
                            Check(!current.Left.Percentage.HasValue && !current.Right.Percentage.HasValue, "launch does not query devices");
                            query.PerformClick();
                            Check(!query.Enabled, "query button is disabled while reading");
                            stage = 1;
                        }
                        else if (stage == 1 && query.Enabled)
                        {
                            Check(current.Left.IsValid(DateTime.UtcNow), "manual query reads worker result");
                            var chooseLeft = (System.Windows.Forms.ToolStripMenuItem)menu.Items[2];
                            ((System.Windows.Forms.ToolStripMenuItem)chooseLeft.DropDownItems[1]).PerformClick();
                            SelectionStore saved = new SelectionStore(selectionPath);
                            saved.Load();
                            Check(saved.LeftId.Length > 0, "real device menu persists the selected device");
                            current = (BatteryPair)pairField.GetValue(context);
                            Check(current.Left.Percentage == 57, "selecting the left device updates the actual rendered pair");
                            first = (BatterySnapshot)snapshotField.GetValue(context);
                            sampledAt = elapsed.ElapsedMilliseconds;
                            Console.WriteLine("Manual query: " + tray.Text);
                            stage = 2;
                        }
                        else if (stage == 2 && elapsed.ElapsedMilliseconds - sampledAt >= 31000)
                        {
                            Check(ReferenceEquals(snapshotField.GetValue(context), first), "no automatic query across the former 30-second interval");
                            Console.WriteLine("PASS: cached reading unchanged after 31 seconds.");
                            query.PerformClick();
                            stage = 3;
                        }
                        else if (stage == 3 && query.Enabled)
                        {
                            Check(!ReferenceEquals(snapshotField.GetValue(context), first), "second manual query obtains a new sample");
                            Console.WriteLine("Manual re-query: " + tray.Text);
                            ((System.Windows.Forms.ToolStripMenuItem)menu.Items[menu.Items.Count - 1]).PerformClick();
                            Check(!tray.Visible, "exit removes notification icon");
                            Console.WriteLine("PASS: Exit menu command ends the loop and removes the icon.");
                            timer.Stop();
                        }
                        if (elapsed.ElapsedMilliseconds > 50000)
                            throw new Exception("Manual tray test exceeded its deadline.");
                    }
                    catch (Exception e)
                    {
                        failure = e;
                        timer.Stop();
                        context.ExitThread();
                    }
                };
                timer.Start();
                System.Windows.Forms.Application.Run(context);
            }
            Environment.SetEnvironmentVariable("BATTERY_MONITOR_TEST_WORKER", previousMode);
            if (failure != null) throw failure;
            Console.WriteLine("PASS: real tray smoke test.");
            return 0;
        }

        private static int Hardware()
        {
            var devices = NativeHid.Enumerate();
            foreach (HidDevice device in devices)
                if (DeviceReader.IsHeadset(device) || DeviceReader.IsMouse(device)) Console.WriteLine("Candidate " + device);
            int valid = 0;
            for (int i = 0; i < 3; i++)
            {
                BatteryReading headset = DeviceReader.Read(devices, true);
                BatteryReading mouse = DeviceReader.Read(devices, false);
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " headset: " + ReadingWire.Encode(headset));
                Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " mouse: " + ReadingWire.Encode(mouse));
                if (headset.IsValid(DateTime.UtcNow) && mouse.IsValid(DateTime.UtcNow)) valid++;
                if (i < 2) System.Threading.Thread.Sleep(1200);
            }
            Console.WriteLine("Valid paired readings: " + valid + "/3");
            return valid == 3 ? 0 : 1;
        }

        private static void Preview(string output, DateTime now)
        {
            int[] levels = {100, 65, 20, 10, 0, -1};
            using (Bitmap sheet = new Bitmap(660, 280))
            using (Graphics g = Graphics.FromImage(sheet))
            using (Font title = new Font("Segoe UI", 10))
            {
                g.Clear(Color.FromArgb(18, 23, 32));
                for (int theme = 0; theme < 2; theme++)
                {
                    bool light = theme == 1;
                    Rectangle block = new Rectangle(0, theme * 140, 660, 140);
                    using (Brush bg = new SolidBrush(light ? Color.FromArgb(243, 244, 247) : Color.FromArgb(24, 29, 39))) g.FillRectangle(bg, block);
                    using (Brush fg = new SolidBrush(light ? Color.FromArgb(40, 50, 65) : Color.FromArgb(230, 235, 242)))
                    {
                        g.DrawString(light ? "Light taskbar / actual 16 px + enlarged" : "Dark taskbar / actual 16 px + enlarged", title, fg, 14, block.Y + 10);
                        for (int col = 0; col < levels.Length; col++)
                        {
                            int x = 20 + col * 106;
                            BatteryReading a = levels[col] < 0 ? new BatteryReading(null, BatteryStatus.Unknown, "未知") : new BatteryReading(levels[col], BatteryStatus.Online, "在线", now);
                            BatteryPair pair = new BatteryPair { Left = a, Right = new BatteryReading(85, BatteryStatus.Online, "在线", now) };
                            using (Bitmap small = RingIcon.Render(16, pair, light, now)) g.DrawImageUnscaled(small, x, block.Y + 47);
                            using (Bitmap large = RingIcon.Render(64, pair, light, now)) g.DrawImageUnscaled(large, x + 18, block.Y + 28);
                            g.DrawString(levels[col] < 0 ? "Unknown / 85" : levels[col] + "% / 85%", title, fg, x, block.Y + 101);
                        }
                    }
                }
                sheet.Save(Path.Combine(output, "icon-preview.png"), ImageFormat.Png);
            }
        }
    }
}
