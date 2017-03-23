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
                var userId = activity.From.Id;
                var haveGreeting = DataManager.GetData<bool>(userId, "HaveGreeting");
                // Create text for a reply message   
                StringBuilder strReplyMessage = new StringBuilder();
                if (haveGreeting == false)
                {
                    strReplyMessage.Append("Hi, how are you today?");
                    DataManager.SaveData(userId, "HaveGreeting", true);

                    ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));
                    Activity replyMessage = activity.CreateReply(strReplyMessage.ToString());
                    await connector.Conversations.ReplyToActivityAsync(replyMessage);
                }
                else
                {
                    await Conversation.SendAsync(activity, () => new DayNinjaDialog());
                }
            }
            else
            {
                Activity replyMessage = HandleSystemMessage(activity);
                if (replyMessage != null)
                {
                    ConnectorClient connector = new ConnectorClient(new Uri(activity.ServiceUrl));
                    await connector.Conversations.ReplyToActivityAsync(replyMessage);
                }
            }
            // Return response
            var response = Request.CreateResponse(HttpStatusCode.OK);
            return response;
        }
        private Activity HandleSystemMessage(Activity message)
        {
            if (message.Type == ActivityTypes.DeleteUserData)
            {
                DataManager.DeleteData(message.From.Id);
            }
            else if (message.Type == ActivityTypes.ConversationUpdate)
            {
                // Handle conversation state changes, like members being added and removed
                // Use Activity.MembersAdded and Activity.MembersRemoved and Activity.Action for info
                // Not available in all channels
            }
            else if (message.Type == ActivityTypes.ContactRelationUpdate)
            {
                // Handle add/remove from contact lists
                // Activity.From + Activity.Action represent what happened
            }
            else if (message.Type == ActivityTypes.Typing)
            {
                // Handle knowing tha the user is typing
            }
            else if (message.Type == ActivityTypes.Ping)
            {
            }

            return null;
        }

        
    }
}