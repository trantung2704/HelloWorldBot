using System.Collections.Generic;
using PayNinja.Business.ViewModels;

namespace DayNinjaBot.Business.Services
{
    public interface ITaskService
    {
        void AddTimeLog(TimeLogViewModel timeLogViewModel, long taskId);
        int CreateTask(TaskViewModel task);
        TaskViewModel GetFirstTask(string userId);
        TaskViewModel GetTask(long id);
        int GetTaskCount(string userId);
        IEnumerable<TaskViewModel> GetTasks(string userId);
        IEnumerable<TaskViewModel> GetTasks(string userId, string description);
        void RemoveTask(long id);
        void RemoveTasks(string userId);
        void UpdateTask(TaskViewModel task);
        void SetTag(List<string> currentTags, long id);
    }
}