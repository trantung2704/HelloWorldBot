using System.Configuration;

namespace HelloWorldBot.Models
{
    public static class ConfigurationReader
    {
        public static string AppAccessToken => ConfigurationManager.AppSettings["AppAccessToken"];        
    }
}