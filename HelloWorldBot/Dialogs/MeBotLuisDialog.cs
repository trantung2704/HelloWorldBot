using System;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Luis;
using Microsoft.Bot.Builder.Luis.Models;

namespace HelloWorldBot.Dialogs
{
    [Serializable]
    [LuisModel("76c5a0ea-fa6b-4094-9007-636e8989feca", "a5f4459c9dee4f65a535203c555f9531")]
    public class MeBotLuisDialog : LuisDialog<object>
    {
        [LuisIntent("HowAreYou")]
        public async Task HowAreYou(IDialogContext context, LuisResult result)
        {
            await context.PostAsync("Great! Thank for asking");
            context.Wait(MessageReceived);
        }
    }
}