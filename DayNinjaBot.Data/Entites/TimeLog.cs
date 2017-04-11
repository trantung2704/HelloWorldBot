using System;

namespace DayNinjaBot.Data.Entites
{
    public class TimeLog
    {
        public long Id { get; set; }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset EndTime { get; set; }

        public long TaskId { get; set; }

        public virtual Task Task { get; set; }
    }
}