using System.Web.Http;
using Autofac;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Internals;
using Microsoft.Bot.Connector;

namespace HelloWorldBot
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);

            var builder = new ContainerBuilder();
            builder.Register(c => new CachingBotDataStore(c.ResolveKeyed<IBotDataStore<BotData>>(typeof(ConnectorStore))
                                                          , CachingBotDataStoreConsistencyPolicy.LastWriteWins))
                   .As<IBotDataStore<BotData>>()
                   .AsSelf()
                   .InstancePerLifetimeScope();
            builder.Update(Conversation.Container);
        }
    }
}
