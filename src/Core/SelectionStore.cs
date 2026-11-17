using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BatteryMonitor
{
    internal sealed class SelectionStore
    {
        public string LeftId = "", RightId = "";
        private readonly string path;
        public SelectionStore(string file) { path = file; }
        public static SelectionStore ForCurrentUser()
        {
            return new SelectionStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BatteryMonitor", "selection.txt"));
        }
        private static bool Valid(string id) { return id == "" || Regex.IsMatch(id, "^[0-3]:[0-9A-F]{32}$"); }
        public void Load()
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 1024) return;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 3 && lines[0] == "BatteryMonitor.Selection.1" && Valid(lines[1]) && Valid(lines[2]))
                { LeftId = lines[1]; RightId = lines[2]; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public void Save()
        {
            if (!Valid(LeftId) || !Valid(RightId)) throw new InvalidDataException("设备选择无效");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, new[] { "BatteryMonitor.Selection.1", LeftId, RightId }, new UTF8Encoding(false));
        }
    }
}
