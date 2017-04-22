using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Resources;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Autofac;
using Autofac.Core;
using Autofac.Integration.WebApi;
using Chronic;
using DayNinjaBot.Business;
using DayNinjaBot.Business.Services;
using DayNinjaBot.Data;
using Hangfire;
using HelloWorldBot.Models;
using HelloWorldBot.Queries;
using HelloWorldBot.Resources;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.FormFlow;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Connector;
using Microsoft.Rest.Serialization;
using Newtonsoft.Json;
using PayNinja.Business.ViewModels;

namespace HelloWorldBot.Dialogs
{
    [Serializable]
    [LuisModel("d0f7f7e2-4dcf-483b-8623-53a6ce63cff9", "f9646355e73642c7bbf3d60cb8640ad2")]
    public class DayNinjaDialog : LuisDialog<object>
    {
        private readonly ITaskService taskService;

        public DayNinjaDialog()
        {
            var builder = new ContainerBuilder();
            builder.RegisterType<DayNinjaDialog>()
                   .As<IDialog<object>>()
                   .InstancePerDependency();

            builder.RegisterType<PayNinjaDb>()
                   .AsSelf()
                   .SingleInstance();

            builder.RegisterType<TaskService>()
                   .Keyed<ITaskService>(FiberModule.Key_DoNotSerialize)
                   .AsImplementedInterfaces()
                   .SingleInstance();

            var resolver = builder.Build();
            taskService = resolver.Resolve<ITaskService>();
        }

        public DayNinjaDialog(ITaskService taskService)
        {
            this.taskService = taskService;
        }

        [LuisIntent("")]
        [LuisIntent("None")]        
        public async Task None(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            await context.PostAsync("Sorry I dont understand you.");
            context.Wait(MessageReceived);
        }

