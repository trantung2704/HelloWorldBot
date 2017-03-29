using System;
using Microsoft.Bot.Builder.FormFlow;

namespace HelloWorldBot.Queries
{
    [Serializable]
    public class TaskQuery
    {
        [Prompt("What is the task would you like to add?")]
        public string Description { get; set; }
    }
}