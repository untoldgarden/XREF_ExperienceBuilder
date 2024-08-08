using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meadow.Studio;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Meadow.Studio
{
    public class MetadataService
    {
        /// <summary>
        /// Gets all the user's experience metadatas and opens the experience page UI
        /// </summary>
        /// <param name="email">The users email</param>  
        public async Task<HTTPSResponse> GetUserExperienceMetadatas(string email)
        {
            string safeEmail = email.Replace(".", "%2E");
            var request = new UnityWebRequest("https://users-getuserexperiencemetadatashttps-u7lfzepmiq-ew.a.run.app", "POST");
            string jsonData = "{\"safeEmail\":\"" + safeEmail + "\",\"includeTests\":true,\"includeNotLive\":true}";
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.Log(request.error);
                return new HTTPSResponse { success = false, message = request.error };
            }
            else
            {
                string json = request.downloadHandler.text;
                var parsedResponse = new JObject(JObject.Parse(json).Properties().OrderBy(p => p.Value["titles"]["en"]?.ToString() ?? p.Value["titles"]["English"]?.ToString()));
                return new HTTPSResponse { success = true, data = parsedResponse };
                
            }
        }
    }
}
