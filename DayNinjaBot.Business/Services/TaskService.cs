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
            if (task.Tags == null)
            {
                task.Tags = new List<string>();
            }
            DbContext.Tasks.Add(task);
            return (int) task.Id;
        }
    }
}