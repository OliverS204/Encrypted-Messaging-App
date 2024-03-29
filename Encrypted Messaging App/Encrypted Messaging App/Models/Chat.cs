﻿using System;
using System.Collections.Generic;
using System.Numerics;
using Xamarin.Forms;
using System.Threading.Tasks;
using Encrypted_Messaging_App.Services;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using static Encrypted_Messaging_App.LoggerService;  // Error()

namespace Encrypted_Messaging_App
{
    public class Chat
    {
        IManageFirestoreService FirestoreService = DependencyService.Resolve<IManageFirestoreService>();  // Initilise Firetore Service

        // Public Attributes:
        public KeyData encryptionInfo { get; set; }
        public string title { get; set; } = "  ";
        public List<User> users = new List<User>();
        public string[] userIDs { get; set; } = new string[0];
        
        public Message[] messages { get => _messages; 
            set {
                if (id != null) { // If server instance:
                    // Get all new messages, give them the secret key and decrypt the content
                    Message[] diff = value.Except(_messages).ToArray();
                    foreach (Message addedMessage in diff)
                    {
                        if (addedMessage.secretKey == default(BigInteger))
                        {
                            addedMessage.secretKey = encryptionKey;
                        }
                        if (addedMessage.content == null)
                        {
                            addedMessage.DecryptContent();
                        }
                    }
                }
                _messages = value;
            } 
        }
        private Message[] _messages = new Message[0];

        public string id { get { return _id; } 
            set {
                _id = value;
                
            } 
        }
        private string _id;

        public Action headerChangedAction;        // Fired when you change Title/Id      -> refresh chatList + chatPage
        public Action<int[]> contentChangedAction;// Fired when you change/send messages -> refresh chatPage  int[]- Messages index to update (empty if all)
        public bool showDecryptedMessages = true;
        public int titleMaxLength = 25;

        // Private Attributes
        private BigInteger encryptionKey;
        

        public Chat() { }

        // Constructors:
        public void SetID(string chatID)
        {
            id = chatID;
            if (encryptionKey.Equals(default(BigInteger))) { encryptionKey = getSecretKey(); }    // If encryption key is not set yet, fetch it
        }
        private async Task setUserIDsAndUsers(string[] newUserIDs)  // Sync users with userIDs (F)
        {
            string[] addedIDs = newUserIDs.Except(userIDs).ToArray();
            string[] removedIDs = userIDs.Except(newUserIDs).ToArray();
            userIDs = newUserIDs;

            foreach (string userID in removedIDs) { users.RemoveAll(user => user.Id == userID); }
            foreach (string userID in addedIDs) { await fetchAndAddUser(userID); }
        }

        // Getters: 
        public string GetPrivateKeyStr(int keyLength)
        {   // Used to display the private key to the user (Popup)
            byte[] sharedKey = SHA256.Create().ComputeHash(encryptionKey.ToByteArray());
            Array.Resize(ref sharedKey, keyLength/8);

            return BitConverter.ToString(sharedKey).Replace("-", "");
        }
        public string GetUsersStr()
        {   // Used to display a string with the users  (Popup)
            return generateDefaultTitle();
        }


        //  --Firestore Fetch--  \\
        public async Task<bool> FetchAndListen()
        {   // Adds a listener which updates the chat (listener fires once at initilisation)
            if (id != null)
            {   TaskCompletionSource<bool> chatUpdatedTask = new TaskCompletionSource<bool>();
                bool success = FirestoreService.ListenData<Chat>("Chat", async (result) => { if(result == null) { return; }
                    await updateChat((Chat)result); chatUpdatedTask.TrySetResult(true);
                }, listenerKey:"Chat", arguments: ("CHATID", id));
                await chatUpdatedTask.Task;
                return success;
            }   return false;
        }
        private async Task<bool> fetchAndAddUser(string userId)
        {
            // When a userID is added, fetch the associated User
            User result = await FirestoreService.UserFromId(userId);
            if (result == null) { Error($"Unable to add user: {userId} (chat: {id})"); return false; }
            users.Add(result);
            return true;
        }
        public void RemoveListener()
        {   // Removes all listeners added for the chat
            FirestoreService.RemoveListenersByKey("Chat");
        }


        //  --Firestore Update/Create--  \\
        public async Task<bool> InitiliseFirestore()
        {   // When the chat is created through a friend request accept, add it to the server
            id = "";
            if (!propertiesDefined()) { Error("Not all properties are defined: Invalid Chat Object"); return false; }
            (bool success, string message) result = await FirestoreService.WriteObject(this, "Chat", ("CHATID", ""));

            if (result.success) {
                id = result.message;
                _ = FirestoreService.UpdateString(id, "Chat", ("CHATID", id));
            }
            return result.success;

        }
        public async Task<bool> AddToUserFirestore(string CUserID)
        {
            // When a user accepts a request + manages accepted request, the chatID is added to the chatIDs in the user's firestore
            (bool success, string message) result = await FirestoreService.AddToArray(id, "CUser/chatsID");
            if (!result.success)
            {
                Error($"Unable to add chat-{id} to firestore-{CUserID}:   {result.message}");
            }
            return result.success;
        }
        public async Task<bool> UpdateTitle(string newTitle)
        {   // When the user edits the title (ChatPage)
            (bool success, string message) result = await FirestoreService.UpdateString(newTitle, "Chat/title", ("CHATID", id));   //FirestoreService.GetPath("Chat", arguments: ("CHATID", id)) + "/Title"
            if (!result.success) { Error($"Can\'t change title of {id} to: {newTitle}      {result.message}"); }
            return result.success;
        }
        public async Task<bool> SendMessage(string content, User authorUser)
        {   if (encryptionKey.Equals(default(BigInteger))) { encryptionKey = getSecretKey(); }
            // Initilises message + encrypts it:
            Message newMessage = new Message(content, authorUser, userIDs, encryptionKey);     
            bool encryptSuccess = newMessage.EncryptContent();
            if (!encryptSuccess) { Error("Unable to encrypt message content"); return false; }
            (bool success, string message) result = await FirestoreService.AddMessageToChat(newMessage, id);
            if (!result.success) { Error($"Unable to add message- to chat{id}:   {result.message}"); }
            return result.success;
        }

