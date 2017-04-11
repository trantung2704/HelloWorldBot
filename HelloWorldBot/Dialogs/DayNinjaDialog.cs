using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Web.UI;
using System.Web.UI.WebControls;
using Chronic;
using DayNinjaBot.Business;
using DayNinjaBot.Business.Services;
using DayNinjaBot.Business.ViewModels;
using Hangfire;
using HelloWorldBot.Models;
using HelloWorldBot.Queries;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.FormFlow;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Builder.Scorables;
using Microsoft.Bot.Connector;
using Newtonsoft.Json;
using PayNinja.Business.ViewModels;

namespace HelloWorldBot.Dialogs
{
    [Serializable]
    [LuisModel("ba898e9f-0981-4273-9530-d0d27b5f1c17", "a5f4459c9dee4f65a535203c555f9531")]
    public class DayNinjaDialog : LuisDialog<object>
    {
        private  readonly TaskService taskService = new TaskService();

        [LuisIntent("")]
        [LuisIntent("None")]        
        public async Task None(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("Sorry I dont understand you.");
            context.Wait(MessageReceived);
        }

        [LuisIntent("Greeting")]
        public async Task Greeting(IDialogContext context, LuisResult result) 
        {
            var profileUrl = await GetProfileUrl(context.Activity.From.Id);
            if (profileUrl != string.Empty)
            {
                await context.PostAsync($"your are: {profileUrl}");
            }

            DateTimeOffset lastUpdate;

            var canGetLastUpdate = context.UserData.TryGetValue(DataKeyManager.LastUpdate, out lastUpdate);

            if (!(canGetLastUpdate && lastUpdate.Date == DateTimeOffset.UtcNow.Date))
            {
                await context.PostAsync("Hey Ninja!  Ready to be productive today? ");
                context.UserData.SetValue(DataKeyManager.LastUpdate, DateTimeOffset.UtcNow);
            }            

            TaskViewModel currentTask;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                var continueActiveTaskOption = new[] {"Yes", "No"};
                var continueTaskDialog = new PromptDialog.PromptChoice<string>(continueActiveTaskOption, $"I assume you are still working on {currentTask.Description} right?  ", null, 3);
                context.Call(continueTaskDialog, AfterConfirmActiveTask);
                return;
            }

