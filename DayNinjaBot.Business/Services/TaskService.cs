using System;
using System.Collections.Generic;
using PayNinja.Business.ViewModels;

namespace DayNinjaBot.Business.Services
{
    [Serializable]
    public class TaskService
    {
        public int CreateNewTask(TaskViewModel task)
        {
            task.Id = new Random().Next(1, 999);
            DbContext.Tasks.Add(task);
            return (int) task.Id;
        }
    }
}