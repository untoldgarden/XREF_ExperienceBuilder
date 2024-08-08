//-----------------------------------------------------------------------
// <copyright file="BundleService.cs" company="Untold Garden LTD">
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


using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Meadow.Studio
{

    class BundleService
    {

        string functionsUrl = "https://europe-west1-xref-client.cloudfunctions.net/";



        public async Task<HTTPSResponse> GetUploadUrl(User user, string experienceId, string buildType, string platform, string bundleType)
        {
            string jsonData =
            "{\"uid\":\"" + user.Id +
            "\",\"email\":\"" + user.Email +
            "\",\"experienceId\":\"" + experienceId +
            "\",\"buildType\":\"" + buildType +
            "\",\"platform\":\"" + platform +
            "\",\"bundleType\":\"" + bundleType +
            "\",\"idToken\":\"" + user.IdToken +
            "\"}";

            byte[] bodyRaw = new System.Text.UTF8Encoding().GetBytes(jsonData);

            string getUrl = functionsUrl + "exp-generateUploadUrl";
            UnityWebRequest request = new UnityWebRequest(getUrl, "POST")
            {
                uploadHandler = new UploadHandlerRaw(bodyRaw),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Content-Type", "application/json");
            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
<<<<<<< Updated upstream
                Debug.LogError($"Error getting the signed URL: {request.error}");
=======
>>>>>>> Stashed changes
                return new HTTPSResponse { success = false, message = request.error, code = request.responseCode };
            }
            else
            {
                string json = request.downloadHandler.text;
                JObject jObject = JObject.Parse(json);
                return new HTTPSResponse { success = true, data = jObject};
            }

        }

        public async Task<HTTPSResponse> UploadFile(string uploadUrl, string bundlePath)
        {
            byte[] fileBytes = File.ReadAllBytes(bundlePath);
            UnityWebRequest request = UnityWebRequest.Put(uploadUrl, fileBytes);
            request.SetRequestHeader("Content-Type", "application/octet-stream");

            await request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("Error uploading file: " + request.error);
                return new HTTPSResponse { success = false, message = request.error };
            }
            else
            {
                // Debug.Log("File uploaded successfully.");
                return new HTTPSResponse { success = true };
            }
        }

        // Delete asset bundle from storage if it exists, and update DB.
        public async Task<HTTPSResponse> DeleteExistingAssetBundleAndUpdateDB(string guid, string experienceId, string buildType, string platform, string bundleType)
        {

            string jsonData =
            "{\"GUID\":\"" + guid +
            "\",\"experienceId\":\"" + experienceId +
            "\",\"buildType\":\"" + buildType +
            "\",\"platform\":\"" + platform +
            "\",\"bundleType\":\"" + bundleType + "\"}";

            byte[] bodyRaw2 = new System.Text.UTF8Encoding().GetBytes(jsonData);
            string delUrl = "https://exp-deleteassetbundleandupdateartworkmetadata-u7lfzepmiq-ew.a.run.app";

            UnityWebRequest request = new UnityWebRequest(delUrl, "POST")
            {
                uploadHandler = new UploadHandlerRaw(bodyRaw2),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.SetRequestHeader("Content-Type", "application/json");
            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Error getting the signed URL: {request.error}");
                return new HTTPSResponse { success = false, message = request.error };
            }
            else
            {
                // Debug.Log("Old file deleted from storage and DB updated. " + platform);
                return new HTTPSResponse { success = true };
                //CoroutineRunner.Instance.StartCoroutine(UploadFile(bundlePath, signedUrlResponse.url));
            }
        }

    }




}

[System.Serializable]
class BundleInfoNoFile
{
    public string uid;
    public string experienceId;
    public string buildType;
    public string platform;
    public string bundleType;
    public string idToken;
}