        [LuisIntent("Greeting")]
        public async Task Greeting(IDialogContext context, LuisResult result)
        {
            bool isOnboard;
            if (!context.UserData.TryGetValue(DataKeyManager.IsOnBoard, out isOnboard))
            {
               await AskToJoin(context);
            }
                        
            if (!isOnboard)
            {
                return;
            }

            DateTimeOffset lastUpdate;
            var canGetLastUpdate = context.UserData.TryGetValue(DataKeyManager.LastAction, out lastUpdate);

            if (!canGetLastUpdate || lastUpdate.Date !=  await DateTimeOffsetNow(context))
            {                
                await context.PostAsync("Hey Ninja!  Ready to be productive today? ");                                
            }
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));

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
                context.Call(dialog, AfterSuggestFocusOnTask);
            }
            else if (taskCount > 1)
            {
                var promptOptions = new[] {"Yes", "List tasks pending"};
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(userId).Description}?", null, 3);
                context.Call(dialog, AfterSuggestFocusOnTask);
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
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
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
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
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
            try
            {
                context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
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
                    var task = tasks.First();

                    TaskViewModel currentTask;
                    if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
                    {
                        var tagStrings = currentTask.Tags.Select(i => $"#{i}");
                        await EndTrackTimeAsync(context, true);
                        await context.PostAsync($"OK, I'll pause this {string.Join(",", tagStrings)} {currentTask.Description} here.");
                    }

                    context.UserData.SetValue(DataKeyManager.CurrentTask, task);
                    await StartTrackTimeProcesses(context);

                    await context.PostAsync($"Timer start for {task.Description}");
                    context.Wait(MessageReceived);
                }
                else
                {
                    await context.PostAsync("Sorry, I cannot understand what that task is. Please phase 'I am working on (say what you are working on, include #hashtags too if you like)");
                    context.Wait(MessageReceived);
                }

            }
            catch (Exception ex)
            {
                await context.PostAsync(context.Activity.From.Id);
                await context.PostAsync(ex.Message);
            }
        }

        [LuisIntent("DeleteUserData")]
        public async Task DeleteUserData(IDialogContext context, LuisResult result)
        {
            context.UserData.Clear();
            taskService.RemoveTasks(context.Activity.From.Id);
            await context.PostAsync("User data has been deleted");
            context.Wait(MessageReceived);
        }

        [LuisIntent("ListTasks")]
        public async Task ListTasks(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
            TaskViewModel task;
            var remainTaskCount = taskService.GetTaskCount(context.Activity.From.Id);
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out task))
            {
                await context.PostAsync($"Current tasks is: {task.Description}");
                remainTaskCount --;
            }

            if (remainTaskCount == 0)
            {
                await context.PostAsync("You dont have any tasks");
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
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync("You dont have active task to pause");
                context.Wait(MessageReceived);
                return;
            }

            await EndTrackTimeAsync(context, true);

            await context.PostAsync($"{currentTask.Description} has been paused, you can type 'Resume timer' to continue your task");
            var options = new[] { "OK", "Remind me in 10", "Remind me in 15" };
            var dialog = new PromptDialog.PromptChoice<string>(options, "OK I will remind you to resume in 5 mins", null, 3);
            context.Call(dialog, AfterChoseRemindTimeAfterPauseTask);            
        }        

        [LuisIntent("StopTimer")]
        public async Task StopTimer(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync("You dont have active task to stop");
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
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
            TaskViewModel pausedTask;
            if (!context.UserData.TryGetValue(DataKeyManager.PausedTask, out pausedTask))
            {
                await context.PostAsync("You dont have paused task to resume");
                context.Wait(MessageReceived);
                return;
            }
            var clientTime = (await DateTimeOffsetNow(context));

            context.UserData.RemoveValue(DataKeyManager.PausedTask);
            context.UserData.SetValue(DataKeyManager.CurrentTask, pausedTask);
            context.UserData.SetValue(DataKeyManager.InformDurationStartTime, clientTime);
            await StartTrackTimeProcesses(context);
                        
            var task = taskService.GetTask(pausedTask.Id);            

            var loggedTime = task.TimeLogs.Where(i => i.StartTime.Date == clientTime.Date
                                                      && i.EndTime.Date == clientTime.Date)
                                 .Sum(i => (i.EndTime - i.StartTime).TotalMinutes);
            await context.PostAsync($"You have resumed {task.Description}. You have been working on this task for {Math.Round(loggedTime)}mins today.");
            context.Wait(MessageReceived);
        }

        [LuisIntent("AddTask")]
        public async Task AddTask(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
            var taskQuery = new TaskQuery();

            var taskForm = new FormDialog<TaskQuery>(taskQuery, BuildTaskForm, FormOptions.PromptInStart);
            context.Call(taskForm, AfterTaskForm);
        }

        [LuisIntent("StartTimer")]
        public async Task StartTimer(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
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

            TaskViewModel currentTask;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await EndTrackTimeAsync(context, true);
            }

            var task = tasks.FirstOrDefault();

            DateTimeOffset lastStartTimer;
            var canGetLastStartTimer = context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out lastStartTimer);
            if (canGetLastStartTimer && lastStartTimer.Date != (await DateTimeOffsetNow(context)).Date)
            {
                await context.PostAsync("Remember you can tell me to switch or pause at any time, but it is best to remain focused on this single task!  ... so I'll shut up now until time is up.");                
            }

            context.UserData.SetValue(DataKeyManager.CurrentTask, task);
            await StartTrackTimeProcesses(context);

            await context.PostAsync($"Timer has stared for: {task.Description}");

            context.Wait(MessageReceived);
        }

        [LuisIntent("ListMyTags")]
        public async Task ListMyTags(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
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

        [LuisIntent("InformDuration")]
        public async Task InformDuration(IDialogContext context, LuisResult result)
        {
            EntityRecommendation needInformTaskIdEntity;
            if (!result.TryFindEntity(LuisEntities.TaskId, out needInformTaskIdEntity))
            {
                context.Wait(MessageReceived);
                return;
            }

            var userData = context.UserData;

            var needInformTaskId = int.Parse(needInformTaskIdEntity.Entity);            
            TaskViewModel currentTask;
            
            if (!userData.TryGetValue(DataKeyManager.CurrentTask, out currentTask) || currentTask.Id != needInformTaskId)                
            {
                context.Wait(MessageReceived);
                return;
            }

            DateTimeOffset informDuartionStarTime;
            if (!context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out informDuartionStarTime)
                || (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes < 13)
            {
                context.Wait(MessageReceived);
                return;
            }

            var task = taskService.GetTask(needInformTaskId);
            var beingTrackedFocusTime = (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes;

            await context.PostAsync($"Awesome! You have been focused on {task.Description} for {Math.Round(beingTrackedFocusTime)}mins since your last break");
            context.Wait(MessageReceived);
            
            var resumptionCookie = new ResumptionCookie(context.Activity.AsMessageActivity());            
            BackgroundJob.Schedule(() => TriggerInformDuration(resumptionCookie, needInformTaskId), TimeSpan.FromMinutes(15));
        }

        [LuisIntent("SuggestBreak")]
        public async Task SuggestBreak(IDialogContext context, LuisResult result)
        {
            EntityRecommendation suggestBreakTaskIdEntity;
            if (!result.TryFindEntity(LuisEntities.TaskId, out suggestBreakTaskIdEntity))
            {
                context.Wait(MessageReceived);
                return;
            }
            var suggestBreakTaskId = int.Parse(suggestBreakTaskIdEntity.Entity);

            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask)
                ||  currentTask.Id != suggestBreakTaskId)
            {
                context.Wait(MessageReceived);
                return;
            }

            DateTimeOffset informDuartionStarTime;
            if (!context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out informDuartionStarTime)
                || (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes < 23)
            {
                context.Wait(MessageReceived);
                return;
            }

            var beingTrackedFocusTime = (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes;
            await context.PostAsync($"Awesome! You have been focused on {currentTask.Description} for {Math.Round(beingTrackedFocusTime)}mins");

            var options = new[] { "Ok I will", "Remind me in 5", "Remind me in 10", "Remind me in 15" };
            var dialog = new PromptDialog.PromptChoice<string>(options, "To help your focus, take a break for 5mins now", null, 3);
            context.Call(dialog, AfterSuggestBeeak);
        }

        [LuisIntent("SuggestResume")]
        public async Task SuggestResume(IDialogContext context, LuisResult result)
        {
            TaskViewModel currentTask;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                context.Wait(MessageReceived);
                return;
            }

            EntityRecommendation suggestResumeTaskIdEntity;
            if (!result.TryFindEntity(LuisEntities.TaskId, out suggestResumeTaskIdEntity))
            {
                context.Wait(MessageReceived);
                return;
            }
            var suggestResumeTaskId = int.Parse(suggestResumeTaskIdEntity.Entity);


            TaskViewModel pausedTask;
            if (!context.UserData.TryGetValue(DataKeyManager.PausedTask, out pausedTask)
                || pausedTask.Id != suggestResumeTaskId)
            {
                context.Wait(MessageReceived);
                return;                
            }

            var options = new[] { "Yes Resume", "No I am working on something else" };
            var dialog = new PromptDialog.PromptChoice<string>(options, $"Let's get back to work on {pausedTask.Description}", null, 3);
            context.Call(dialog, AfterOfferResumeAfterPasue);
        }

        [LuisIntent("StopWorking")]
        public async Task StopWorking(IDialogContext context, LuisResult result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, await DateTimeOffsetNow(context));
            TaskViewModel currentTask;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await EndTrackTimeAsync(context, true);
            }

            var options = new[] { "OK", "I will comeback tomorrow" };
            var dialog = new PromptDialog.PromptChoice<string>(options, "Do you want to comeback after 60mins?", null, 3);
            context.Call(dialog, AfterConfirmStopWorking);
        }

        [LuisIntent("ConfirmOvernight")]
        public async Task ConfirmOvernight(IDialogContext context, LuisResult result)
        {
            EntityRecommendation confirmOvernighTaskIdEntity;
            if (!result.TryFindEntity(LuisEntities.TaskId, out confirmOvernighTaskIdEntity))
            {
                context.Wait(MessageReceived);
                return;
            }
            var confirmOvernightTaskId = int.Parse(confirmOvernighTaskIdEntity.Entity);

            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask)
                || currentTask.Id != confirmOvernightTaskId)
            {
                context.Wait(MessageReceived);
                return;
            }

            DateTimeOffset informDuartionStarTime;
            if (!context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out informDuartionStarTime))
            {
                context.Wait(MessageReceived);
                return;
            }

            DateTimeOffset lastAction;
            if (!context.UserData.TryGetValue(DataKeyManager.LastAction, out lastAction))
            {
                context.Wait(MessageReceived);
                return;
            }

            var beingTrackedFocusTime = (DateTimeOffset.UtcNow - informDuartionStarTime).TotalMinutes;
            var options = new[] { "Yes", "No" };
            var dialog = new PromptDialog.PromptChoice<string>(options, $"Were you working on {currentTask.Description} until know? " +
                                                                        $"The last I hear from wast at {lastAction.ToString("dd/MM HH:mm")} " +
                                                                        $"and you had been working for {Math.Round(beingTrackedFocusTime)}mins", null, 3);
            context.Call(dialog, AfterConfirmOvernight);

        }

        [LuisIntent("ResetOnboard")]
        public async Task ResetOnboard(IDialogContext context, LuisResult result)
        {
            context.UserData.RemoveValue(DataKeyManager.IsOnBoard);
            context.UserData.RemoveValue(DataKeyManager.CheckinTime);
            context.UserData.RemoveValue(DataKeyManager.CheckinWeekkend);

            await context.PostAsync("Onboard process has been reseted");
            context.Wait(MessageReceived);
        }

        public void TriggerInformDuration(ResumptionCookie resume, long needInformTaskId)
        {
            var message = $"Inform duration {needInformTaskId}";
            SendMessageToBot(resume, message);
        }

        public void TriggerSuggestBreak(ResumptionCookie resume, long needSuggestBreakTaskId)
        {
            var message = $"Suggest break {needSuggestBreakTaskId}";
            SendMessageToBot(resume, message);
        }

        public async Task StartTrackTimeProcesses(IDialogContext context)
        {
            TaskViewModel currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);
            context.UserData.SetValue(DataKeyManager.InformDurationStartTime, await DateTimeOffsetNow(context));

            var midnight = await DateTimeOffsetNow(context);
            midnight = midnight.Date.AddHours(24);

            var resumptionCookie = new ResumptionCookie(context.Activity.AsMessageActivity());
            BackgroundJob.Schedule(() => TriggerInformDuration(resumptionCookie, currentTask.Id), TimeSpan.FromMinutes(15));            
            BackgroundJob.Schedule(() => TriggerSuggestBreak(resumptionCookie, currentTask.Id), TimeSpan.FromMinutes(25));
            BackgroundJob.Schedule(() => TriggerConfirmOvernight(resumptionCookie, currentTask.Id), midnight);
        }

        public void TriggerConfirmOvernight(ResumptionCookie resumptionCookie, long needConfirmOvernightTaskId)
        {
            var message = $"Confirm overnight {needConfirmOvernightTaskId}";
            SendMessageToBot(resumptionCookie, message);
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
        
        public void SendMessageToBot(ResumptionCookie resume, string message)
        {
            if (resume == null)
            {
                throw new ArgumentNullException(nameof(resume));
            }
            var restoreResume = RestoreResumptionCookie(resume);
            var messageActivity = restoreResume.GetMessage();

            messageActivity.Text = message;
            messageActivity.Type = ActivityTypes.Message;

            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri(ConfigurationReader.BotApiEndpoint);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var token = GetAuthToken(ConfigurationReader.BotMicrosoftAppId, ConfigurationReader.BotMicrosoftAppPassword);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var result = client.PostAsJsonAsync("/api/messages", messageActivity)
                                   .Result;
            }
        }

        private async Task AskToJoin(IDialogContext context)
        {
            var profile = await GetProfile(context.Activity.From.Id);
            var letter = string.Format(Language.AskToJoinLetter, profile.Firstname);
            await context.PostAsync(letter);

            var options = new[]
                          {
                              Language.YesBringItOn,
                              Language.NoIamNotInterestedImprovingMyLife
                          };
            var dialog = new PromptDialog.PromptChoice<string>(options, Language.AsktoJoinQuestion, null, 3);
            context.Call(dialog, AfterOfferOnboard);
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
                context.Call(dialog, AfterSuggestFocusOnTask);
                return;
            }

            TaskViewModel currentTask;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync($"Timer has started for {currentTask.Description}");
                await StartTrackTimeProcesses(context);
            }
            context.Wait(MessageReceived);
        }

        private async Task AfterSuggestFocusOnTask(IDialogContext context, IAwaitable<string> result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            TaskViewModel suggestTask;
            context.UserData.TryGetValue(DataKeyManager.SuggestFocusTask, out suggestTask);
            switch (await result)
            {
                case "Yes":
                    DateTimeOffset lastStartTimer;
                    var canGetLastStartTimer = context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out lastStartTimer);
                    if (canGetLastStartTimer && lastStartTimer.Date != (await DateTimeOffsetNow(context)).Date)
                    {
                        await context.PostAsync("Remember you can tell me to switch or pause at any time, but it is best to remain focused on this single task!  ... so I'll shut up now until time is up.");
                    }
                    context.UserData.SetValue(DataKeyManager.CurrentTask, suggestTask);

                    await StartTrackTimeProcesses(context);

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
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            var taskDescription = await result;
            var tasks = taskService.GetTasks(context.Activity.From.Id, taskDescription);

            context.UserData.SetValue(DataKeyManager.CurrentTask, tasks.First());
            await StartTrackTimeProcesses(context);
            await context.PostAsync($"Timer has started for {tasks.First().Description}");
            context.Wait(MessageReceived);
        }

        private async Task AfterOfferTags(IDialogContext context, IAwaitable<string> result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
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
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
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
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            switch (await result)
            {
                case "Yes":
                    await context.PostAsync("I will keep timer running");                    
                    break;
                case "No":
                    await EndTrackTimeAsync(context, true);

                    await context.PostAsync("Timer has paused");
                    await context.PostAsync("So what are you working on?");                    
                    break;
            }
            context.Wait(MessageReceived);
        }

        private async Task EndTrackTimeAsync(IDialogContext context, bool isPause)
        {
            DateTimeOffset startTime;
            if (!context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out startTime))
            {
                await context.PostAsync("Add time log fail because cant get InformDurationStartTime");
                context.Wait(MessageReceived);
                return;
            }

            var currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);

            taskService.AddTimeLog(new TimeLogViewModel
                                   {
                                       StartTime = startTime,
                                       EndTime = await DateTimeOffsetNow(context)
                                   },
                                   currentTask.Id);

            currentTask.TimeLogs.Add(new TimeLogViewModel
                                     {
                                         StartTime = startTime,
                                         EndTime = await DateTimeOffsetNow(context)
            });
            context.UserData.RemoveValue(DataKeyManager.CurrentTask);
            context.UserData.RemoveValue(DataKeyManager.InformDurationStartTime);

            if (isPause)
            {
                context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);
            }            
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
                UserId =  context.Activity.From.Id,
                TimeLogs =  new List<TimeLogViewModel>()
            };

            taskService.CreateTask(newTask);
            await context.PostAsync($"Task with description: '{taskDescription}' has been created");

            if (startAfterCreate)
            {
                context.UserData.SetValue(DataKeyManager.CurrentTask, newTask);                
            }
            else
            {
                context.UserData.SetValue(DataKeyManager.SuggestFocusTask, newTask);
            }            

            if (!tags.Any() && !knownAboutTags)
            {
                var promptOptions = new[] { "Tell me more", "Yes, I know" };
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, "Did you know you can include #hashtags in your descriptions to categorise your tasks", null, 3);
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
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            var currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);
            context.UserData.RemoveValue(DataKeyManager.CurrentTask);
            switch (await result)
            {
                case "Complete":                    
                    taskService.RemoveTask(currentTask.Id);
                    context.UserData.RemoveValue(DataKeyManager.CurrentTask);
                    
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
                        var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(context.Activity.From.Id).Description}?", null, 3);
                        context.Call(dialog, AfterSuggestFocusOnTask);
                    }
                    else
                    {
                        context.UserData.SetValue(DataKeyManager.SuggestFocusTask, taskService.GetFirstTask(context.Activity.From.Id));

                        var promptOptions = new[] { "Yes", "List tasks pending" };
                        var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {taskService.GetFirstTask(context.Activity.From.Id).Description}?", null, 3);
                        context.Call(dialog, AfterSuggestFocusOnTask);
                    }
                    break;
                case "Pause":
                    await EndTrackTimeAsync(context, true);
                    await context.PostAsync($"{currentTask.Description} has been paused, you can type 'Resume timer' to continue your task");
                    context.Wait(MessageReceived);
                    break;
            }
        }

        private async Task<FacebookUserProfile> GetProfile(string userId)
        {
            try
            {
                var httpClient = new HttpClient();

                var url = $"https://graph.facebook.com/v2.9/{userId}?access_token={ConfigurationReader.AppAccessToken}";
                var responseString = await httpClient.GetStringAsync(url);
                var profile = JsonConvert.DeserializeObject<FacebookUserProfile>(responseString);
                return profile;

                //var profilePicture = profile.ProfilePicture.Split(new[] { ".jpg" }, StringSplitOptions.None)
                //                            .First()
                //                            .Split('/')
                //                            .Last();

                //return $"{profilePicture}.jpg";

            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task AfterChoseRemindTimeAfterPauseTask(IDialogContext context, IAwaitable<string> result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            var resume = new ResumptionCookie(context.Activity.AsMessageActivity());            
            var pauseTask = context.UserData.Get<TaskViewModel>(DataKeyManager.PausedTask);
            
            var triggerCommand = $"Suggest Resume {pauseTask.Id}";
            switch (await result)
            {
                case "OK":
                    BackgroundJob.Schedule(() => SendMessageToBot(resume, triggerCommand), TimeSpan.FromMinutes(5));
                    break;
                case "Remind me in 10":
                    BackgroundJob.Schedule(() => SendMessageToBot(resume, triggerCommand), TimeSpan.FromMinutes(10));
                    break;
                case "Remind me in 15":
                    BackgroundJob.Schedule(() => SendMessageToBot(resume, triggerCommand), TimeSpan.FromMinutes(15));
                    break;
            }
        }

        private async Task AfterOfferResumeAfterPasue(IDialogContext context, IAwaitable<string> result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            switch (await result)
            {
                case "Yes Resume":
                    await ResumeTimer(context, null);
                    break;
                case "No I am working on something else":
                    TaskViewModel pausedTask;
                    if (context.UserData.TryGetValue(DataKeyManager.PausedTask, out pausedTask))
                    {
                        context.UserData.RemoveValue(DataKeyManager.PausedTask);
                        await context.PostAsync($"So what are you working on?");
                    }
                    context.Wait(MessageReceived);
                    break;
            }
        }

        private async Task AfterSuggestBeeak(IDialogContext context, IAwaitable<string> result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            var resume = new ResumptionCookie(context.Activity.AsMessageActivity());
            TaskViewModel currentTask;
            if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
            {
                await context.PostAsync("You dont have active task to pause");
                context.Wait(MessageReceived);
                return;
            }
            switch (await result)
            {
                case "Ok I will":                    
                    var informDuartionStarTime = context.UserData.Get<DateTimeOffset>(DataKeyManager.InformDurationStartTime);

                    var timeLogViewModel = new TimeLogViewModel
                    {
                        StartTime = informDuartionStarTime,
                        EndTime = DateTimeOffset.UtcNow
                    };

                    currentTask.TimeLogs.Add(timeLogViewModel);
                    taskService.AddTimeLog(timeLogViewModel, currentTask.Id);
                    context.UserData.RemoveValue(DataKeyManager.InformDurationStartTime);

                    context.UserData.RemoveValue(DataKeyManager.CurrentTask);
                    context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);
                    await context.PostAsync("Go for a walk, get a glass of water. I'll remind you when 5mins is up");

                    var triggerCommand = $"Suggest Resume {currentTask.Id}";                    
                    BackgroundJob.Schedule(() => SendMessageToBot(resume, triggerCommand), TimeSpan.FromMinutes(5));
                    break;
                case "Remind me in 5":
                    BackgroundJob.Schedule(() => TriggerSuggestBreak(resume, currentTask.Id), TimeSpan.FromMinutes(5));
                    break;
                case "Remind me in 10":
                    BackgroundJob.Schedule(() => TriggerSuggestBreak(resume, currentTask.Id), TimeSpan.FromMinutes(10));
                    break;
                case "Remind me in 15":
                    BackgroundJob.Schedule(() => TriggerSuggestBreak(resume, currentTask.Id), TimeSpan.FromMinutes(15));
                    break;
            }
        }

        private async Task AfterConfirmStopWorking(IDialogContext context, IAwaitable<string> result)
        {
            context.UserData.SetValue(DataKeyManager.LastAction, DateTimeOffsetNow(context));
            var resumptionCookie = new ResumptionCookie(context.Activity.AsMessageActivity());
            var pausedTask = context.UserData.Get<TaskViewModel>(DataKeyManager.PausedTask);

            var message = $"Suggest resume {pausedTask.Id}";
            switch (await result)
            {
                case "Ok":
                    BackgroundJob.Schedule(() => SendMessageToBot(resumptionCookie, message), TimeSpan.FromMinutes(60));
                    break;
                case "I will comeback tomorrow":

                    var tasks = taskService.GetTasks(context.Activity.From.Id);

                    var tagReports = await CalculateTagReport(context, tasks);

                    foreach (var tagReport in tagReports)
                    {
                        await context.PostAsync($"Today #{tagReport.TagName} {tagReport.TotalHourInDay}hrs, this week {tagReport.TotalHoursInWeek}hrs total, {tagReport.TotalHours}hrs in all time");
                    }                 
                    

                    var nextCheckin = await CalculateNextCheckin(context);
                    BackgroundJob.Schedule(() => SendMessageToBot(resumptionCookie, message), nextCheckin);
                    break;
            }
        }

        private async Task AfterConfirmOvernight(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "Yes":
                    await context.PostAsync("I will keep timer running");
                    break;

                case "No":
                    DateTimeOffset startTime;
                    if (!context.UserData.TryGetValue(DataKeyManager.InformDurationStartTime, out startTime))
                    {
                        await context.PostAsync("Add time log fail because cant get InformDurationStartTime");
                        context.Wait(MessageReceived);
                        return;
                    }

                    DateTimeOffset lastAction;
                    if (!context.UserData.TryGetValue(DataKeyManager.LastAction, out lastAction))
                    {
                        await context.PostAsync("Add time log fail because cant get LastAction");
                        context.Wait(MessageReceived);
                        return;
                    }

                    var currentTask = context.UserData.Get<TaskViewModel>(DataKeyManager.CurrentTask);

                    taskService.AddTimeLog(new TimeLogViewModel
                                           {
                                               StartTime = startTime,
                                               EndTime = lastAction
                                           },
                                           currentTask.Id);

                    currentTask.TimeLogs.Add(new TimeLogViewModel
                                             {
                                                 StartTime = startTime,
                                                 EndTime = lastAction
                                             });

                    context.UserData.RemoveValue(DataKeyManager.CurrentTask);
                    context.UserData.RemoveValue(DataKeyManager.InformDurationStartTime);
                    context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);

                    await context.PostAsync("Ok I will store to last interaction with you");
                    break;
            }
            context.Wait(MessageReceived);
        }

        private async Task AfterOfferOnboard(IDialogContext context, IAwaitable<string> result)
        {
            if (await result == Language.YesBringItOn)
            {
                await context.PostAsync(Language.WelcomeOnboard);

                context.UserData.SetValue(DataKeyManager.IsOnBoard, true);
                var options = new[]
                              {
                                  Language.EightHour,
                                  Language.HalfPastEight,
                                  Language.NineHour,
                                  Language.TenHour
                              };
                var dialog = new PromptDialog.PromptChoice<string>(options, Language.AskTimeForCheckin, null, 3);
                context.Call(dialog, AfterOfferCheckinTime);
            }
            else if (await result == Language.NoIamNotInterestedImprovingMyLife)
            {
                context.UserData.SetValue(DataKeyManager.IsOnBoard, false);
                await context.PostAsync(Language.SorryForNotBeingOnBoard);
                context.Wait(MessageReceived);
            }
        }

        private async Task AfterOfferCheckinTime(IDialogContext context, IAwaitable<string> result)
        {
            var choice = await result;
             if (choice == Language.EightHour)
            {
                context.UserData.SetValue(DataKeyManager.CheckinTime, DateTimeOffset.Now.Date.AddHours(8));
            }
            else if (choice == Language.HalfPastEight)
            {
                var checkinTime = DateTimeOffset.Now.Date.AddHours(8).AddMinutes(30);
                context.UserData.SetValue(DataKeyManager.CheckinTime, checkinTime);
            }
            else if (choice == Language.NineHour)
            {
                context.UserData.SetValue(DataKeyManager.CheckinTime, DateTimeOffset.Now.Date.AddHours(9));
            }
            else if (choice == Language.TenHour)
            {
                context.UserData.SetValue(DataKeyManager.CheckinTime, DateTimeOffset.Now.Date.AddHours(10));
            }

            await context.PostAsync(string.Format(Language.InformCheckIntime, choice));
            
            var options = new[]
                          {
                              Language.No,
                              Language.Saturday,
                              Language.BothSaturdaySunday
                          };
            var dialog = new PromptDialog.PromptChoice<string>(options, Language.AskIfCheckInWeekend, null, 3);
            context.Call(dialog, AfterAskIfCheckInWeekend);
        }

        private async Task AfterAskIfCheckInWeekend(IDialogContext context, IAwaitable<string> result)
        {
            var choice = await result;
            if (choice == Language.No)
            {
                context.UserData.SetValue(DataKeyManager.CheckinWeekkend, CheckInWeekend.No);
            }
            else if (choice == Language.Saturday)
            {
                context.UserData.SetValue(DataKeyManager.CheckinWeekkend, CheckInWeekend.Saturday);
            }
            else if (choice == Language.BothSaturdaySunday)
            {
                context.UserData.SetValue(DataKeyManager.CheckinWeekkend, CheckInWeekend.BothSaturdayAndSunday);
            }

            await context.PostAsync(Language.OkNoted);

            var nextCheckin = await CalculateNextCheckin(context);
            var resumptionCookie = new ResumptionCookie(context.Activity.AsMessageActivity());

            BackgroundJob.Schedule(() => SendMessageToBot(resumptionCookie, Language.Hi), nextCheckin);
            await Greeting(context, new LuisResult());
        }

        private async Task<DateTimeOffset> CalculateNextCheckin(IDialogContext context)
        {           
            var checkinTime = context.UserData.Get<DateTimeOffset>(DataKeyManager.CheckinTime);

            var checkinHour = checkinTime.Hour;
            var checkinMinute = checkinTime.Minute;

            var checkinWeekend = context.UserData.Get<CheckInWeekend>(DataKeyManager.CheckinWeekkend);

            var clientTime = await DateTimeOffsetNow(context);

            var nextCheckin = clientTime.Date
                                        .AddHours(checkinHour)
                                        .AddMinutes(checkinMinute);

            var invalid = true;
            while (invalid)
            {
                if (clientTime > nextCheckin)
                {
                    nextCheckin = nextCheckin.AddDays(1);
                    continue;
                }

                if (nextCheckin.DayOfWeek == DayOfWeek.Saturday
                    && checkinWeekend == CheckInWeekend.No)
                {
                    nextCheckin = nextCheckin.AddDays(1);
                    continue;
                }

                if (nextCheckin.DayOfWeek == DayOfWeek.Sunday
                    && (checkinWeekend == CheckInWeekend.No || checkinWeekend == CheckInWeekend.Saturday))
                {
                    nextCheckin = nextCheckin.AddDays(1);
                    continue;
                }
                invalid = false;
            }

            return nextCheckin;

        }

        private async Task<List<TagReportModel>> CalculateTagReport(IDialogContext context, IEnumerable<TaskViewModel> tasks)
        {
            var tagReports = new List<TagReportModel>();

            foreach (var task in tasks)
            {
                var clientTimeToday = await DateTimeOffsetNow(context);
                var monday = clientTimeToday.StartOfWeek();
                var sunday = monday.AddDays(7);

                var totalHoursInDay = task.TimeLogs
                                          .Where(i => i.StartTime.Date == clientTimeToday.Date)
                                          .Sum(i => (i.StartTime - i.EndTime).TotalHours);

                var totalHoursInWeek = task.TimeLogs
                                           .Where(i => i.StartTime.Date > monday && i.StartTime.Date < sunday)
                                           .Sum(i => (i.StartTime - i.EndTime).TotalHours);

                var totalHours = task.TimeLogs.Sum(i => (i.StartTime - i.EndTime).TotalHours);

                foreach (var tag in task.Tags)
                {
                    var isExist = tagReports.Any(i => i.TagName == tag);
                    if (!isExist)
                    {
                        tagReports.Add(new TagReportModel
                        {
                            TagName = tag,
                            TotalHourInDay = totalHoursInDay,
                            TotalHoursInWeek = totalHoursInWeek,
                            TotalHours = totalHours
                        });
                    }
                    else
                    {
                        var tagReport = tagReports.First(i => i.TagName == tag);
                        tagReport.TotalHourInDay += totalHoursInDay;
                        tagReport.TotalHoursInWeek += totalHoursInWeek;
                        tagReport.TotalHours += totalHours;
                    }
                }
            }

            foreach (var tagReportModel in tagReports)
            {
                tagReportModel.TotalHourInDay = Math.Round(tagReportModel.TotalHourInDay, 2);
                tagReportModel.TotalHoursInWeek = Math.Round(tagReportModel.TotalHoursInWeek, 2);
                tagReportModel.TotalHours = Math.Round(tagReportModel.TotalHours, 2);
            }
            return tagReports;
        }


        public string GetAuthToken(string appId, string appKey)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("https://login.microsoftonline.com"); //This is important
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var values = new List<KeyValuePair<string, string>>();

                values.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
                values.Add(new KeyValuePair<string, string>("client_id", appId));
                values.Add(new KeyValuePair<string, string>("client_secret", appKey));
                values.Add(new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default")); // This is important

                using (var content = new FormUrlEncodedContent(values))
                {
                    var response = client.PostAsync("/common/oauth2/v2.0/token", content)
                                         .Result;
                    response.EnsureSuccessStatusCode();

                    var stringResponse = response.Content.ReadAsStringAsync()
                                                 .Result;
                    dynamic responseObject = JsonConvert.DeserializeObject(stringResponse);

                    return responseObject.access_token;
                }
            }
        }

        private async Task<int> GetTimeZone(IDialogContext context)
        {
            int timezone;

            if (context.UserData.TryGetValue(DataKeyManager.Timezone, out timezone))
            {
                return timezone; 
            }

            var profile = await GetProfile(context.Activity.From.Id);

            context.UserData.SetValue(DataKeyManager.Timezone, int.Parse(profile.Timezone));
            return int.Parse(profile.Timezone);
        }

        //It's not system time, it's client side time
        private async Task<DateTimeOffset> DateTimeOffsetNow(IDialogContext context)
        {
            if (context.Activity.ChannelId != "facebook")
            {
                return DateTimeOffset.UtcNow;
            }

            var timezone = await GetTimeZone(context);

            return new DateTimeOffset(DateTime.UtcNow.Year,
                                      DateTime.UtcNow.Month,
                                      DateTime.UtcNow.Day,
                                      DateTime.UtcNow.Hour + timezone,
                                      DateTime.UtcNow.Minute,
                                      DateTime.UtcNow.Second,
                                      TimeSpan.FromHours(timezone));
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

        [JsonProperty("timezone")]
        public string Timezone { get; set; }
    }
}