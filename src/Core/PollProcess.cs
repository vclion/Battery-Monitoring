using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace BatteryMonitor
{
    internal static class ReadingWire
    {
        public static string Encode(BatteryReading reading)
        {
            return (reading.Percentage.HasValue ? reading.Percentage.Value.ToString(CultureInfo.InvariantCulture) : "") + "|" +
                (int)reading.Status + "|" + reading.Detail.Replace("|", " ").Replace("\r", " ").Replace("\n", " ") + "|" +
                reading.ObservedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }
        public static BatteryReading Decode(string line)
        {
            if (line == null) throw new InvalidDataException();
            string[] parts = line.Split('|');
            int percentage, status;
            long ticks;
            if (parts.Length != 4 || !int.TryParse(parts[1], out status) || !Enum.IsDefined(typeof(BatteryStatus), status) ||
                !long.TryParse(parts[3], out ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                throw new InvalidDataException();
            int? level = null;
            if (parts[0].Length > 0)
            {
                if (!int.TryParse(parts[0], out percentage) || percentage < 0 || percentage > 100) throw new InvalidDataException();
                level = percentage;
            }
            return new BatteryReading(level, (BatteryStatus)status, parts[2], new DateTime(ticks, DateTimeKind.Utc));
        }
    }

    internal sealed class BatteryPair
    {
        public BatteryReading Left, Right;
        public static BatteryPair Unavailable(string detail)
        {
            return new BatteryPair { Left = new BatteryReading(null, BatteryStatus.Error, detail),
                Right = new BatteryReading(null, BatteryStatus.Error, detail) };
        }
    }

    internal static class PollProcess
    {
        private static readonly object lifecycle = new object();
        private static Process active;
        private static bool stopping;

        public static void Stop()
        {
            lock (lifecycle)
            {
                stopping = true;
                if (active != null)
                    try { if (!active.HasExited) active.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
            }
        }

        public static BatteryPair Read(string executable)
        {
            try
            {
                string[] lines = Run(executable, "--read", null).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length != 2) return BatteryPair.Unavailable("电量查询结果无效");
                return new BatteryPair { Left = ReadingWire.Decode(lines[0]), Right = ReadingWire.Decode(lines[1]) };
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is InvalidDataException || e is TimeoutException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)) throw;
                return BatteryPair.Unavailable("电量查询失败或超时");
            }
        }

        // One bounded worker per discovery provider or device. Stuck native calls cannot
        // block the UI, lose other devices' successful results, or leak polling threads.
        public static string Run(string executable, string arguments, string input)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, arguments) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    RedirectStandardInput = input != null,
                    WorkingDirectory = Path.GetDirectoryName(executable), StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                try
                {
                    lock (lifecycle)
                    {
                        if (stopping || active != null) throw new InvalidOperationException("电量查询已停止");
                        process.Start();
                        active = process;
                    }
                    var output = process.StandardOutput.ReadToEndAsync();
                    if (input != null) { process.StandardInput.WriteLine(input); process.StandardInput.Close(); }
                    if (!process.WaitForExit(6000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        process.WaitForExit(1000);
                        throw new TimeoutException("电量查询超时，请点击重试");
                    }
                    if (!output.Wait(1000)) throw new TimeoutException("电量查询输出超时");
                    if (process.ExitCode != 0 || output.Result.Length > 2097152) throw new InvalidDataException("设备读取进程未成功完成");
                    return output.Result;
                }
                finally
                {
                    // Also reap workers if writing the request or decoding output failed.
                    try { if (process.Id != 0 && !process.HasExited) { process.Kill(); process.WaitForExit(1000); } }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    lock (lifecycle) { if (ReferenceEquals(active, process)) active = null; }
                }
            }
        }
    }
}
