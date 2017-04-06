using System;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(HelloWorldBot.Startup))]

namespace HelloWorldBot
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureHangFire(app);
        }
        private void ConfigureHangFire(IAppBuilder app)
        {
            Hangfire.GlobalConfiguration.Configuration.UseSqlServerStorage("HangFireConnection");
            app.UseHangfireServer();
            app.UseHangfireDashboard();
            using (var connection = JobStorage.Current.GetConnection())
            {
                foreach (var recurringJob in connection.GetRecurringJobs())
                {
                    RecurringJob.RemoveIfExists(recurringJob.Id);
                }
            }
        }
    }
}
