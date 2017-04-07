using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            task.TimeLogs = new List<TimeLog>();
            DbContext.Tasks.Add(task);
            return (int) task.Id;
        }

        public int GetTaskCount(string userId)
        {
            return DbContext.Tasks.Count(i => i.UserId == userId);
        }

        public TaskViewModel GetFirstTask(string userId)
        {
            return DbContext.Tasks.First(i => i.UserId == userId);
        }

        public List<TaskViewModel> GetTasks(string userId, string description)
        {
            return DbContext.Tasks.Where(i => i.UserId == userId &&
                                              string.Equals(description, i.Description, StringComparison.CurrentCultureIgnoreCase))
                            .ToList();
        }

        public void RemoveTasks(string userId)
        {
            DbContext.Tasks.RemoveAll(i => i.UserId == userId);
        }

        public List<TaskViewModel> GetTasks(string userId)
        {
            return DbContext.Tasks.Where(i => i.UserId == userId).ToList();
        }

        public TaskViewModel GetTask(long id)
        {
            return DbContext.Tasks.First(i => i.Id == id);
        }

        public void RemoveTask(long id)
        {
            var removedTask = DbContext.Tasks.First(i => i.Id == id);
            DbContext.Tasks.Remove(removedTask);
        }

        public void UpdateTask(TaskViewModel task)
        {
            var entity = DbContext.Tasks.First(i => i.Id == task.Id);
            DbContext.Tasks.Remove(entity);

            DbContext.Tasks.Add(task);
        }
    }
}