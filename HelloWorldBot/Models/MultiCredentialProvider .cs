using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Bot.Connector;

namespace HelloWorldBot.Models
{
    public class MultiCredentialProvider : ICredentialProvider
    {
        public Dictionary<string, string> Credentials = new Dictionary<string, string>
        {
            { "f0182014-41fd-4465-8d1b-901c640955a0", "pcofL2H9oZXbhr97oZyadfr" },
        };

        public Task<bool> IsValidAppIdAsync(string appId)
        {
            return Task.FromResult(this.Credentials.ContainsKey(appId));
        }

        public Task<string> GetAppPasswordAsync(string appId)
        {
            return Task.FromResult(this.Credentials.ContainsKey(appId) ? this.Credentials[appId] : null);
        }

        public Task<bool> IsAuthenticationDisabledAsync()
        {
            return Task.FromResult(!this.Credentials.Any());
        }
    }
}