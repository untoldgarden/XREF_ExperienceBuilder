using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Meadow.Studio
{
    public class UpdateService
    {
        string releasesURL = "https://github.com/untoldgarden/Meadow-Studio/releases";

        public string GetCurrentVersion()
        {
            //find package.json in Meadow-Studio folder which is somewhere in Assets folder
            string[] dirs = Directory.GetDirectories(Application.dataPath, "Meadow-Studio", SearchOption.AllDirectories);
            if (dirs.Length == 0)
            {
                Debug.LogError("Meadow-Studio folder not found in Assets folder");
                return null;
            }

            string packageJson = File.ReadAllText(dirs[0] + "/package.json");
            

            //read the version from the package.json file

            var package = Newtonsoft.Json.Linq.JObject.Parse(packageJson);
            return "v" + package["version"].ToString();
        }

        public async Task<HTTPSResponse> CheckForUpdates()
        {
            string currentVersion = GetCurrentVersion();

            var request = new UnityWebRequest("https://api.github.com/repos/untoldgarden/Meadow-Studio/releases/latest", "GET");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/vnd.github.v3+json");
            request.SetRequestHeader("User-Agent", "Meadow-Studio");

            await request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.Log(request.error);
                return new HTTPSResponse { success = false, message = request.error };
            }
            else
            {
                string json = request.downloadHandler.text;
                var release = Newtonsoft.Json.Linq.JObject.Parse(json);
                var version = release["tag_name"].ToString();

                if (CompareVersionStrings(currentVersion, version) < 0)
                {
                    
                    string updateUrl = release["assets"][0]["browser_download_url"].ToString();
                    return new HTTPSResponse { success = true, data = updateUrl, message = "update-available", version = version };
                }
                else
                {
                    return new HTTPSResponse { success = true, message = "no-update" };
                }
            }
        }

        int CompareVersionStrings(string v1, string v2)
        {

            //remove the v from the version string
            v1 = v1.Substring(1);
            v2 = v2.Substring(1);

            string[] v1Parts = v1.Split('.');
            string[] v2Parts = v2.Split('.');

            for (int i = 0; i < v1Parts.Length; i++)
            {
                if (i >= v2Parts.Length)
                {
                    return 1;
                }

                int v1Part = int.Parse(v1Parts[i]);
                int v2Part = int.Parse(v2Parts[i]);

                if (v1Part > v2Part)
                {
                    return 1;
                }
                else if (v1Part < v2Part)
                {
                    return -1;
                }
            }

            if (v1Parts.Length < v2Parts.Length)
            {
                return -1;
            }

            return 0;
        }

    }
}