using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BatteryMonitor
{
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem startup = new ToolStripMenuItem("开机启动");
        private readonly ToolStripMenuItem query = new ToolStripMenuItem("查询电量");
        private readonly ToolStripMenuItem devices = new ToolStripMenuItem("设备电量");
        private readonly ToolStripMenuItem left = new ToolStripMenuItem("左半圆显示");
        private readonly ToolStripMenuItem right = new ToolStripMenuItem("右半圆显示");
        private readonly SelectionStore settings;
        private readonly DeviceEvents events = new DeviceEvents();
        private BatterySnapshot snapshot = new BatterySnapshot();
        private BatteryPair pair = BatteryPair.Unavailable("点击查询电量");
        private Icon icon;
        private bool busy, closing;
        private string renderKey;
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupName = "BatteryMonitorDualRing";

        public TrayApplication() : this(SelectionStore.ForCurrentUser()) { }
        internal TrayApplication(SelectionStore selection)
        {
            settings = selection;
            settings.Load();
            startup.Checked = StartupEnabled();
            startup.Click += ToggleStartup;
            query.Click += delegate { Poll(); };
            menu.Items.Add(query);
            menu.Items.Add(devices);
            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Poll(); };
            menu.Opening += delegate { startup.Checked = StartupEnabled(); };
            RefreshDeviceMenus();
            RefreshIcon();
            // Explorer may keep the initial tooltip as the app's accessible title.
            tray.Text = "电量监控";
            tray.Visible = true;
            RefreshIcon();
            events.Changed += delegate { renderKey = null; RefreshIcon(); };
        }

        private void RefreshDeviceMenus()
        {
            ClearItems(devices);
            ClearItems(left);
            ClearItems(right);
            AddSelection(left, true, null);
            AddSelection(right, false, null);
            DateTime now = DateTime.UtcNow;
            foreach (DeviceSample sample in snapshot.Devices)
            {
                string label = sample.Device.Name.Replace("&", "&&").Replace("\r", " ").Replace("\n", " ") +
                    " [" + sample.Device.SourceName + "]：" + sample.Reading.Display(now);
                devices.DropDownItems.Add(new ToolStripMenuItem(label) { Enabled = false,
                    ToolTipText = sample.Reading.Detail });
                AddSelection(left, true, sample.Device);
                AddSelection(right, false, sample.Device);
            }
            if (snapshot.Devices.Count == 0)
                devices.DropDownItems.Add(new ToolStripMenuItem(snapshot.QueriedAtUtc.HasValue ? "未发现可读取的设备" : "请先点击查询电量") { Enabled = false });
            foreach (string warning in snapshot.Warnings)
                devices.DropDownItems.Add(new ToolStripMenuItem(warning) { Enabled = false });
            devices.DropDownItems.Add(new ToolStripMenuItem("系统报告可能为缓存，设备更新时间未知") { Enabled = false });
        }

        private static void ClearItems(ToolStripMenuItem menuItem)
        {
            while (menuItem.DropDownItems.Count > 0)
            {
                ToolStripItem child = menuItem.DropDownItems[0];
                menuItem.DropDownItems.RemoveAt(0);
                child.Dispose();
            }
        }

        private void AddSelection(ToolStripMenuItem target, bool isLeft, BatteryDevice device)
        {
            string id = device == null ? "" : device.Id;
            string current = isLeft ? settings.LeftId : settings.RightId;
            string other = isLeft ? settings.RightId : settings.LeftId;
            var item = new ToolStripMenuItem(device == null ? "自动选择" :
                device.Name.Replace("&", "&&").Replace("\r", " ").Replace("\n", " ") + " [" + device.SourceName + "]") {
                Checked = current == id, Enabled = id.Length == 0 || id != other };
            item.Click += delegate
            {
                string previous = isLeft ? settings.LeftId : settings.RightId;
                if (isLeft) settings.LeftId = id; else settings.RightId = id;
                try { settings.Save(); }
                catch (Exception e)
                {
                    if (!(e is System.IO.IOException || e is UnauthorizedAccessException)) throw;
                    if (isLeft) settings.LeftId = previous; else settings.RightId = previous;
                    MessageBox.Show("无法保存设备选择，请检查当前用户目录权限。", "电量监控");
                }
                RefreshDeviceMenus();
                RefreshIcon();
            };
            target.DropDownItems.Add(item);
            if (device == null && current.Length != 0 && !snapshot.Devices.Exists(s => s.Device.Id == current))
                target.DropDownItems.Add(new ToolStripMenuItem("已选设备暂未发现；保留选择") { Checked = true, Enabled = false });
        }

        private async void Poll()
        {
            if (busy || closing) return;
            busy = true;
            query.Enabled = false;
            query.Text = "正在查询…";
            try
            {
                BatterySnapshot result = await Task.Run(() => DeviceQueries.Read(Application.ExecutablePath));
                if (!closing) { snapshot = result; RefreshDeviceMenus(); RefreshIcon(); }
            }
            catch (Exception)
            {
                if (!closing)
                {
                    snapshot = new BatterySnapshot { QueriedAtUtc = DateTime.UtcNow };
                    snapshot.Warnings.Add("读取失败，请点击重试");
                    RefreshDeviceMenus(); RefreshIcon();
                }
            }
            finally
            {
                busy = false;
                if (!closing) { query.Text = "查询电量"; query.Enabled = true; }
            }
        }

        private void RefreshIcon()
        {
            DateTime now = DateTime.UtcNow;
            bool light = false;
            string leftName, rightName;
            pair = snapshot.Pair(settings.LeftId, settings.RightId, out leftName, out rightName);
            string text = ShortName(leftName) + "：" + ShortReading(pair.Left, now) + "\n" +
                ShortName(rightName) + "：" + ShortReading(pair.Right, now);
            if (snapshot.QueriedAtUtc.HasValue)
                text += "\n查询 " + snapshot.QueriedAtUtc.Value.ToLocalTime().ToString("MM-dd HH:mm:ss");
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            string key = light + ":" + pair.Left.IsValid(now) + ":" + pair.Left.Percentage + ":" +
                pair.Right.IsValid(now) + ":" + pair.Right.Percentage + ":" + SystemInformation.SmallIconSize.Width;
            if (key == renderKey) return;
            Icon replacement = RingIcon.Create(pair, light, now, SystemInformation.SmallIconSize.Width);
            tray.Icon = replacement;
            if (icon != null) icon.Dispose();
            icon = replacement;
            renderKey = key;
        }

        private static string ShortName(string name)
        {
            name = name.Replace("\r", " ").Replace("\n", " ");
            return name.Length > 9 ? name.Substring(0, 8) + "…" : name;
        }
        private static string ShortReading(BatteryReading reading, DateTime now)
        {
            if (reading.IsValid(now)) return reading.Percentage + "%" + (reading.Status == BatteryStatus.Reported ? "(系统)" :
                reading.Status == BatteryStatus.Charging ? "(充电)" : reading.Status == BatteryStatus.Full ? "(满)" : "");
            return "--(" + (reading.Detail.Length > 8 ? reading.Detail.Substring(0, 8) : reading.Detail) + ")";
        }

        private static bool StartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && string.Equals(key.GetValue(StartupName) as string, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleStartup(object sender, EventArgs e)
        {
            try
            {
                bool enable = !StartupEnabled();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enable) key.SetValue(StartupName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(StartupName, false);
                }
                startup.Checked = StartupEnabled();
            }
            catch (Exception)
            {
                MessageBox.Show("无法修改开机启动设置，请检查当前用户权限。", "电量监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void ExitThreadCore()
        {
            closing = true;
            PollProcess.Stop();
            tray.Visible = false;
            base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing = true;
                PollProcess.Stop();
                events.Dispose(); tray.Dispose(); menu.Dispose();
                if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class DeviceEvents : NativeWindow, IDisposable
        {
            private const int RefreshMessage = 0x8001;
            public event Action Changed;
            public DeviceEvents() { CreateHandle(new CreateParams { Caption = "BatteryMonitor.Events" }); }
            public void ScheduleRefresh() { PostMessage(Handle, RefreshMessage, IntPtr.Zero, IntPtr.Zero); }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x219 || m.Msg == 0x1A || m.Msg == 0x2E0 || m.Msg == RefreshMessage)
                { if (Changed != null) Changed(); }
                base.WndProc(ref m);
            }
            public void Dispose() { DestroyHandle(); }
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        }
    }
}
