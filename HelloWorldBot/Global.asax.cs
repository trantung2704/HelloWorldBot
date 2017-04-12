using System.Security.Claims;
using System.Web.Http;
using Autofac;
using DayNinjaBot.Business.Services;
using DayNinjaBot.Data;
using HelloWorldBot.Dialogs;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Internals.Fibers;
using Microsoft.Bot.Connector;

namespace HelloWorldBot
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
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

            builder.Update(Conversation.Container);
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }
    }
}
