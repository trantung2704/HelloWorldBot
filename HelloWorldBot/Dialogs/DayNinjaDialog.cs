using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Web.UI;
using System.Web.UI.WebControls;
using Chronic;
using DayNinjaBot.Business;
using DayNinjaBot.Business.Services;
using HelloWorldBot.Queries;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.FormFlow;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Builder.Scorables;
using Microsoft.Bot.Connector;
using PayNinja.Business.ViewModels;

namespace HelloWorldBot.Dialogs
{
    [Serializable]
    [LuisModel("21d7c272-725e-4794-90c7-f17fef5e7e78", "f2033cc0ddc94c7fad7e27797a618db0")]
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
            //await context.PostAsync("you are");
            //await context.PostAsync($"- Id: {context.Activity.From.Id}");
            //await context.PostAsync($"- From: {context.Activity.From.Name}");            
            //await context.PostAsync($"- ChannelData: {new JavaScriptSerializer().Serialize(context.Activity.ChannelData)}");            

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
                var continueActiveTaskOption = new[] { "Yes", "No" };
                var continueTaskDialog = new PromptDialog.PromptChoice<string>(continueActiveTaskOption, $"I assume you are still working on {currentTask.Description} right?  ", null, 3);
                context.Call(continueTaskDialog, AfterConfirmActiveTask);
                return;
            }

            if (DbContext.Tasks.Count <= 1)
            {
                await context.PostAsync("So what are you working on?");
                context.Wait(MessageReceived);
                return;
            }

            var suggestTask = DbContext.Tasks.OrderByDescending(i => i.Created).First();
            context.UserData.SetValue(DataKeyManager.SuggestTask, suggestTask);

            var promptOptions = new[] { "Yes", "No", "List other pending tasks" };
            var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {suggestTask.Description}? ", null, 3);
            context.Call(dialog, AfterConfirmSuggestTask);            
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
            EntityRecommendation taskDescriptionEntity;
            if (result.TryFindEntity(LuisEntities.TaskTitle, out taskDescriptionEntity))
            {
                var description = PreProcessTitleEntity(taskDescriptionEntity);

                var tasks = DbContext.Tasks.Where(i => string.Equals(description, i.Description, StringComparison.CurrentCultureIgnoreCase));

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

                    await context.PostAsync($"OK, I'll pause this {string.Join(",", tagStrings)} {currentTask.Description} here.");
                }

                context.UserData.SetValue(DataKeyManager.CurrentTask, task);
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
            DbContext.Tasks.RemoveAll(i => i.AddedByUserId == context.Activity.From.Id);
            await context.PostAsync("User data has been deleted");
        }

        [LuisIntent("ListTasks")]
        public async Task ListTasks(IDialogContext context, LuisResult result)
        {
            TaskViewModel task;
            if (context.UserData.TryGetValue(DataKeyManager.CurrentTask, out task))
            {
                await context.PostAsync($"Current tasks are: {task.Description}");
            }

            if (!DbContext.Tasks.Any())
            {
                await context.PostAsync($"You dont have any task");
                context.Wait(MessageReceived);
                return;
            }

            await context.PostAsync("You have following task:");
            foreach (var taskViewModel in DbContext.Tasks)
            {
                await context.PostAsync($"- {taskViewModel.Description}");
            }
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
            
            context.UserData.SetValue(DataKeyManager.PausedTask, currentTask);
            await context.PostAsync($"{currentTask.Description} has been paused, you can type 'Resume timer' to continue your task");
            context.Wait(MessageReceived);
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

            var task = DbContext.Tasks.Find(i => i.Id == currentTask.Id);
            DbContext.Tasks.Remove(task);

            await context.PostAsync($"{currentTask.Description} has been stoped and removed from your task list.");

            var remainTaskCount = DbContext.Tasks.Count;

            if (remainTaskCount == 0)
            {
                await context.PostAsync("So, what are you doing now?");
                context.Wait(MessageReceived);
            }
            else if (remainTaskCount == 1)
            {
                context.UserData.SetValue(DataKeyManager.SuggestFocusTask, DbContext.Tasks.First());

                var promptOptions = new[] {"Yes", "Not yet"};
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {DbContext.Tasks.First() .Description}", null, 3);
                context.Call(dialog, AfterOfferSuggestFocusOnTask);
            }
            else
            {
                var promptOptions = new[] { "Yes", "List tasks pendding" };
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"Shall we start on {DbContext.Tasks.First().Description}", null, 3);
                context.Call(dialog, AfterOfferSuggestFocusOnTask);
            }
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

            var taskTitle = PreProcessTitleEntity(taskTitleWithTagsEntity).Replace("#", string.Empty);

            var tasks = DbContext.Tasks.Where(i => string.Compare(i.Description, taskTitle, StringComparison.CurrentCultureIgnoreCase) == 0);
            if (!tasks.Any())
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
            }
            context.Wait(MessageReceived);
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

                    await context.PostAsync($"Timer has stared for: {suggestTask.Description}");
                    context.Wait(MessageReceived);
                    break;
                case "Not yet":
                    await context.PostAsync("No worries buddy you are the boss here! tell me what else is to do...");
                    context.Wait(MessageReceived);
                    break;
                case "List tasks pendding":
                    var penddingTaskOptions = DbContext.Tasks.Select(i => i.Description);
                    var dialog = new PromptDialog.PromptChoice<string>(penddingTaskOptions, "Here are your pendding tasks: ", null, 3);
                    context.Call(dialog, AfterChoseTaskFromPendingList);
                    break;
            }

            context.UserData.RemoveValue(DataKeyManager.SuggestFocusTask);            
        }

        private async Task AfterChoseTaskFromPendingList(IDialogContext context, IAwaitable<string> result)
        {
            var taskDescription = await result;
            var task = DbContext.Tasks.First(i => string.Compare(taskDescription, i.Description, StringComparison.CurrentCultureIgnoreCase)==0);

            context.UserData.SetValue(DataKeyManager.CurrentTask, task);
            await context.PostAsync($"Timer has started for {task.Description}");
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

                    var task = DbContext.Tasks.First(i => i.Id == currentTask.Id);
                    task.Tags = currentTags;
                    if (currentTags.Count == 1)
                    {
                        await context.PostAsync($"{currentTags.Count} has been linked to task");
                    }
                    else
                    {
                        await context.PostAsync($"{currentTags.Count} have been linked to task");
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

                    var task = DbContext.Tasks.First(i => i.Id == currentTask.Id);
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
                    context.UserData.RemoveValue(DataKeyManager.CurrentTask);
                    await context.PostAsync("Timer has stoped");
                    await context.PostAsync("So what are you working on?");
                    break;
            }
            context.Wait(MessageReceived);
        }

        private async Task AfterConfirmSuggestTask(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "Yes":
                    await context.PostAsync("I will start timer");
                    context.Wait(MessageReceived);
                    break;
                case "No":                    
                    await context.PostAsync("So what are you working on?");
                    break;
                case "List other pending tasks":
                    var suggestTask = context.UserData.Get<TaskViewModel>(DataKeyManager.SuggestTask);

                    var promptOptions = DbContext.Tasks.Where(i => i.Id != suggestTask.Id).Select(i => i.Description);

                    var dialog = new PromptDialog.PromptChoice<string>(promptOptions, "Other Tasks:", null, 3);
                    context.Call(dialog, AfterSelectTask);
                    break;
            }
        }

        private async Task AfterSelectTask(IDialogContext context, IAwaitable<string> result)
        {
            var taskDescription = await result;
            var selectedTask = DbContext.Tasks.First(i => i.Description == taskDescription);
            context.UserData.RemoveValue(DataKeyManager.SuggestTask);
            context.UserData.SetValue(DataKeyManager.CurrentTask, selectedTask);
            await context.PostAsync($"I will start timer for task: {taskDescription}");
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
                Created = DateTimeOffset.UtcNow
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

    }
}