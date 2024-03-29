﻿using System;
using System.Threading.Tasks;

namespace Encrypted_Messaging_App
{
    public interface IManageFirestoreService
    {
        Task<(bool, string)> InitiliseUser(string Username);

        Task<(bool, string)> SendAcceptedRequest(string requestUserID, AcceptedRequest ARequest);

        Task<(bool, string)> AddToArray(object newItem, string pathInfo, params (string, string)[] arguments);
        Task<(bool, string)> RemoveFromArray(string oldItem, string pathInfo, params (string, string)[] arguments);

        Task<(bool, string)> UpdateString(string newString, string pathInfo, params (string, string)[] arguments);
        Task<(bool, string)> AddMessageToChat(Message message, string chatID);


        Task<(bool, object)> FetchData<ReturnType>(string pathInfo, params (string, string)[] arguments);

        bool ListenData<ReturnType>(string type, Action<object> action, string changeType = null,string listenerKey = "", params (string, string)[] arguments);

        Task<(bool, string)> WriteObject(object obj, string pathInfo, params (string, string)[] arguments);

        Task<(bool, string)> DeleteObject(string pathInfo, params (string, string)[] arguments);



        Task<User> UserFromId(string id);
        Task<User> UserFromUsername(string username);

        void RemoveAllListeners();
        void RemoveListenersByKey(string key);
    }
}
