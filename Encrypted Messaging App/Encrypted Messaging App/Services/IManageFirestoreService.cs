﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Encrypted_Messaging_App
{
    public interface IManageFirestoreService
    {
        string GetPath(string type, params (string, string)[] arguments);

        Task<(bool, string)> InitiliseUser(string Username);

        Task<(bool, string)> InitiliseChat(Chat chat);

        Task<(bool, string)> SendAcceptedRequest(string requestUserID, AcceptedRequest ARequest);
        Task<(bool, string)> SendRequest(Request request, string requestUserID);

        Task<(bool, string)> AddToArray(string newItem, string path);

        Task<(bool, string)> UpdateString(string newString, string path);



        Task<(bool, object)> FetchData<ReturnType>(string type, params (string, string)[] arguments);
        bool ListenData<ReturnType>(string type, Action<object> action, string changeType = null, bool ignoreInitialEvent = false, params (string, string)[] arguments);

        Task<User> UserFromId(string id);
        Task<User> UserFromUsername(string username);

        void RemoveListeners();
    }
}
