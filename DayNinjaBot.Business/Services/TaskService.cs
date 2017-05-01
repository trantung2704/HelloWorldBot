using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using DayNinjaBot.Data;
using DayNinjaBot.Data.Entites;
using PayNinja.Business.ViewModels;
using TimeLog = DayNinjaBot.Data.Entites.TimeLog;

namespace DayNinjaBot.Business.Services
{
    public class TaskService : ITaskService
    {
        private readonly PayNinjaDb db;
        
        public TaskService(PayNinjaDb db)
        {
            this.db = db;
        }

        public int CreateTask(TaskViewModel task)
        {
            var newTask = new Task
                          {
                              AddedByUserId = task.AddedByUserId,
                              DayMoveCount = task.DayMoveCount,
                              Description = task.Description,
                              Done = task.Done,
                              ForDate = task.ForDate,
                              ForUserId = task.ForUserId,
                              HourlyRate = task.HourlyRate,
                              JobId = task.JobId,
                              PosNo = task.PosNo,
                              PrivateNote = task.PrivateNote,
                              PublicNote = task.PublicNote,
                              RestMins = task.RestMins,
                              SplitOfTaskId = task.SplitOfTaskId,
                              UnitMins = task.UnitMins,
                              UnitsAct = task.UnitsAct,
                              UnitsEst = task.UnitsEst,
                              Updated = task.Updated,
                              Tags = task.Tags ?? new List<string>(),
                              UserId = task.UserId,
                              TotalTime = task.TotalTime
                          };

            db.Tasks.Add(newTask);
            db.SaveChanges();
            task.Id = newTask.Id;
            return (int) newTask.Id;
        }

        public int GetTaskCount(string userId)
        {
            return db.Tasks.Count(i => i.UserId == userId);
        }

        public TaskViewModel GetFirstTask(string userId)
        {
            var task  = db.Tasks.FirstOrDefault(i => i.UserId == userId);
            return new TaskViewModel(task);
        }

        public IEnumerable<TaskViewModel> GetTasks(string userId, string description)
        {
            return db.Tasks.Where(i => i.UserId == userId &&
                                       description.ToLower() == i.Description.ToLower())
                     .ToList()
                     .Select(i => new TaskViewModel(i));
        }

        public void RemoveTasks(string userId)
        {
            var removedTasks = db.Tasks.Where(i => i.UserId == userId);
            db.Tasks.RemoveRange(removedTasks);
            db.SaveChanges();
        }

        public IEnumerable<TaskViewModel> GetTasks(string userId)
        {
            return db.Tasks.Where(i => i.UserId == userId)
                     .ToList()
                     .Select(i => new TaskViewModel(i));
        }

        public TaskViewModel GetTask(long id)
        {
            var task = db.Tasks.Find(id);
            if (task == null)
            {
                return null;
            }
            task.TimeLogs = db.TimeLogs.Where(i=>i.TaskId == task.Id).ToList();
            return new TaskViewModel(task);
        }

        public void RemoveTask(long id)
        {
            var removedTask = db.Tasks.Find(id);
            if (removedTask != null)
            {
                db.Tasks.Remove(removedTask);
            }            
            db.SaveChanges();
        }

        public void UpdateTask(TaskViewModel task)
        {
           var entity = db.Tasks.First(i => i.Id == task.Id);
           entity.AddedByUserId = task.AddedByUserId;
           entity.DayMoveCount = task.DayMoveCount;
           entity.Description = task.Description;
           entity.Done = task.Done;
           entity.ForDate = task.ForDate;
           entity.ForUserId = task.ForUserId;
           entity.HourlyRate = task.HourlyRate;
           entity.JobId = task.JobId;
           entity.PosNo = task.PosNo;
           entity.PrivateNote = task.PrivateNote;
           entity.PublicNote = task.PublicNote;
           entity.RestMins = task.RestMins;
           entity.SplitOfTaskId = task.SplitOfTaskId;
           entity.UnitMins = task.UnitMins;
           entity.UnitsAct = task.UnitsAct;
           entity.UnitsEst = task.UnitsEst;
           entity.Updated = task.Updated;
           entity.Tags = task.Tags ?? new List<string>();
           entity.UserId = task.UserId;
           entity.TotalTime = task.TotalTime;
        }

        public void SetTag(List<string> currentTags, long id)
        {
            var task = db.Tasks.Find(id);

            if (task != null)
            {
                task.Tags = currentTags;
                db.Entry(task).State = EntityState.Modified;
                db.SaveChanges();
            }            
        }

        public void AddTimeLog(TimeLogViewModel timeLogViewModel, long taskId)
        {
            var timeLog = new TimeLog
                          {
                              StartTime = timeLogViewModel.StartTime,
                              EndTime = timeLogViewModel.EndTime,
                              TaskId = taskId,
                          };
            db.TimeLogs.Add(timeLog);
            db.SaveChanges();
        }
    }
}