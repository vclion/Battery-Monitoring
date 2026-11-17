using System;

namespace BatteryMonitor
{
    internal static class BatteryProtocol
    {
        public static byte[] HeadsetRequest()
        {
            byte[] request = new byte[64];
            request[0] = 0x55; request[1] = 0x65; request[2] = 0x01;
            return request;
        }
        public static byte[] MouseRequest(int length)
        {
            if (length < 21) throw new ArgumentOutOfRangeException("length");
            byte[] request = new byte[length];
            // Report 0x11, query 0x06; invert command/payload, preserve the report ID.
            for (int i = 1; i < length; i++) request[i] = 0xFF;
            request[0] = 0x11; request[1] = 0xF9;
            return request;
        }
        public static BatteryReading ParseHeadset(byte[] response)
        {
            if (response == null || response.Length < 4 || response[0] != 0x55 || response[1] != 0x65 || response[2] > 100) return null;
            switch (response[3])
            {
                case 2: return new BatteryReading(response[2], BatteryStatus.Online, "在线");
                case 3: return new BatteryReading(response[2], BatteryStatus.Charging, "充电中");
                case 4: return new BatteryReading(response[2], BatteryStatus.Full, "已充满");
                case 26: return new BatteryReading(null, BatteryStatus.Sleeping, "耳机休眠或未连接");
                default: return new BatteryReading(null, BatteryStatus.Unknown, "设备状态未知");
            }
        }
        public static BatteryReading ParseMouse(byte[] response)
        {
            if (response == null || response.Length < 13 || response[0] != 0x11 || response[1] != 0xF9) return null;
            int vendor = (response[2] ^ 0xFF) | ((response[3] ^ 0xFF) << 8);
            // All-zero payloads after wake-up and unrelated responses are not battery samples.
            if (vendor != 0x5253) return null;
            int flags = response[10] ^ 0xFF;
            int level = response[11] ^ 0xFF;
            int charge = response[12] ^ 0xFF;
            if ((flags & 8) == 0) return new BatteryReading(null, BatteryStatus.Disconnected, "鼠标未连接或休眠");
            if (level > 100 || charge > 1) return null;
            return new BatteryReading(level, charge == 1 ? BatteryStatus.Charging : BatteryStatus.Online,
                charge == 1 ? "充电中" : "在线");
        }
    }
}
