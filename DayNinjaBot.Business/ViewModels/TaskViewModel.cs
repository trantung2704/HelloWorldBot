﻿using System;
using System.Collections.Generic;
using System.Linq;
using DayNinjaBot.Data.Entites;

namespace PayNinja.Business.ViewModels
{
    public class TaskPostViewModel
    {
        //public TaskPostViewModel()
        //{
        //    Intervals = new List<TaskIntervalViewModel>();
        //}


        public long Id { get; set; }

        public string Description { get; set; }
        //public TaskEnumerations.DayAllocation DayAllocation { get; set; }

        public int? PosNo { get; set; } // where in the list
        public DateTimeOffset? Created { get; set; }  // client side time date
        public string PublicNote { get; set; } //Ignore: future use
        public string PrivateNote { get; set; }  //Ignore:future use
        public long? JobId { get; set; } //Ignore: only when related to a job (future use)

        public int? UnitMins { get; set; }  //Ignore: advanced 
        public int? RestMins { get; set; }  //Ignore: advanced
        public int? UnitsEst { get; set; } //Ignore: advanced
        public int? UnitsAct { get; set; } //Ignore: advanced
        public long? SplitOfTaskId { get; set; } //Ignore: advanced
        public DateTimeOffset? Done { get; set; }


        //public List<TaskIntervalViewModel> Intervals { get; set; }
    }



    public class TaskViewModel
    {
        public TaskViewModel()
        {
        }

        public TaskViewModel(Task task)
        {
            Id = task.Id;
            AddedByUserId = task.AddedByUserId;
            DayMoveCount = task.DayMoveCount;
            Description = task.Description;
            Done = task.Done;
            ForDate = task.ForDate;
            ForUserId = task.ForUserId;
            HourlyRate = task.HourlyRate;
            JobId = task.JobId;
            PosNo = task.PosNo;
            PrivateNote = task.PrivateNote;
            PublicNote = task.PublicNote;
            RestMins = task.RestMins;
            SplitOfTaskId = task.SplitOfTaskId;
            UnitMins = task.UnitMins;
            UnitsAct = task.UnitsAct;
            UnitsEst = task.UnitsEst;
            Updated = task.Updated;
            Tags = task.Tags ?? new List<string>();
            UserId = task.UserId;
            TotalTime = task.TotalTime;
            TimeLogs = task.TimeLogs?.Select(i=> new TimeLogViewModel(i)).ToList() ?? new List<TimeLogViewModel>();

        }
        //public TaskViewModel()
        //{
        //    Intervals = new List<TaskIntervalViewModel>();
        //}
        public long Id { get; set; }
        public int? ForUserId { get; set; } // not needed on post
        public string AddedByUserId { get; set; }
        public long? JobId { get; set; }
        public string Description { get; set; }
        public string PublicNote { get; set; }
        public string PrivateNote { get; set; }
        public DateTimeOffset? Created { get; set; }
        public DateTimeOffset? ForDate { get; set; }
        public int? PosNo { get; set; }
        public int UnitMins { get; set; }
        public int RestMins { get; set; }
        public int? UnitsEst { get; set; }
        public int? UnitsAct { get; set; }
        public decimal? HourlyRate { get; set; }
        public int? DayMoveCount { get; set; }
        public long? SplitOfTaskId { get; set; }
        public DateTimeOffset? Updated { get; set; }
        //public TaskEnumerations.DayAllocation DayAllocation { get; set; }
        public DateTimeOffset? Done { get; set; }

        //public List<TaskIntervalViewModel> Intervals { get; set; } // only populated in the GET response  (ie. Don't POST or PUT)
        public TimeSpan? TotalTime { get; set; }  // only populated in the GET response  (ie. Don't POST or PUT)

        public List<string> Tags { get; set; }

        public string UserId { get; set; }

        public List<TimeLogViewModel> TimeLogs { get; set; }
    }

    public class TimeLogViewModel
    {
        public TimeLogViewModel()
        {
            
        }

        public TimeLogViewModel(TimeLog timeLog)
        {
            StartTime = timeLog.StartTime;
            EndTime = timeLog.EndTime;
        }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset EndTime { get; set; }
    }
}