        // --Firestore Delete--  \\
        public async Task<bool> Leave()
        {
            (bool success, string message) result = await FirestoreService.RemoveFromArray(id, "CUser/chatsID");
            if (!result.success) { Error($"Couldn't remove user from chat:    {result.message}"); }
            result = await FirestoreService.DeleteObject("Chat", ("CHATID", id));
            if (!result.success) { Error($"Couldn't delete chat:    {result.message}"); return false; }
            return result.success;
        }


        // Private Methods:
        private async Task updateChat(Chat newChat)
        {   // Sets all properties from another chat object (from firestore)
            bool headerChanged = false;
            bool contentChanged = false;
            List<int> editedMessages = new List<int>();

            if (newChat.messages == null) { Error($"Invalid messages retrieved for: {id}"); }
            else if(messages != newChat.messages && messages != null) { 
                contentChanged = true;
                
                if(contentChangedAction != null)
                {
                    for (int i = 0; i < newChat.messages.Length; i++)
                    {
                        if (messages == null || i > messages.Length - 1)
                        {
                            editedMessages.Add(i);
                        }
                        else if (messages[i].encryptedContent != newChat.messages[i].encryptedContent)
                        {
                            editedMessages.Add(i);
                        }
                    }
                }
                messages = newChat.messages;
            }
            else if (messages != newChat.messages)
            { // If there are no messages before, add all messages
                contentChanged = true;
                for(int i=0; i<newChat.messages.Length; i++) { editedMessages.Add(i); }
                messages = newChat.messages;
            }

            if (newChat.userIDs == null || newChat.userIDs.Length < 2) { Error($"Invalid user IDs retrieved for: {id}"); }
            else { await setUserIDsAndUsers(newChat.userIDs); }
            

            if (users == null || users.Count < 2) { Error($"Invalid users retrieved from userIDs for: {id}"); }
            else if (userIDs.Length != users.Count) { Error($"Invalid users from userID for: {id}  ({userIDs.Length} userID vs {users.Count} users)"); }
            
            

            if(title != newChat.title)
            {
                title = newChat.title;
                if (title == null || title.Length == 0)
                {
                    if (newChat.userIDs != null) { title = generateDefaultTitle(); }
                    else { title = "  "; }
                }
                headerChanged = true;
            }

            if(encryptionInfo == null)  // Shouldn't be changed after chat creation
            {  
                encryptionInfo = newChat.encryptionInfo;
            }
            
            // Invokes the events which can refresh chatList / chatPage
            if (headerChanged && headerChangedAction != null)
            {
                headerChangedAction.Invoke();
            }
            if (contentChanged && contentChangedAction != null)
            {
                contentChangedAction.Invoke(editedMessages.ToArray());
            }
        }
        private bool propertiesDefined()
        {   // Checks if all the properties are defined before adding it to firestore
            bool defined = true;
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if (prop.GetValue(this, null) == null)
                {
                    Console.WriteLine($"{prop} is not defined!!");
                    defined = false;
                }
            }
            return defined;
        }
        private string generateDefaultTitle()
        {    // Generates a title from the usernames in the chat
            string title = "";
            foreach (User user in users)
            {
                title = title + user.Username + ", ";
            }
            if(users.Count > 0)
            {
                title = title.Remove(title.Length - 2);
                if(title.Length > titleMaxLength-2)
                {
                    title = title.Substring(0, titleMaxLength - 2) + "..";
                }
                return title;
            }
            else {  return "Empty Chat";  }
        }
        private BigInteger getSecretKey()
        {   // Get's the secret key from ChatKeys (phone local storage) + converts it to BigInt
            if (id == null) { return default; }

            string keyString = SQLiteService.ChatKeys.Get(id);
            if(keyString.Length == 0) { return default; }

            bool resultBool = BigInteger.TryParse(keyString, out BigInteger resultInt);
            if (!resultBool) { return default; }
            else { return resultInt; }
        }


        // Deprecated:
        public void SetEncryptKey(BigInteger chatEncryptKey)
        {
            encryptionKey = chatEncryptKey;
        }
        public async Task<bool> RefreshUsersFromIDs()
        {
            users = new List<User>();
            bool success = true;
            foreach (string userID in userIDs)
            {
                success &= await fetchAndAddUser(userID);
            }
            return success;
        }
        public void CreateFromData(KeyData chatEncryptInfo, BigInteger chatEncryptKey, User[] chatUsers)
        {
            encryptionInfo = chatEncryptInfo;
            users = chatUsers.ToList();
            userIDs = new string[chatUsers.Length];
            for (int i = 0; i < chatUsers.Length; i++)
            {
                userIDs[i] = chatUsers[i].Id;
            }
            encryptionKey = chatEncryptKey;
            messages = new Message[] { };
            title = "";
        }
        public async Task<bool> GetFromServer()
        {
            if (id == null) { return false; }

            (bool success, object result) response = await FirestoreService.FetchData<Chat>("Chat", arguments: ("CHATID", id));
            if (!response.success) { return false; }

            await updateChat((Chat)response.result);
            return true;
        }
        public async Task<bool> FetchAndListenOld()
        {
            bool result = await FetchAndListen();
            if (!result) { Error($"Can't get chat from server: {id}"); return false; }

            return result;
        }
    }
}
