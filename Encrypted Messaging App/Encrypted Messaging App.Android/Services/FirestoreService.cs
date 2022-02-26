﻿using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xamarin.Forms;
using Encrypted_Messaging_App.Droid;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
//using Google.Cloud.Firestore;
using Firebase.Firestore;
using Android.Gms.Extensions;
using Encrypted_Messaging_App.Droid.Resources;
using Java.Util;
using System.Reflection;
using static Encrypted_Messaging_App.LoggerService;

[assembly: Dependency(typeof(Encrypted_Messaging_App.Droid.ManageFirestoreService))]


namespace Encrypted_Messaging_App.Droid
{
    class ManageFirestoreService : IManageFirestoreService
    { // Main manager for everything firestore

        private Dictionary<string, string> firestorePaths = new Dictionary<string, string>
        {
            {"Requests", $"requests/pending/[CUSERID]" },
            {"AcceptRequests", $"requests/accepted/[CUSERID]" },
            {"CUser", $"users/[CUSERID]" },
            {"UserFromUsername", $"usersPublic/<USERNAME>" },
            {"UserFromId", $"usersPublicID/<USERID>" },
            {"Chat", $"chats/<CHATID>" },
            {"ChatsID", $"users/[CUSERID]/chatsID" }
        };

        private Dictionary<string, TaskCompletionSource<bool>> uncompletedFetchTasks = new Dictionary<string, TaskCompletionSource<bool>>();
        private List<IListenerRegistration> Listeners = new List<IListenerRegistration>();
        private Dictionary<string, List<IListenerRegistration>> ListenerDict = new Dictionary<string, List<IListenerRegistration>>(); 
        private int randomNum = new System.Random().Next();



        private string GetPath(string pathInfo, params (string, string)[] arguments)
        {            
            if (pathInfo.Split("/").Length > 1)   // Allows you to do:  CUser/chatsID
            {
                Debug($"Splitting path: {pathInfo}", 1, true);
                string[] pathParts = pathInfo.Split("/");
                string pathPt1 = GetPath(pathParts[0], arguments);
                string pathPt2 = GetPath(string.Join("/", pathParts.Skip(1).ToArray()));
                if (pathPt1 == null || pathPt2 == null) { Error("Splitting path failed", 1); return null; }
                else {
                    //Debug($"Path generated: {pathPt1}/{pathPt2}", 1);
                    return pathPt1 + "/" + pathPt2; 
                }
            } 


            string type = pathInfo;
            Dictionary<string, string> dictArgs = arguments.ToDictionary(arg => arg.Item1, arg => arg.Item2);

            if (!firestorePaths.ContainsKey(type) || firestorePaths[type] == null)
            {
                //Error($"Invalid type passed: {type}, can't get path", 1);
                return parseLevelArgument(type, dictArgs);
            }


            
            string[] pathLevels = firestorePaths[type].Split("/");
            for (int i=0; i<pathLevels.Length; i++)
            {
                pathLevels[i] = parseLevelArgument(pathLevels[i], dictArgs);
                
                if(pathLevels[i] == null)     { return null; }
                if(pathLevels[i].Length == 0) { pathLevels = pathLevels.Where((s, index) => index!=i).ToArray(); ; i--; }
            }
            Debug($"Path expanded: {type} -> {string.Join("/", pathLevels)}", 1);
            return string.Join("/", pathLevels);
        }
        private string parseLevelArgument(string levelName, Dictionary<string, string> arguments)
        {
            if (levelName.StartsWith("<") && levelName.EndsWith(">"))
            {
                levelName = levelName.TrimStart('<').TrimEnd('>');

                if (arguments == null) { Error($"No Arguments have been set (Expecting {levelName})", 1); return null; }
                else if (!arguments.ContainsKey(levelName)) { Error($"Missing Argument: {levelName}", 1); return null; }
                else
                {
                    levelName = arguments[levelName];
                }
            }
            else if (levelName.StartsWith("[") && levelName.EndsWith("]"))
            {
                levelName = levelName.TrimStart('[').TrimEnd(']');
                if (levelName == "CUSERID")
                {
                    if (arguments.ContainsKey("CUSERID")) { return parseLevelArgument("<CUSERID>", arguments); }
                    levelName = FirebaseAuth.Instance.CurrentUser.Uid;
                }
                else
                {
                    Error($"Unrecognised automatic argument: {levelName}", 1);
                }
            }
            return levelName;
        }
        private (bool success, DocumentReference docRef, CollectionReference collectRef) GetReferenceFromPath(string[] pathLevels)
        {
            if (pathLevels == null || pathLevels.Length == 0) { return (false, null, null); }


            CollectionReference collection = FirebaseFirestore.Instance.Collection(pathLevels[0]);
            DocumentReference document = null;

            foreach (string currPathLevel in pathLevels.Skip(1))
            {
                if (currPathLevel.Length == 0) { break; }
                if (document is null)
                {
                    document = collection.Document(currPathLevel);
                    collection = null;
                }
                else
                {
                    collection = document.Collection(currPathLevel);
                    document = null;
                }
            }
            return (true, document, collection);
        }
        private (bool success, DocumentReference docRef, CollectionReference collectRef) GetReferenceFromPath(string path)
        {
            return GetReferenceFromPath(path.Split('/'));
        }
        private DocumentChange.Type GetDocChangeType(string changeType)
        {
            if (changeType == "added") { return DocumentChange.Type.Added; }
            else if (changeType == "modified") { return DocumentChange.Type.Modified; }
            else if (changeType == "removed") { return DocumentChange.Type.Removed; }
            return null;
        }
        private bool isFieldType(Type returnType)          // Checks if a type is a field (e.g string, int)
        {
            return returnType.Namespace.StartsWith("System") || (returnType.IsArray && returnType.GetElementType().Namespace.StartsWith("System"));
        }
        private string popFieldName(ref string inputPath)  // Removes + returns last item from path
        {
            string[] inputArray = inputPath.Split("/");


            List<string> inputList = inputArray.ToList();
            string removedValue = inputArray[inputArray.Length - 1];
            inputList.RemoveAt(inputArray.Length - 1);
            inputPath = string.Join('/', inputList.ToArray());

            return removedValue;
        }



