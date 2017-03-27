using System;
using System.Collections.Generic;
using PayNinja.Business.ViewModels;

namespace DayNinjaBot.Business
{
    public sealed class DbContext
    {
        private static readonly Lazy<DbContext> lazy =new Lazy<DbContext>(() => new DbContext());

        public static DbContext Instance => lazy.Value;

        public static readonly List<TaskViewModel> Tasks = new List<TaskViewModel>();

        private DbContext()
        {
        }
    }
}