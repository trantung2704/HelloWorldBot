using System;
using System.Data.Entity;
using DayNinjaBot.Data.Entites;

namespace DayNinjaBot.Data
{
    public class PayNinjaDb : DbContext
    {
        public PayNinjaDb() : base("PayNinjaBotEntities")
        {
            
        }

        public DbSet<Task> Tasks { get; set; }

        public DbSet<TimeLog> TimeLogs { get; set; }
    }
}