        //       General Functions:
        public Task<(bool, object)> FetchData<returnType>(string pathInfo, params (string, string)[] arguments)                                   // GET     
        {
            Debug($"Fetching Data for {pathInfo}:", 0, true);
            var tcs = new TaskCompletionSource<(bool, object)>();

            string path = GetPath(pathInfo, arguments);
            string fieldName = null;
            if (path is null)
            {
                tcs.TrySetResult((false, $"Invalid type passed: {pathInfo}, can't fetch data"));
                return tcs.Task;
            }
            if (isFieldType(typeof(returnType)))
            {
                fieldName = popFieldName(ref path);
            }

            (bool success, DocumentReference document, CollectionReference collection) reference = GetReferenceFromPath(path);
            if (!reference.success)
            {
                tcs.TrySetResult((false, $"Invalid path: {path}"));
            }
            else
            {
                if (reference.document != null) { reference.document.Get().AddOnCompleteListener(new OnCompleteListener(tcs, typeof(returnType))); }
                else { reference.collection.Get().AddOnCompleteListener(new OnCompleteListener(tcs, typeof(returnType))); }
            }
            return tcs.Task;

        }
        public bool ListenData<returnType>(string pathInfo, Action<object> action, string changeType = null, string listenerKey=" ", params (string, string)[] arguments) // LISTEN  
        {  // listenerKey = key used to bulk remove listeners (all listeners with the same key)
            Debug($"Listening Data for {pathInfo}:", 0, true);
            string path = GetPath(pathInfo, arguments);
            string fieldName = null;


            if (isFieldType(typeof(returnType)))
            {
                fieldName = popFieldName(ref path);
            }
            if (path is null) { return false; }
            DocumentChange.Type ChangeType = GetDocChangeType(changeType);

            (bool success, DocumentReference document, CollectionReference collection) reference = GetReferenceFromPath(path);


            if (reference.success)
            {
                IListenerRegistration currListenerReg;
                if (reference.document != null)
                {
                    currListenerReg = reference.document.AddSnapshotListener(new OnEventListener(typeof(returnType), action, ChangeType, fieldName));
                }
                else { currListenerReg = reference.collection.AddSnapshotListener(new OnEventListener(typeof(returnType), action, ChangeType, fieldName)); }
                Listeners.Add(currListenerReg);
                if (ListenerDict.ContainsKey(listenerKey)) { ListenerDict[listenerKey].Add(currListenerReg); }
                else { ListenerDict[listenerKey] = new List<IListenerRegistration> { currListenerReg }; }
                
                return true;
            }
            else
            {
                Error($"Invalid path: {path}");
                return false;
            }
        }
        public async Task<(bool, string)> WriteObject(object obj, string pathInfo, params (string, string)[] arguments)                           // WRITE   
        {
            string path = GetPath(pathInfo, arguments);
            if(path is null) { return (false, "Invalid path info"); }
            (bool success, DocumentReference document, CollectionReference collection) reference = GetReferenceFromPath(path);
            if (!reference.success)
            {
                return (false, $"Invalid path given: {path}");
            }
            /*else if(reference.document is null)
            {
                return (false, "Invalid length of path given, must be odd to give document");
            }*/

            HashMap objHashMap = GetMap(obj);
            try
            {
                if (reference.document is null)
                {
                    DocumentReference newDocumentRef = (DocumentReference)await reference.collection.Add(objHashMap);
                    return (true, newDocumentRef.Id);
                }
                else
                {
                    await reference.document.Set(objHashMap);
                    return (true, "");
                }
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }


        }
        public async Task<(bool, string)> DeleteObject(string pathInfo, params (string, string)[] arguments)                                      // DELETE  
        {
            string path = GetPath(pathInfo, arguments);
            if(path is null) { return (false, $"Invalid path info"); }
            (bool success, DocumentReference document, CollectionReference collection) reference = GetReferenceFromPath(path);
            if (!reference.success) { return (false, $"Invalid path given: {path}"); }

            if (reference.collection != null)
            {
                QuerySnapshot snap = (QuerySnapshot)await reference.collection.Get();
                try
                {
                    foreach (DocumentSnapshot doc in snap.Documents)
                    {
                        await doc.Reference.Delete();
                    }
                }
                catch (Exception e)
                {
                    return (false, $"Unable to delete collection: {path}  ({e.Message})");
                }

            }
            else if (reference.document != null)
            {
                try
                {
                    await reference.document.Delete();
                }
                catch (Exception e)
                {
                    return (false, $"Unable to delete document: {path}  ({e.Message}");
                }
            }
            return (true, "");
        }
        private async Task<(bool, string)> UpdateField(Java.Lang.Object obj, string path)                                                         // UPDATE  
        {
            string fieldLevel = popFieldName(ref path);
            string[] pathLevels = path.Split('/');
            (bool success, DocumentReference document, CollectionReference collection) reference = GetReferenceFromPath(pathLevels);

            if (!reference.success)
            {
                return (false, $"Invalid path given: {path}");
            }
            else if (reference.document == null)
            {
                return (false, "Invalid length of path given, must be odd");
            }
            else
            {
                try
                {
                    await reference.document.Update(fieldLevel, obj);
                    return (true, "");
                }
                catch (Exception e)
                {
                    return (false, e.Message);
                }
            }
        }
        // UpdateField methods:
        public async Task<(bool, string)> AddToArray(string newItem, string pathInfo, params (string, string)[] arguments)
        {
            string path = GetPath(pathInfo, arguments);


            return await UpdateField(FieldValue.ArrayUnion(newItem), path);
        }
        public async Task<(bool, string)> RemoveFromArray(string oldItem, string pathInfo, params (string, string)[] arguments)
        {
            string path = GetPath(pathInfo, arguments);
            return await UpdateField(FieldValue.ArrayRemove(oldItem), path);
        }
        public async Task<(bool, string)> UpdateString(string newString, string pathInfo, params (string, string)[] arguments)
        {
            string path = GetPath(pathInfo, arguments);
            return await UpdateField(newString, path);
        }

