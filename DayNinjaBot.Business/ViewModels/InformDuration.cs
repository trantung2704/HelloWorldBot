using System;

namespace DayNinjaBot.Business.ViewModels
{
    public class InformDuration
    {
        public long CurrentTaskId { get; set; }

        public DateTimeOffset StartTime { get; set; }
    }
}