using System;
using System.Collections.Generic;
using System.Linq;

namespace HelloWorldBot
{
    public class DataManager
    {
        private static readonly Lazy<DataManager> Lazy = new Lazy<DataManager>(() => new DataManager());

        public  static DataManager Instance => Lazy.Value;

        private static readonly List<UserData> userDatas = new List<UserData>();

        public static void SaveData(string userId, string key, object value)
        {
            var userData = userDatas.FirstOrDefault(i => i.UserId == userId && i.Key == key);

            if (userData != null)
            {
                userData.Value = value;
            }
            else
            {
                userDatas.Add(new UserData
                {
                    UserId = userId,
                    Key = key,
                    Value = value
                });
            }
        }

        public static T GetData<T>(string userId, string key)
        {
            var userData = userDatas.FirstOrDefault(i => i.UserId == userId && i.Key == key);
            if (userData != null)
            {
                return (T)userData.Value;
            }
            return default(T);
        }

        public static void DeleteData(string userId)
        {
            userDatas.RemoveAll(i => i.UserId == userId);
        }
    }


    public class UserData
    {
        public string UserId { get; set; }

        public string Key { get; set; }

        public object Value { get; set; }
    }
}