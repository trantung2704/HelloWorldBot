using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using HelloWorldBot.Dialogs;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

namespace HelloWorldBot.Controllers
{
    [BotAuthentication]
    public class MessagesController : ApiController
    {
        public async Task<HttpResponseMessage> Post([FromBody]Activity activity)
        {
            if (activity.Type == ActivityTypes.Message)
            {
                await Conversation.SendAsync(activity, () => new DayNinjaDialog());
            }
            else
            {
                await HandleSystemMessage(activity);
            }
            // Return response
            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }

        private async Task <Activity> HandleSystemMessage(Activity activity)
        {
            if (activity.Type == ActivityTypes.DeleteUserData)
            {
                activity.Type = ActivityTypes.Message;
                activity.Text = "Delete User Data";
                await Conversation.SendAsync(activity, () => new DayNinjaDialog());
            }
            else if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                if (activity.MembersAdded.Any(m => m.Id == activity.Recipient.Id))
                {
                    activity.Type = ActivityTypes.Message;
                    activity.Text = "Conversation Update";
                    await Conversation.SendAsync(activity, () => new DayNinjaDialog());
                }
            }
            else if (activity.Type == ActivityTypes.ContactRelationUpdate)
            {
                if (activity.MembersAdded.Any(m => m.Id == activity.Recipient.Id))
                {
                    activity.Type = ActivityTypes.Message;
                    activity.Text = "Conversation Update";
                    await Conversation.SendAsync(activity, () => new DayNinjaDialog());
                }
            }
            else if (activity.Type == ActivityTypes.Typing)
            {
            }
            else if (activity.Type == ActivityTypes.Ping)
            {
                if (activity.MembersAdded.Any(m => m.Id == activity.Recipient.Id))
                {
                    activity.Type = ActivityTypes.Message;
                    activity.Text = "Conversation Update";
                    await Conversation.SendAsync(activity, () => new DayNinjaDialog());
                }
            }

            return null;
        }
    }
}