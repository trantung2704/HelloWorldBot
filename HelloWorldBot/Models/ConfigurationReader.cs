using System.Configuration;

namespace HelloWorldBot.Models
{
    public static class ConfigurationReader
    {
        public static string AppAccessToken => ConfigurationManager.AppSettings["AppAccessToken"];        

        public static string BotApiEndpoint => ConfigurationManager.AppSettings["BotApiEndpoint"];        

        public static string BotMicrosoftAppId => ConfigurationManager.AppSettings["MicrosoftAppId"];        

        public static string BotMicrosoftAppPassword => ConfigurationManager.AppSettings["MicrosoftAppPassword"];        
    }
}