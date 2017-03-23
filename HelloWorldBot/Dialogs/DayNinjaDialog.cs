using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Connector;

namespace HelloWorldBot.Dialogs
{
    [Serializable]
    [LuisModel("21d7c272-725e-4794-90c7-f17fef5e7e78", "f2033cc0ddc94c7fad7e27797a618db0")]
    public class CreateNewTask : LuisDialog<object>
    {
        [LuisIntent("None")]
        public async Task None(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("Sorry I dont understand you.");
            context.Wait(MessageReceived);
        }
    }
}