        public void RemoveAllListeners()
        {
            foreach (KeyValuePair<string, List<IListenerRegistration>> KeyValue in ListenerDict){
                foreach (IListenerRegistration listener in KeyValue.Value)
                {
                    listener.Remove();
                }
            }


            //foreach (IListenerRegistration listener in Listeners)
            //{
            //    listener.Remove();
            //}
        }
        public void RemoveListenersByKey(string key)
        {
            if (!ListenerDict.ContainsKey(key)) { return; }

            foreach (IListenerRegistration listener in ListenerDict[key])
            {
                listener.Remove();
            }
        }


        // Common GET Requests:
        public async Task<User> UserFromId(string id) // (GET)
        {
            (bool success, object user) response = await FetchData<User>("UserFromId", ("USERID", id));  //new Dictionary<string, string> { { "USERID", id } }

            return response.success ? (User)response.user : null;
        }
        public async Task<User> UserFromUsername(string username)  // (GET)
        {
            (bool success, object user) response = await FetchData<User>("UserFromUsername", ("USERNAME", username)); //new Dictionary<string, string> { { "USERID", username } }

            return response.success ? (User)response.user : null;
        }


        // WRITE Requests:

        public async Task<(bool, string)> InitiliseUser(string username)
        {
            HashMap privateMap = new HashMap();
            privateMap.Put("Username", username);
            privateMap.Put("chatsID", new JavaList<string>());

            HashMap publicMap = new HashMap();
            publicMap.Put("Id", FirebaseAuth.Instance.CurrentUser.Uid);
            HashMap publicMap2 = new HashMap();
            publicMap2.Put("Username", username);

            // Private User Data
            DocumentReference docRef = FirebaseFirestore.Instance.Collection("users").Document(FirebaseAuth.Instance.CurrentUser.Uid);
            // Public
            DocumentReference publicDocRef = FirebaseFirestore.Instance.Collection("usersPublic").Document(username);
            DocumentReference publicDocRef2 = FirebaseFirestore.Instance.Collection("usersPublicID").Document(FirebaseAuth.Instance.CurrentUser.Uid);

            try
            {
                await docRef.Set(privateMap);
                await publicDocRef.Set(publicMap);
                await publicDocRef2.Set(publicMap2);
                return (true, "");
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
        }

        // TODO: Replace all these functions with versions of the WriteObject
        // Test to see if the WriteObject function works, will be implemented in the actual classes if works.
        public async Task<(bool, string)> InitiliseChat(Chat chat)
        {
            (bool success, string newChatID) result = await WriteObject(chat, "Chat", ("CHATID", ""));
            return result;
        }

        public async Task<(bool, string)> SendAcceptedRequest(string requestUserID, AcceptedRequest ARequest)
        {
            CollectionReference requestCollection = FirebaseFirestore.Instance.Collection("requests");
            DocumentReference acceptedRequests = requestCollection.Document("accepted");
            DocumentReference userAcceptedRequests = acceptedRequests.Collection(requestUserID).Document(FirebaseAuth.Instance.CurrentUser.Uid);

            DocumentReference pendingRequests = requestCollection.Document("pending");
            DocumentReference userPendingRequests = pendingRequests.Collection(FirebaseAuth.Instance.CurrentUser.Uid).Document(requestUserID);


            HashMap acceptMap = GetMap(ARequest);

            try
            {
                await userPendingRequests.Delete();
                Debug($"Deleted Pending Request for id:{FirebaseAuth.Instance.CurrentUser.Uid}");

                await userAcceptedRequests.Set(acceptMap);
                Debug($"Added Accepted Request for id:{requestUserID}");
                return (true, "");
            } catch(Exception e)
            {
                return (false, e.Message);
            }
        }

        public async Task<(bool, string)> SendRequest(Request request, string requestUserID)
        {
            CollectionReference requestCollection = FirebaseFirestore.Instance.Collection("requests");
            DocumentReference pendingRequests = requestCollection.Document("pending");
            CollectionReference userCollection = pendingRequests.Collection(requestUserID);


            HashMap pendingRequest = GetMap(request);

            try
            {
                await userCollection.Document(request.SourceUser.Id).Set(pendingRequest);
                Debug("Added Pending Request");
                return (true, "");
            } catch(Exception e)
            {
                return (false, e.Message);
            }
        }

        public async Task<(bool, string)> AddChatIDToUser(string userID, string chatID)
        {
            DocumentReference doc = FirebaseFirestore.Instance.Collection("users").Document(userID);
            try
            {
                await doc.Update("chatsID", FieldValue.ArrayUnion(chatID));
                return (true, "");
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
        }

        public async Task<(bool, string)> AddMessageToChat(Message message, string chatID)
        {
            string path = GetPath("Chat/messages", ("CHATID", chatID));

            object messageObj = GetMap(message);

            (bool success, string message) result = await UpdateField(FieldValue.ArrayUnion((Java.Lang.Object)messageObj), path);

            return result;
        }

        
        
        // Utility functions
        private HashMap GetMap(object obj) // Object -> HashMap    (for WRITE/LISTEN)
        {
            // Organising Output Logs
            string parentMethodName = (new System.Diagnostics.StackTrace()).GetFrame(1).GetMethod().Name;
            string indent = "";
            if(parentMethodName == "GetMap") { indent = "     ";  }
            Debug($"{indent}Converting {obj.GetType()} to HashMap...");

            
            HashMap map = new HashMap();
            foreach (PropertyInfo prop in obj.GetType().GetProperties())
            {
                var propValue = GetValue(prop, obj);
                
                if(propValue == null) { Error($"{prop} is not defined in {obj.GetType()}", 1); continue; }
                Type type = propValue.GetType();

                // Contains other objects
                if (type.IsArray || type.IsGenericType)
                {
                    bool isGenericListType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
                    if (isGenericListType && propValue is List<object> propList) { propValue = propList.ToArray(); }


                    JavaList<HashMap> arrayMap = new JavaList<HashMap>();
                    int index = 0;
                    foreach (var item in (object[])propValue){
                        if (item is string str_item) {
                            Debug($"{indent}{index}: {str_item}", 1);
                            arrayMap.Add(str_item);
                        }
                        else {
                            Debug($"{indent}{index}: {item.GetType()}", 1);
                            arrayMap.Add(GetMap(item));
                        }

                        
                        index++;
                    }
                    map.Put(prop.Name, arrayMap);
                }
                // Is a base object
                else if (!type.Namespace.StartsWith("System"))
                {
                    Debug($"{indent}{prop.Name}: ", 1);
                    map.Put(prop.Name, GetMap(propValue));
                }
                else if (type == typeof(DateTime))
                {   // Convert date/time to binary for more efficient storage and converting
                    Debug($"{indent}{prop.Name}:{propValue}", 1);
                    map.Put(prop.Name, ((DateTime)propValue).ToBinary().ToString());
                }
                else
                {
                    Debug($"{indent}{prop.Name}:{propValue}", 1);
                    map.Put(prop.Name, propValue.ToString());
                }
            }
            if(obj.GetType().GetProperties().Length == 0)
            {
                Debug($"No properties found of object of type: {obj.GetType()}", 1);
            }
            return map;
        }
        private object GetValue(PropertyInfo prop, object obj)
        {
            try
            {
                var propValue = prop.GetValue(obj, null);
                return propValue;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }
        



        // Deprecated:
        public Task<bool> ListenDataAsync<returnType>(string pathInfo, Action<object> action, string changeType = null, bool returnOnInitial = true, string listenerKey = " ", params (string, string)[] arguments)  // Not used
        {
            TaskCompletionSource<bool> fetchDataCompletion = new TaskCompletionSource<bool>();

            bool result = ListenData<returnType>(pathInfo, (object result) => { Console.WriteLine("Resolved Listener!!"); action.Invoke(result); fetchDataCompletion.TrySetResult(true); }, changeType, listenerKey, arguments);
            if (!result) { fetchDataCompletion.TrySetResult(false); }

            return fetchDataCompletion.Task;
        }
        private string PopFromList(ref List<string> path, int index)
        {
            string fieldLevel = path[path.Count - 1];
            path.RemoveAt(path.Count - 1);
            return fieldLevel;
        }
    }
}
