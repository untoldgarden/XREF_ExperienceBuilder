//-----------------------------------------------------------------------
// <copyright file="AuthService.cs" company="Untold Garden LTD">
//
// Copyright 2024 Untold Garden LTD
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Meadow.Studio
{

    class AuthService
    {

        string functionsUrl = "https://europe-west1-xref-client.cloudfunctions.net/";
        public User currentUser {get; set;}


        public async Task<HTTPSResponse> AuthenticateUser(string email, string password)
        {
            if (email == null || password == null || email == "" || password == "")
            {
                return new HTTPSResponse { success = false, message = "Email or password is null" };
            }

            var request = new UnityWebRequest(functionsUrl + "auth-authenticateUser", "POST");
            // Create the JSON data to send
            string jsonData = "{\"email\":\"" + email + "\",\"password\":\"" + password + "\"}";
            // Convert the JSON data to a byte array
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // Send the request
            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                return new HTTPSResponse { success = false, message = request.error };
            }
            else
            {
                string json = request.downloadHandler.text;
                // deserialize to AuthReponse


                if (json != null)
                {
                    // get Id, DisplayName, IdToken, RefreshToken & ExpiresIn from JSON and create User
                    JObject jObject = JObject.Parse(json);
                    string userID = jObject["id"].ToString();
                    string idToken = jObject["idToken"].ToString();
                    string refreshToken = jObject["refreshToken"].ToString();
                    int expiresIn = int.Parse(jObject["expiresIn"].ToString());
                    string displayName = jObject["displayName"].ToString();
                    User user = new()
                    {
                        Id = userID,
                        Email = email,
                        DisplayName = displayName,
                        IdToken = idToken,
                        RefreshToken = refreshToken,
                        ExpiresIn = expiresIn,
                        ProfileThumbnail = jObject["profileThumbnail"].ToString()
                    };
                    SaveLoginData(user);
                    return new HTTPSResponse { success = true, data = user };
                    // File.WriteAllLines(dataPath, new string[]{aEmail, userID});
                    // CoroutineRunner.Instance.StartCoroutine(GetUserExperienceMetadatas(userID));
                }
                else
                {
                    return new HTTPSResponse { success = false, message = "Response did not contain expected data" };
                }
            }
        }
       
        public async Task<HTTPSResponse> RefreshToken()
        {

            var request = new UnityWebRequest(functionsUrl + "auth-refreshAuth", "POST");
            // Create the JSON data to send
            string jsonData = "{\"refreshToken\":\"" + currentUser.RefreshToken + "\"}";
            // Convert the JSON data to a byte array
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // Send the request
            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                return new HTTPSResponse { success = false, message = request.error };
            }
            else
            {
                string json = request.downloadHandler.text;
                // deserialize to User
                if (json != null)
                {
                    // get Id, DisplayName, IdToken, RefreshToken & ExpiresIn from JSON and create User
                    JObject jObject = JObject.Parse(json);
                    string userID = jObject["id"].ToString();
                    string idToken = jObject["idToken"].ToString();
                    string refreshToken = jObject["refreshToken"].ToString();
                    int expiresIn = int.Parse(jObject["expiresIn"].ToString());
                    User user = new()
                    {
                        Id = userID,
                        Email = currentUser.Email,
                        DisplayName = currentUser.DisplayName,
                        IdToken = idToken,
                        RefreshToken = refreshToken,
                        ExpiresIn = expiresIn,
                        ProfileThumbnail = currentUser.ProfileThumbnail
                    };
                    SaveLoginData(user);
                    currentUser = user;
                    return new HTTPSResponse { success = true, data = user };
                }
                else
                {
                    Debug.Log("Response did not contain expected data");
                    return new HTTPSResponse { success = false, message = "Response did not contain expected data" };
                }

            }

        }
        
        
        public void SaveLoginData(User user)
        {
            // Create a binary formatter
            BinaryFormatter formatter = new BinaryFormatter();

            // Path to save the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-login.data");

            // Create a file stream
            using (FileStream stream = new FileStream(path, FileMode.Create))
            {
                // Serialize the User object and write it to the file
                formatter.Serialize(stream, user);
            }
        }
        public User LoadLoginData()
        {
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-login.data");

            // Check if the file exists
            if (File.Exists(path))
            {
                try
                {

                // Create a binary formatter
                BinaryFormatter formatter = new BinaryFormatter();

                // Create a file stream
                using (FileStream stream = new FileStream(path, FileMode.Open))
                {
                    // Deserialize the User object from the file
                    User user = (User)formatter.Deserialize(stream);
                    return user;
                }
                }
                catch (Exception e)
                {
                    Debug.LogError("Error loading login data: " + e.Message);
                    return null;
                }
            }
            else
            {
                return null;
            }
        }
        public void DeleteLoginData()
        {
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-login.data");

            // Check if the file exists
            if (File.Exists(path))
            {
                // Delete the file
                File.Delete(path);
            }
        }

    }



    [System.Serializable]
    class User
    {
        public string Id;
        public string Email;
        public string DisplayName;
        public string IdToken;
        public string RefreshToken;
        public int ExpiresIn;
        public string ProfileThumbnail;
    }

}
