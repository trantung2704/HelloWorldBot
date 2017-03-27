using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chronic;
using DayNinjaBot.Business;
using DayNinjaBot.Business.Services;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
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

        [LuisIntent("ConversationUpdate")]
        public async Task ConversationUpdate(IDialogContext context, LuisResult result)
        {
            var userData = context.UserData;
            try
            {
                var lastUpdate = userData.Get<DateTimeOffset>("LastUpdate");
                if (lastUpdate.Date != DateTimeOffset.UtcNow.Date)
                {
                    await context.PostAsync("Hey Ninja, ready to be productive today");
                    await context.PostAsync("So what are you working on?");
                    userData.SetValue("LastUpdate", DateTimeOffset.UtcNow);
                }
                else
                {
                    await context.PostAsync("So what are you working on?");
                }
            }
            catch (Exception)
            {
                await context.PostAsync("Hey Ninja, ready to be productive today");
                await context.PostAsync("So what are you working on?");
                userData.SetValue("LastUpdate", DateTimeOffset.UtcNow);
            }            

            
            context.Wait(MessageReceived);
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
            context.Wait(MessageReceived);
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

            const string defineTaskKeyword = "working on";
            var text = context.Activity.AsMessageActivity().Text;

            var startTaskDescrioptionIndex = text.LastIndexOf(defineTaskKeyword, StringComparison.InvariantCultureIgnoreCase);
            var taskDescription = text.Substring(startTaskDescrioptionIndex + defineTaskKeyword.Length + 1);
            
            var tags = taskDescription.Split(' ').Where(i=>i.StartsWith("#")).Select(i => i.Substring(1));
            taskDescription = taskDescription.ReplaceAll("#", string.Empty);
            var capitalisedWords = taskDescription.Split(' ').Where(i => char.IsUpper(i[0]));
            context.UserData.SetValue(DataKeyManager.CurrentTaskDescription, taskDescription);
            
            var newTask = new TaskViewModel
                          {
                              Description = taskDescription,
                              AddedByUserId = context.Activity.From.Id
                          };
            taskService.CreateNewTask(newTask);
            context.UserData.SetValue(DataKeyManager.CurrentTask, newTask);

            await context.PostAsync($"Task with description: '{taskDescription}' has been created");

            if (!tags.Any() && !knownAboutTags)
            {                
                var promptOptions = new[] {"Tell me more", "Yes, I know"};
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, "Did you know if you can markup tasks by writing #hashtags?", null, 3);
                context.Call(dialog, AfterOfferKnowAboutTags);
            }
            else if(tags.Any())
            {
                context.UserData.SetValue(DataKeyManager.CurrentTags, tags);
                var promptOptions = new[] { "Thanks", "What are tags?" };

                var tagsString = string.Empty;
                foreach (var tag in tags)
                {
                    tagsString += $"{tag}, ";
                }                
                
                var dialog = new PromptDialog.PromptChoice<string>(promptOptions, $"I see you mentioned {tagsString}, I'll tag for your stats", null, 3);
                context.Call(dialog, AfterOfferTags);
            }
            else if(!tags.Any() && capitalisedWords.Any())
            {
                context.UserData.SetValue(DataKeyManager.CurrentTags, capitalisedWords);
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

        }

        [LuisIntent("Greeting")]
        public async Task Greeting(IDialogContext context, LuisResult result)
        {
            var userData = context.UserData;
            try
            {
                var lastUpdate = userData.Get<DateTimeOffset>("LastUpdate");
                if (lastUpdate.Date != DateTimeOffset.UtcNow.Date)
                {
                    await context.PostAsync("Hey Ninja, ready to be productive today");
                    await context.PostAsync("So what are you working on?");
                    userData.SetValue("LastUpdate", DateTimeOffset.UtcNow);
                }
                else
                {
                    await context.PostAsync("So what are you working on?");
                }
            }
            catch (Exception)
            {
                await context.PostAsync("Hey Ninja, ready to be productive today");
                await context.PostAsync("So what are you working on?");
                userData.SetValue("LastUpdate", DateTimeOffset.UtcNow);
            }
            context.Wait(MessageReceived);
        }

        [LuisIntent("DeleteUserData")]
        public async Task DeleteUserData(IDialogContext context, LuisResult result)
        {
            context.UserData.Clear();
            DbContext.Tasks.RemoveAll(i => i.AddedByUserId == context.Activity.From.Id);
            await context.PostAsync("User data has been deleted");
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
                    if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
                    {
                        await context.PostAsync("There is an issue when process tags");
                        context.Wait(MessageReceived);
                        break;
                    }

                    var task = DbContext.Tasks.First(i => i.Id == currentTask.Id);
                    task.Tags = currentTags;
                    await context.PostAsync($"{currentTags.Count} tags has been link to task");

                    context.UserData.SetValue(DataKeyManager.CurrentTags, new List<string>());
                    context.Wait(MessageReceived);
                    break;
            }
        }

        private async Task AfterOfferTagCapitalWords(IDialogContext context, IAwaitable<string> result)
        {
            switch (await result)
            {
                case "No":
                    context.Wait(MessageReceived);
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
                    if (!context.UserData.TryGetValue(DataKeyManager.CurrentTask, out currentTask))
                    {
                        await context.PostAsync("There is an issue when process tags");
                        context.Wait(MessageReceived);
                        break;
                    }

                    var task = DbContext.Tasks.First(i => i.Id == currentTask.Id);
                    task.Tags = currentTags;
                    await context.PostAsync($"{currentTags.Count} tags has been link to task");

                    context.UserData.SetValue(DataKeyManager.CurrentTags, new List<string>());
                    context.Wait(MessageReceived);
                    break;
            }

        }
    }
}