            var userId = context.Activity.From.Id;
            var taskCount = taskService.GetTaskCount(userId);
            if (taskCount == 0)
            {
                await context.PostAsync("So, what are you working on?");
                context.Wait(MessageReceived);
            }
            else if (taskCount == 1)
            {
                context.UserData.SetValue(DataKeyManager.SuggestFocusTask, taskService.GetFirstTask(userId));

                var promptOptions = new[] {"Yes", "Not yet"};
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(userId).Description}?", null, 3);
                context.Call(dialog, AfterOfferSuggestFocusOnTask);
            }
            else if (taskCount > 1)
            {
                var promptOptions = new[] {"Yes", "List tasks pending"};
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(userId).Description}?", null, 3);
                context.Call(dialog, AfterOfferSuggestFocusOnTask);
            }            
        }

        [LuisIntent("ConversationUpdate")]
        public async Task ConversationUpdate(IDialogContext context, LuisResult result)
        {
            await Greeting(context, result);
        }

        [LuisIntent("WhatAreTags")]
        public async Task WhatAreTags(IDialogContext context, LuisResult result)
        {
            var card = new HeroCard
                       {
                           Title = "What are tags?",
                           Text = "Tags allow you to track which activities you are spending your focused time",
                           Buttons = new List<CardAction>
                                     {
                                         new CardAction()
                                         {
                                             Type = ActionTypes.OpenUrl,
                                             Title = "Link to Faq",
                                             Value = "http://google.com"
                                         }
                                     }
            };            

            var message = context.MakeMessage();
            message.Attachments = new List<Attachment>
                                  {
                                      card.ToAttachment()
                                  };

            await context.PostAsync(message);
        }

        [LuisIntent("HowCanI")]
        public async Task HowCanI(IDialogContext context, LuisResult result)
        {
            var card = new HeroCard
            {
                Title = "Faq",
                Text = "You can find more information in our page",
                Buttons = new List<CardAction>
                                     {
                                         new CardAction()
                                         {
                                             Type = ActionTypes.OpenUrl,
                                             Title = "Link to Faq",
                                             Value = "http://google.com"
                                         }
                                     }
            };

            var message = context.MakeMessage();
            message.Attachments = new List<Attachment>
                                  {
                                      card.ToAttachment()
                                  };
            await context.PostAsync(message);
            context.Wait(MessageReceived);
        }

        [LuisIntent("DefineTask")]
        public async Task DefineTask(IDialogContext context, LuisResult result)
        {
            var userId = context.Activity.From.Id;
            EntityRecommendation taskDescriptionEntity;
            if (result.TryFindEntity(LuisEntities.TaskTitle, out taskDescriptionEntity))
            {
                var description = PreProcessTitleEntity(taskDescriptionEntity);

                var tasks = taskService.GetTasks(userId, description);

                if (!tasks.Any())
                {
                    await CreateTaskAsync(context, string.Join(" ", description));
                    return;
                }
                var task = taskService.GetFirstTask(userId);

                TaskViewModel currentTask;
                if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
                {
                    var tagStrings = currentTask.Tags.Select(i => $"#{i}");

                    await context.PostAsync($"OK, I'll pause this {string.Join(",", tagStrings)} {currentTask.Description} here.");
                }

                context.UserData.SetValue(DataKeyManager.CurrentTask, task);
                RegisterTrackTimeProcesses(context);

                context.UserData.SetValue(DataKeyManager.LastStartTimer, task);
                context.UserData.SetValue(DataKeyManager.StartTimer, DateTimeOffset.UtcNow);                

                await context.PostAsync($"Timer start for {task.Description}");
                context.Wait(MessageReceived);
            }
            else
            {
                await context.PostAsync("Task description is not regconized");
                context.Wait(MessageReceived);
            }
        }

        [LuisIntent("DeleteUserData")]
        public async Task DeleteUserData(IDialogContext context, LuisResult result)
        {
            context.UserData.Clear();
            taskService.RemoveTasks(context.Activity.From.Id);
            await context.PostAsync("User data has been deleted");
        }

        [LuisIntent("ListTasks")]
        public async Task ListTasks(IDialogContext context, LuisResult result)
        {
            TaskViewModel task;
            var remainTaskCount = taskService.GetTaskCount(context.Activity.From.Id);
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out task))
            {
                await context.PostAsync($"Current tasks is: {task.Description}");
                remainTaskCount --;
            }

            if (remainTaskCount == 0)
            {
                await context.PostAsync("You dont have any task");
                context.Wait(MessageReceived);
                return;
            }

            if (remainTaskCount == 1)
            {
                await context.PostAsync("You have following task:");
            }

            if (remainTaskCount > 1)
            {
                await context.PostAsync("You have following tasks:");
            }

            var userId = context.Activity.From.Id;
            var tasks = taskService.GetTasks(userId);
            foreach (var taskViewModel in tasks)
            {
                if (task == null || taskViewModel.Id != task.Id)
                {
                    await context.PostAsync($"- {taskViewModel.Description}");
                }
            }

            context.Wait(MessageReceived);
        }

        [LuisIntent("PauseTimer")]
        public async Task PauseTimer(IDialogContext context, LuisResult result)
        {
            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync("You dont have active task to pause");
                context.Wait(MessageReceived);
                return;
            }

            var informDuartionStarTime = context.UserData.Get<DateTimeOffset>(DataKeyManager.InformDurationStartTime);
            currentTask.TimeLogs.Add(new TimeLog
                                     {
                                         StartTime = informDuartionStarTime,
                                         EndTime = DateTimeOffset.UtcNow
                                     });
            taskService.UpdateTask(currentTask);
            context.UserData.RemoveValue(DataKeyManager.InformDurationStartTime);


            context.UserData.RemoveValue(DataKeyManager.CurrentTask);
            context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);

            await context.PostAsync($"{currentTask.Description} has been paused, you can type 'Resume timer' to continue your task");
            var options = new[] { "OK", "Remind me in 10", "Remind me in 15" };
            var dialog = new PromptDialog.PromptChoice<string>(options, "OK I will remind you to resume in 5 mins", null, 3);
            context.Call(dialog, AfterChoseRemindTimeAfterPauseTask);            
        }

        public async Task AfterChoseRemindTimeAfterPauseTask(IDialogContext context, IAwaitable<string> result)
        {
            var resume = new ResumptionCookie(context.Activity.AsMessageActivity());

            var options = new Dictionary<string, string>
                          {
                              {"Yes resume", "Resume"},
                              {"No I am working on something else", "DeclineResume"}
                          };
            var pauseTask = context.UserData.Get<TaskViewModel>(DataKeyManager.PausedTask);

            var message = $"Let's go back to work on {pauseTask.Description}";
            switch (await result)
            {
                case "OK":                    
                    BackgroundJob.Schedule(() => CreateCardReply(resume, string.Empty, message, options), TimeSpan.FromMinutes(5));
                    break;
                case "Remind me in 10":
                    BackgroundJob.Schedule(() => CreateCardReply(resume, string.Empty, message, options), TimeSpan.FromMinutes(10));
                    break;
                case "Remind me in 15":
                    BackgroundJob.Schedule(() => CreateCardReply(resume, string.Empty, message, options), TimeSpan.FromMinutes(15));
                    break;
            }
        }

        [LuisIntent("StopTimer")]
        public async Task StopTimer(IDialogContext context, LuisResult result)
        {
            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync("You dont have active task to pause");
                context.Wait(MessageReceived);
                return;
            }            

            var task = taskService.GetTask(currentTask.Id);

            var confirmStopTaskOptions = new [] { "Pause", "Complete" };
            var confirmStopTaskDialog = new PromptDialog.PromptChoice<string>(confirmStopTaskOptions, $"Shall I pause {task.Description} or mark as complete? ", null, 3);
            context.Call(confirmStopTaskDialog, AfterConfirmStopTask);           
        }

        [LuisIntent("ResumeTimer")]
        public async Task ResumeTimer(IDialogContext context, LuisResult result)
        {
            TaskViewModel pausedTask;
            if (!context.UserData.TryGetValue(DataKeyManager.PausedTask, out pausedTask))
            {
                await context.PostAsync("You dont have paused task to resume");
                context.Wait(MessageReceived);
                return;
            }

            context.UserData.RemoveValue(DataKeyManager.PausedTask);
            context.UserData.SetValue(DataKeyManager.CurrentTask, pausedTask);
            RegisterTrackTimeProcesses(context);

            await context.PostAsync($"{pausedTask.Description} has been resumed");
            context.Wait(MessageReceived);
        }

        [LuisIntent("AddTask")]
        public async Task AddTask(IDialogContext context, LuisResult result)
        {
            var taskQuery = new TaskQuery();

            var taskForm = new FormDialog<TaskQuery>(taskQuery, BuildTaskForm, FormOptions.PromptInStart);
            context.Call(taskForm, AfterTaskForm);
        }

        [LuisIntent("StartTimer")]
        public async Task StartTimer(IDialogContext context, LuisResult result)
        {
            EntityRecommendation taskTitleWithTagsEntity;

            result.TryFindEntity(LuisEntities.TaskTitle, out taskTitleWithTagsEntity);

            var taskDescription = PreProcessTitleEntity(taskTitleWithTagsEntity).Replace("#", string.Empty);

            var userId = context.Activity.From.Id;
            var tasks = taskService.GetTasks(userId, taskDescription);

            if (taskService.GetTaskCount(userId)> 0)
            {
                await context.PostAsync("There is no task like you describled");
                context.Wait(MessageReceived);
                return;
            }
            var task = tasks.FirstOrDefault();

            DateTimeOffset lastStartTimer;
            var canGetLastStartTimer = context.UserData.TryGetValue(DataKeyManager.LastStartTimer, out lastStartTimer);
            if (canGetLastStartTimer && lastStartTimer.Date != DateTimeOffset.Now.Date)
            {
                await context.PostAsync("Remember you can tell me to switch or pause at any time, but it is best to remain focused on this single task!  ... so I'll shut up now until time is up.");                
            }

            context.UserData.SetValue(DataKeyManager.LastStartTimer, DateTimeOffset.UtcNow);
            context.UserData.SetValue(DataKeyManager.CurrentTask, task);
            RegisterTrackTimeProcesses(context);

            await context.PostAsync($"Timer has stared for: {task.Description}");

            context.Wait(MessageReceived);
        }

        [LuisIntent("ListMyTags")]
        public async Task ListMyTags(IDialogContext context, LuisResult result)
        {
            TaskViewModel currenTask;
            if(!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currenTask))
            {
                await context.PostAsync("you dont have an active task right now");
                context.Wait(MessageReceived);
                return;
            }
            if(!currenTask.Tags.Any())
            {
                await context.PostAsync($"task {currenTask.Description} dose not have any tag");
                context.Wait(MessageReceived);
                return;
            }

            await context.PostAsync($"Task {currenTask.Description} has following tasks: {string.Join(";", currenTask.Tags)}");
            context.Wait(MessageReceived);
        }

        [LuisIntent("DeclineResume")]
        public async Task DeclineResume(IDialogContext context, LuisResult result)
        {
            TaskViewModel pausedTask;
            if (context.UserData.TryGetValue(DataKeyManager.PausedTask, out pausedTask))
            {
                context.UserData.RemoveValue(DataKeyManager.PausedTask);
                await context.PostAsync($"So what are you working on?");                
            }            
            context.Wait(MessageReceived);
        }

        [LuisIntent("BreakIn5")]
        public async Task BreakIn(IDialogContext context, LuisResult result)
        {
            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync("You dont have active task to pause");
                context.Wait(MessageReceived);
                return;
            }
            var informDuartionStarTime = context.UserData.Get<DateTimeOffset>(DataKeyManager.InformDurationStartTime);
            currentTask.TimeLogs.Add(new TimeLog
            {
                StartTime = informDuartionStarTime,
                EndTime = DateTimeOffset.UtcNow
            });
            taskService.UpdateTask(currentTask);
            context.UserData.RemoveValue(DataKeyManager.InformDurationStartTime);

            context.UserData.RemoveValue(DataKeyManager.CurrentTask);
            context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);


            var resume = new ResumptionCookie(context.Activity.AsMessageActivity());
            var message = $"Let's go back to work on {currentTask.Description}";
            var options = new Dictionary<string, string>
                          {
                              {"Yes resume", "Resume"},
                              {"No I am working on something else", "DeclineResume"}
                          };

            await context.PostAsync("Go for a walk, get a glass of water. I'll remind you when 5mins is up");
            BackgroundJob.Schedule(() => CreateCardReply(resume, string.Empty, message, options), TimeSpan.FromMinutes(5));
        }

        [LuisIntent("RemindBreakLater")]
        public async Task DelayBreak(IDialogContext context, LuisResult result)
        {
            EntityRecommendation entityRecommendation;
            if (result.TryFindEntity(LuisEntities.DelayBreakMinutes, out entityRecommendation))
            {

                var delayMinutes = int.Parse(entityRecommendation.Entity);
                TaskViewModel currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);
                var resumptionCookie = new ResumptionCookie(context.Activity.AsMessageActivity());
                BackgroundJob.Schedule(() => SuggestBreak(resumptionCookie, currentTask.Id), TimeSpan.FromMinutes(delayMinutes));
                await context.PostAsync("Ok");
            }
        }

        private async Task AfterOfferKnowAboutTags(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "Tell me more":
                    await WhatAreTags(context, null);
                    break;
                case "Yes, I know":                    
                    context.UserData.SetValue(DataKeyManager.KnownAboutTags, true);
                    break;
            }
            await SuggestFocusTask(context);
        }

        private async Task SuggestFocusTask(IDialogContext context)
        {
            TaskViewModel suggestFocusTask;
            if (context.UserData.TryGetValue(DataKeyManager.SuggestFocusTask, out suggestFocusTask))
            {
                var promptOptions = new[] { "Yes", "Not yet" };
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, "Let's get focused!", null, 3);
                context.Call(dialog, AfterOfferSuggestFocusOnTask);
                return;
            }

            TaskViewModel currentTask;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync($"Timer has started for {currentTask.Description}");
                RegisterTrackTimeProcesses(context);
            }
            context.Wait(MessageReceived);
        }

        private void RegisterTrackTimeProcesses(IDialogContext context)
        {
            TaskViewModel currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);
            context.UserData.SetValue(DataKeyManager.InformDurationStartTime, DateTimeOffset.UtcNow);

            var resumptionCookie = new ResumptionCookie(context.Activity.AsMessageActivity());
            BackgroundJob.Schedule(() => InformDuration(resumptionCookie, currentTask.Id), TimeSpan.FromMinutes(15));
            BackgroundJob.Schedule(() => SuggestBreak(resumptionCookie, currentTask.Id), TimeSpan.FromMinutes(25));
        }

        private async Task AfterOfferSuggestFocusOnTask(IDialogContext context, IAwaitable<string> result)
        {
            TaskViewModel suggestTask;
            context.UserData.TryGetValue(DataKeyManager.SuggestFocusTask, out suggestTask);
            switch (await result)
            {
                case "Yes":
                    DateTimeOffset lastStartTimer;
                    var canGetLastStartTimer = context.UserData.TryGetValue(DataKeyManager.LastStartTimer, out lastStartTimer);
                    if (canGetLastStartTimer && lastStartTimer.Date != DateTimeOffset.Now.Date)
                    {
                        await context.PostAsync("Remember you can tell me to switch or pause at any time, but it is best to remain focused on this single task!  ... so I'll shut up now until time is up.");
                    }
                    context.UserData.SetValue(DataKeyManager.LastStartTimer, DateTimeOffset.UtcNow);
                    context.UserData.SetValue(DataKeyManager.CurrentTask, suggestTask);
                    RegisterTrackTimeProcesses(context);

                    await context.PostAsync($"Timer has stared for: {suggestTask.Description}");
                    context.Wait(MessageReceived);
                    break;

                case "Not yet":
                    await context.PostAsync("No worries buddy you are the boss here! tell me what you are working on once you start.");
                    context.Wait(MessageReceived);
                    break;

                case "List tasks pending":
                    var pendingDescripotionTasks = taskService.GetTasks(context.Activity.From.Id).Select(i => i.Description);
                    var dialog = new PromptDialog.PromptChoice<string>(pendingDescripotionTasks, "Here are your pending tasks: ", null, 3);
                    context.Call(dialog, AfterChoseTaskFromPendingList);
                    break;
            }

            context.UserData.RemoveValue(DataKeyManager.SuggestFocusTask);            
        }

        private async Task AfterChoseTaskFromPendingList(IDialogContext context, IAwaitable<string> result)
        {
            var taskDescription = await result;
            var tasks = taskService.GetTasks(context.Activity.From.Id, taskDescription);

            context.UserData.SetValue(DataKeyManager.CurrentTask, tasks.First());
            RegisterTrackTimeProcesses(context);
            await context.PostAsync($"Timer has started for {tasks.First().Description}");
            context.Wait(MessageReceived);
        }

        private async Task AfterOfferTags(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "What are tags?":
                    await WhatAreTags(context, null);
                    break;
                case "Thanks":
                    List<string> currentTags;
                    if (!context.UserData.TryGetValue(DataKeyManager.CurrentTags, out currentTags))
                    {
                        await context.PostAsync("There is an issue when process tags");
                        context.Wait(MessageReceived);
                        break;
                    }
                    TaskViewModel currentTask;
                    if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask) &&
                        !context.UserData.TryGetValue(DataKeyManager.SuggestFocusTask, out currentTask))
                    {
                        await context.PostAsync("There is an issue when process tags");
                        context.Wait(MessageReceived);
                        break;
                    }

                    var task = taskService.GetTask(currentTask.Id);
                    task.Tags = currentTags;
                    if (currentTags.Count == 1)
                    {
                        await context.PostAsync($"{currentTags.Count} tag has been linked to task");
                    }
                    else
                    {
                        await context.PostAsync($"{currentTags.Count} tags have been linked to task");
                    }                    

                    context.UserData.SetValue(DataKeyManager.CurrentTags, new List<string>());                    
                    break;
            }
            await SuggestFocusTask(context);
        }

        private async Task AfterOfferTagCapitalWords(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "No":
                    break;
                case "Yes":
                    List<string> currentTags;
                    if (!context.UserData.TryGetValue(DataKeyManager.CurrentTags, out currentTags))
                    {
                        await context.PostAsync("There is an issue when process tags");
                        context.Wait(MessageReceived);
                        break;
                    }
                    TaskViewModel currentTask;
                    if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask)
                        && !context.UserData.TryGetValue(DataKeyManager.SuggestFocusTask, out currentTask))
                    {
                        await context.PostAsync("There is an issue when process tags");
                        context.Wait(MessageReceived);
                        break;
                    }

                    var task = taskService.GetTask(currentTask.Id);
                    task.Tags = currentTags;
                    await context.PostAsync($"{currentTags.Count} tags has been link to task");

                    context.UserData.SetValue(DataKeyManager.CurrentTags, new List<string>());                    
                    break;
            }
            await SuggestFocusTask(context);

        }

        private async Task AfterConfirmActiveTask(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "Yes":
                    await context.PostAsync("I will keep timer running");                    
                    break;
                case "No":
                    var currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);

                    taskService.RemoveTask(currentTask.Id);
                    context.UserData.RemoveValue(DataKeyManager.CurrentTask);

                    await context.PostAsync("Timer has stopped");
                    await context.PostAsync("So what are you working on?");
                    break;
            }
            context.Wait(MessageReceived);
        }

        private async Task AfterTaskForm(IDialogContext context, IAwaitable<TaskQuery> result)
        {
            var taskQuery = await result;
            await CreateTaskAsync(context, taskQuery.Description, false);
        }

        private async Task CreateTaskAsync(IDialogContext context, string taskDescription, bool startAfterCreate = true)
        {
            bool knownAboutTags;
            try
            {
                knownAboutTags = context.UserData.Get<bool>(DataKeyManager.KnownAboutTags);
            }
            catch (Exception)
            {
                knownAboutTags = false;
                context.UserData.SetValue(DataKeyManager.KnownAboutTags, false);
            }

            var tags = taskDescription.Split(' ')
                                      .Where(i => i.StartsWith("#"))
                                      .Select(i => i.Substring(1));
            taskDescription = taskDescription.ReplaceAll("#", string.Empty);

            var capitalisedWords = tags.Where(i => char.IsUpper(i[0]));

            context.UserData.SetValue(DataKeyManager.CurrentTaskDescription, taskDescription);

            var newTask = new TaskViewModel
            {
                Description = taskDescription,
                AddedByUserId = context.Activity.From.Id,
                Created = DateTimeOffset.UtcNow,
                UserId =  context.Activity.From.Id
            };

            taskService.CreateNewTask(newTask);
            if (startAfterCreate)
            {
                context.UserData.SetValue(DataKeyManager.CurrentTask, newTask);
                context.UserData.SetValue(DataKeyManager.LastStartTimer, DateTimeOffset.UtcNow);                
            }
            else
            {
                context.UserData.SetValue(DataKeyManager.SuggestFocusTask, newTask);
            }

            await context.PostAsync($"Task with description: '{taskDescription}' has been created");

            if (!tags.Any() && !knownAboutTags)
            {
                var promptOptions = new[] { "Tell me more", "Yes, I know" };
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, "Did you know if you can markup tasks by writing #hashtags?", null, 3);
                context.Call(dialog, AfterOfferKnowAboutTags);
            }
            else if (tags.Any() && capitalisedWords.Any())
            {
                context.UserData.SetValue(DataKeyManager.CurrentTags, tags);
                var promptOptions = new[] { "Yes", "No" };

                var tagsString = string.Empty;
                foreach (var tag in capitalisedWords)
                {
                    tagsString += $"{tag}, ";
                }

                var dialog = new PromptDialog.PromptChoice<string>(promptOptions,
                                                                   $"I see you mentioned {tagsString}, I'll tag for your stats \n " +
                                                                   "BTW: if me asking you this is bothersome then include one #hashtag or don\'t use Capitalised words ;)",
                                                                   null,
                                                                   3);
                context.Call(dialog, AfterOfferTagCapitalWords);
            }
            else if (tags.Any())
            {
                context.UserData.SetValue(DataKeyManager.CurrentTags, tags);
                var promptOptions = new[] { "Thanks", "What are tags?" };

                var tagsString = string.Empty;
                foreach (var tag in tags)
                {
                    tagsString += $"{tag}, ";
                }

                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"I see you mentioned {tagsString} I'll tag for your stats", null, 3);
                context.Call(dialog, AfterOfferTags);
            }
            else
            {
                await SuggestFocusTask(context);
            }
        }

        private IForm<TaskQuery> BuildTaskForm()
        {
            OnCompletionAsyncDelegate<TaskQuery> processAddTask = async (context, state) =>
            {
                await context.PostAsync("We are creating task for you");
            };

            return new FormBuilder<TaskQuery>()
                .OnCompletion(processAddTask)
                .Build();
        }

        private string PreProcessTitleEntity(EntityRecommendation taskDescription)
        {
            var tmpTaskDes = taskDescription.Entity.Split(' ');

            for (int i = 0; i < tmpTaskDes.Length; i++)
            {
                if (tmpTaskDes[i] == "#")
                {
                    tmpTaskDes[i] = string.Empty;
                    tmpTaskDes[i + 1] = $"#{tmpTaskDes[i + 1]}";
                }
            }

            tmpTaskDes = tmpTaskDes.Where(i => !string.IsNullOrEmpty(i))
                                   .ToArray();

            return string.Join(" ", tmpTaskDes);
        }

        private async Task AfterConfirmStopTask(IDialogContext context, IAwaitable<string> result)
        {
            var currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);
            context.UserData.RemoveValue(DataKeyManager.CurrentTask);
            switch (await result)
            {
                case "Complete":                    
                    taskService.RemoveTask(currentTask.Id);

                    await context.PostAsync($"{currentTask.Description} has been stopped and removed from your task list.");

                    var remainTaskCount = taskService.GetTaskCount(context.Activity.From.Id);

                    if (remainTaskCount == 0)
                    {
                        await context.PostAsync("So, what are you working on?");
                        context.Wait(MessageReceived);
                    }
                    else if (remainTaskCount == 1)
                    {
                        context.UserData.SetValue(DataKeyManager.SuggestFocusTask, taskService.GetFirstTask(context.Activity.From.Id));

                        var promptOptions = new[] { "Yes", "Not yet" };
                        var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(context.Activity.From.Id)}?", null, 3);
                        context.Call(dialog, AfterOfferSuggestFocusOnTask);
                    }
                    else
                    {
                        var promptOptions = new[] { "Yes", "List tasks pending" };
                        var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(context.Activity.From.Id)}?", null, 3);
                        context.Call(dialog, AfterOfferSuggestFocusOnTask);
                    }
                    break;
                case "Pause":
                    context.UserData.RemoveValue(DataKeyManager.CurrentTask);
                    context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);
                    await context.PostAsync($"{currentTask.Description} has been paused, you can type 'Resume timer' to continue your task");


                    var informDuartionStarTime = context.UserData.Get<DateTimeOffset>(DataKeyManager.InformDurationStartTime);
                    currentTask.TimeLogs.Add(new TimeLog
                    {
                        StartTime = informDuartionStarTime,
                        EndTime = DateTimeOffset.UtcNow
                    });
                    taskService.UpdateTask(currentTask);
                    context.UserData.RemoveValue(DataKeyManager.InformDurationStartTime);

                    context.Wait(MessageReceived);
                    break;
            }
        }

        public void InformDuration(ResumptionCookie resume, long needInformTaskId)
        {
            if (resume == null)
            {
                throw new ArgumentNullException(nameof(resume));
            }
            var restoreResume = RestoreResumptionCookie(resume);
            var messageactivity = restoreResume.GetMessage();
            
            var stateClient = messageactivity.GetStateClient();

            var userData = stateClient.BotState.GetUserData(restoreResume.Address.ChannelId, restoreResume.Address.UserId);

            var currentTask = userData.GetProperty<TaskViewModel>(DataKeyManager.CurrentTask);
            if (currentTask == null || currentTask.Id != needInformTaskId)
            {
                return;
            }

            var informDuartionStarTime = userData.GetProperty<DateTimeOffset>(DataKeyManager.InformDurationStartTime);

            var beingTrackedFocusTime = (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes;
            if (informDuartionStarTime == new DateTimeOffset() || beingTrackedFocusTime < 15)
            {
                return;
            }

            BackgroundJob.Schedule(() => InformDuration(resume, needInformTaskId), TimeSpan.FromMinutes(15));

            var task = taskService.GetTask(needInformTaskId);
            var logedFocusTime = task.TimeLogs.Sum(i => (i.EndTime - i.StartTime).TotalMinutes);

            var reply = messageactivity.CreateReply();
            reply.Text = $"Awesome! You have been focused on {task.Description}, for {int.Parse((logedFocusTime + beingTrackedFocusTime).ToString())} minutes";

            var client = new ConnectorClient(new Uri(messageactivity.ServiceUrl));
            client.Conversations.ReplyToActivity(reply);
        }

        public void SuggestBreak(ResumptionCookie resume, long needInformTaskId)
        {
            var restoreResume = RestoreResumptionCookie(resume);
            var messageactivity = restoreResume.GetMessage();

            var stateClient = messageactivity.GetStateClient();

            var userData = stateClient.BotState.GetUserData(restoreResume.Address.ChannelId, restoreResume.Address.UserId);

            var currentTask = userData.GetProperty<TaskViewModel>(DataKeyManager.CurrentTask);
            if (currentTask == null || currentTask.Id != needInformTaskId)
            {
                return;
            }

            var informDuartionStarTime = userData.GetProperty<DateTimeOffset>(DataKeyManager.InformDurationStartTime);

            var beingTrackedFocusTime = (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes;
            if (informDuartionStarTime == new DateTimeOffset() || beingTrackedFocusTime < 25)
            {
                return;
            }
            var options = new Dictionary<string, string>
                          {
                              {"Ok I will", "BreakIn 5"  },
                              {"Remind me in 5", "RemindBreakLater 5" },
                              { "Remind me in 10", "RemindBreakLater 10"},
                              { "Remind me in 15", "RemindBreakLater 15"},
                          };
            CreateCardReply(resume, string.Empty, "To help your focus, take a break for 5mins now", options);
        }

        public ResumptionCookie RestoreResumptionCookie(ResumptionCookie resume)
        {
            var data = JsonConvert.SerializeObject(resume);

            dynamic resumeData = JsonConvert.DeserializeObject(data);
            string botId = resumeData.address.botId;
            string channelId = resumeData.address.channelId;
            string userId = resumeData.address.userId;
            string conversationId = resumeData.address.conversationId;
            string serviceUrl = resumeData.address.serviceUrl;
            string userName = resumeData.userName;
            bool isGroup = resumeData.isGroup;

            var restoreResume = new ResumptionCookie(new Address(botId, channelId, userId, conversationId, serviceUrl), userName, isGroup, "en_GB");
            return restoreResume;
        }

        public void CreateReply(ResumptionCookie resume, string message)
        {
            var restoreResume = RestoreResumptionCookie(resume);
            var messageactivity = restoreResume.GetMessage();
            var reply = messageactivity.CreateReply();

            reply.Text = message;

            var client = new ConnectorClient(new Uri(messageactivity.ServiceUrl));

            client.Conversations.ReplyToActivity(reply);
        }

        public void CreateCardReply(ResumptionCookie resume, string title, string text, Dictionary<string, string> options)
        {
            var buttons = options.Select(i => new CardAction
                                              {
                                                  Type = ActionTypes.PostBack,
                                                  Title = i.Key,
                                                  Value = i.Value
                                              })
                                 .ToList();

            var card = new HeroCard
            {
                Title = title,
                Text = text,
                Buttons = buttons
            };


            var restoreResume = RestoreResumptionCookie(resume);
            var messageactivity = restoreResume.GetMessage();            

            var reply = messageactivity.CreateReply();
            reply.Attachments = new List<Attachment> { card.ToAttachment() };

            var client = new ConnectorClient(new Uri(messageactivity.ServiceUrl));
            client.Conversations.ReplyToActivity(reply);
        }

        private async Task<string> GetProfileUrl(string userId)
        {
            try
            {
                var httpClient = new HttpClient();

                var url = $"https://graph.facebook.com/v2.6/{userId}?fields=first_name,last_name,profile_pic,locale,timezone,gender&access_token={ConfigurationReader.AppAccessToken}";
                var responseString = await httpClient.GetStringAsync(url);
                var profile = JsonConvert.DeserializeObject<FacebookUserProfile>(responseString);

                var profilePicture = profile.ProfilePicture.Split(new[] { ".jpg" }, StringSplitOptions.None)
                                            .First()
                                            .Split('/')
                                            .Last();

                return $"{profilePicture}.jpg";

            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

    }

    internal class FacebookUserProfile
    {
        [JsonProperty("first_name")]
        public string Firstname { get; set; }

        [JsonProperty("last_name")]
        public string LastName { get; set; }

        [JsonProperty("profile_pic")]
        public string ProfilePicture { get; set; }
    }
}