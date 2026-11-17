using System;

namespace BatteryMonitor
{
    internal enum BatteryStatus { Unknown, Online, Charging, Full, Sleeping, Disconnected, Error, Stale, Reported }

    internal sealed class BatteryReading
    {
        public readonly int? Percentage;
        public readonly BatteryStatus Status;
        public readonly string Detail;
        public readonly DateTime ObservedAtUtc;

        public BatteryReading(int? percentage, BatteryStatus status, string detail)
            : this(percentage, status, detail, DateTime.UtcNow) { }
        public BatteryReading(int? percentage, BatteryStatus status, string detail, DateTime observedAtUtc)
        {
            Percentage = percentage; Status = status; Detail = detail; ObservedAtUtc = observedAtUtc;
        }
        public bool IsValid(DateTime nowUtc)
        {
            return Percentage.HasValue && Percentage.Value >= 0 && Percentage.Value <= 100 &&
                (Status == BatteryStatus.Online || Status == BatteryStatus.Charging || Status == BatteryStatus.Full || Status == BatteryStatus.Reported)
                && ObservedAtUtc <= nowUtc.AddSeconds(5);
        }
        public string Display(DateTime nowUtc)
        {
            if (IsValid(nowUtc)) return Percentage.Value + "%" +
                (Status == BatteryStatus.Charging ? "（充电中）" : Status == BatteryStatus.Full ? "（已充满）" : Status == BatteryStatus.Reported ? "（系统报告）" : "");
            return "--（" + Detail + "）";
        }
    }

}
