//-----------------------------------------------------------------------
// <copyright file="PluginUtils.cs" company="Untold Garden LTD">
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


using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager;
using UnityEngine.UIElements;

/// <summary>
/// Build AssetBundle.
/// </summary>
public class PluginUtils
{
    public bool BuildAssetBundles(BundleInfo info, string assetBundleName)
    {
        //EditorUtility.DisplayProgressBar("Building Asset Bundle", "Starting...", 0f);
        if (info == null)
        {
            Debug.Log("Error in build");
            return false;
        }

        // string metaGuidelines = GetMetaGuidelines();
        // if (metaGuidelines == "")
        // {
        //     Debug.LogError("GetMetaGuidelines failed");
        //     return false;
        // }

        // string unityVersion = Application.unityVersion;
        // Debug.Log("unityVersion: " + unityVersion);
        // Dictionary<string, object> dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(metaGuidelines);
        // if (dictionary == null)
        // {
        //     Debug.LogError("parse json failed");
        //     return false;
        // }

        // string text = dictionary["unityVersion"] as string;
        // int compareResult;
        // bool majorMinorIsEqual;
        // string minCompatibleVersion;
        // bool patchIsEqual;
        // CheckUnityVersionCompatibility(text, out majorMinorIsEqual, out patchIsEqual, out compareResult, out minCompatibleVersion);
        // if (compareResult == -1)
        // {
        //     Debug.LogError("You are not using the correct version of Unity. Minimum compatible version: " + minCompatibleVersion + "; Recommended version: " + text);
        //     EditorUtility.DisplayDialog("Error", "You are not using the correct version of Unity. \n Minimum compatible version: " + minCompatibleVersion + " \n Recommended version: " + text, "OK");
        //     return false;
        // }

        // if (compareResult >= 0 && !patchIsEqual)
        // {
        //     if (!EditorUtility.DisplayDialog("Warning", "You are not using the recommended version of Unity. \n Unknown issues might occur. \n Recommended version: " + text + ". \n Do you want to continue?", "Yes", "No"))
        //     {
        //         return false;
        //     }

        //     return Build(info, assetBundleName);
        // }

        return Build(info, assetBundleName);
    }
    public bool Build(BundleInfo info, string bundleName)
    {
        Debug.Log("Starting build for bundle: " + bundleName);

        if (!Directory.Exists("Assets/Temp"))
        {
            Directory.CreateDirectory("Assets/Temp");
        }

        string text = "Assets/Temp/XREFBundleMeta.txt";
        CheckPackageVersion("com.untoldgarden.xref", out var version, out var latestVersion);
        CheckPackageVersion("com.untoldgarden.xref-experience-builder", out var version2, out var latestVersion2);
        int num = ComparePackageVersion(version, latestVersion);
        int num2 = ComparePackageVersion(version2, latestVersion2);
        if (num == -1 || num2 == -1)
        {
            Debug.LogError("XREF and XREF Experience Builder must be updated to the latest  version. \n XREF: " + version + " latest: " + latestVersion + "\n XREF Experience Builder: " + version2 + " latest: " + latestVersion2);
            return false;
        }

        if (version == "ERROR" || version2 == "ERROR")
        {
            Debug.LogError("Error while checking package versions");
            return false;
        }

        if (version == "NOT_FOUND" || version2 == "NOT_FOUND")
        {
            Debug.LogError("Package not found. XREF and XREF Experience Builder must be installed.");
            return false;
        }

        string contents = "com.untoldgarden.xref: " + version + "\ncom.untoldgarden.xref-experience-builder: " + version2;
        File.WriteAllText(text, contents);
        AssetDatabase.ImportAsset(text);

        // ------------------------------------ new test code ------------------------------------
        if (!Directory.Exists("Assets/Temp/" + bundleName))
        {
            Directory.CreateDirectory("Assets/Temp/" + bundleName);
        }
        string text3 = "Assets/Temp/" + bundleName + "/XREFBundleMeta.txt";
        File.Copy(text, text3, overwrite: true);
        AssetDatabase.ImportAsset(text3);

        string[] assetPathsFromAssetBundle = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
        //EditorUtility.DisplayProgressBar("Building Asset Bundle", "Adding assets to bundle...", 0.3f);
        Debug.Log("Found " + assetPathsFromAssetBundle.Length + " assets for bundle");

        AssetBundleBuild item = new AssetBundleBuild
        {
            assetBundleName = bundleName
        };
        List<string> list3 = new List<string> { text3 };
        string[] array2 = assetPathsFromAssetBundle;
        foreach (string text4 in array2)
        {
            if (!(AssetDatabase.GetMainAssetTypeAtPath(text4) == typeof(MonoScript)))
            {
                list3.Add(text4);
            }
        }
        item.assetNames = list3.ToArray();
        List<AssetBundleBuild> list = new List<AssetBundleBuild> { item };

        AssetBundleManifest assetBundleManifest = BuildPipeline.BuildAssetBundles(info.outputDirectory, list.ToArray(), info.options, info.buildTarget);
        Debug.Log("Build completed: " + info.outputDirectory + " " + info.buildTarget + " " + info.options + " " + bundleName);
        //EditorUtility.DisplayProgressBar("Building Asset Bundle", "Build completed...", 0.6f);

        string path = "Assets/Temp/" + bundleName;
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        File.Delete(text);
        if (assetBundleManifest == null)
        {
            Debug.Log("Error in build");
            return false;
        }

        if (info.onBuild != null)
        {
            info.onBuild(bundleName);
        }
        // Debug.Log("Build method completed successfully");
        //EditorUtility.ClearProgressBar();

        // ------------------------------------- end of new test code -------------------------------------

        return true;
    }

    public void CheckPackageVersion(string packageName, out string version, out string latestVersion)
    {
        version = "";
        latestVersion = "";
        ListRequest listRequest = Client.List();
        while (!listRequest.IsCompleted)
        {
            Thread.Sleep(10);
        }

        if (listRequest.Status == StatusCode.Success)
        {
            foreach (UnityEditor.PackageManager.PackageInfo item in (IEnumerable<UnityEditor.PackageManager.PackageInfo>)listRequest.Result)
            {
                if (item.name == packageName)
                {
                    // Debug.Log("Package version: " + item.version);
                    version = item.version;
                    latestVersion = item.versions.latest;
                    return;
                }
            }

            Debug.LogWarning("Package not found: " + packageName);
            version = "NOT_FOUND";
        }
        else if (listRequest.Status >= StatusCode.Failure)
        {
            Debug.LogError("Failed to get packages list.");
            version = "ERROR";
        }
        else
        {
            Debug.LogError("Unknown error while getting packages list.");
            version = "ERROR";
        }
    }
    public string GetMetaGuidelines()
    {
        using UnityWebRequest unityWebRequest = UnityWebRequest.Get("https://firebasestorage.googleapis.com/v0/b/xref-client.appspot.com/o/appconfig%2Fxref-bundle-meta.json?alt=media");
        unityWebRequest.SendWebRequest();
        while (!unityWebRequest.isDone)
        {
            Thread.Sleep(10);
        }

        if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError("Error downloading file: " + unityWebRequest.error);
            return "";
        }

        // Debug.Log("Downloaded file content: " + unityWebRequest.downloadHandler.text);
        return unityWebRequest.downloadHandler.text;
    }
    public void CheckUnityVersionCompatibility(string unityVersion, out bool majorMinorIsEqual, out bool patchIsEqual, out int compareResult, out string minCompatibleVersion)
    {
        majorMinorIsEqual = false;
        patchIsEqual = false;
        minCompatibleVersion = "";
        compareResult = 0;
        string[] array = (from x in unityVersion.Split('.')
                          select x.Trim()).ToArray();
        string[] array2 = Application.unityVersion.Split('.');
        if (array2.Length < 2)
        {
            Debug.LogError("Error parsing unity version");
            return;
        }

        if (array.Length < 2)
        {
            Debug.LogError("Error parsing unity version");
            return;
        }

        int num = int.Parse(array2[1]);
        int num2 = int.Parse(array[1]);
        int num3 = int.Parse(array2[0]);
        int num4 = int.Parse(array[0]);
        array2[2] = array2[2].Split('f')[0];
        array[2] = array[2].Split('f')[0];
        int num5 = int.Parse(array2[2]);
        int num6 = int.Parse(array[2]);
        compareResult = num3.CompareTo(num4);
        if (compareResult == 0)
        {
            compareResult = num.CompareTo(num2);
            if (compareResult == 0)
            {
                compareResult = num5.CompareTo(num6);
            }
        }

        majorMinorIsEqual = num3 == num4 && num == num2;
        patchIsEqual = num5 == num6;
        minCompatibleVersion = num4 + "." + num2;
    }
    private int ComparePackageVersion(string version, string otherVersion)
    {
        string[] array = (from x in version.Split('.')
                          select x.Trim()).ToArray();
        string[] array2 = (from x in otherVersion.Split('.')
                           select x.Trim()).ToArray();
        if (array.Length < 2)
        {
            Debug.LogError("Error parsing version");
            return 0;
        }

        if (array2.Length < 2)
        {
            Debug.LogError("Error parsing version");
            return 0;
        }

        int num = int.Parse(array[1]);
        int value = int.Parse(array2[1]);
        int num2 = int.Parse(array[0]);
        int value2 = int.Parse(array2[0]);
        int num3 = num2.CompareTo(value2);
        if (num3 == 0)
        {
            num3 = num.CompareTo(value);
        }

        return num3;
    }
    public Dictionary<string, List<string>> RetrieveAllAssetPaths()
    {
        //refresh the asset database
        AssetDatabase.Refresh();
        Dictionary<string, List<string>> assetBundlePaths = new Dictionary<string, List<string>>();
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();

        if(allAssetPaths == null || allAssetPaths.Length == 0)
        {
            //no asset paths found
            return null;
        }

        foreach (string assetPath in allAssetPaths)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);

            //check that the importer is not null
            if(importer == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(importer.assetBundleName))
            {   
            
                if (!assetBundlePaths.ContainsKey(importer.assetBundleName))
                {
                    assetBundlePaths[importer.assetBundleName] = new List<string>();
                }
                assetBundlePaths[importer.assetBundleName].Add(assetPath);
            }
        }
        return assetBundlePaths;
    }
    public string GetPluginDir(bool removeBeforeAssets = false){
        string[] dirs = Directory.GetDirectories(Application.dataPath, "Meadow-Studio", SearchOption.AllDirectories);
        if (dirs.Length == 0)
        {
            Debug.LogError("Meadow-Studio folder not found in Assets folder");
            return null;
        }
        if(removeBeforeAssets)
        {
            //remove the entire path before the Assets folder
            return "Assets" + dirs[0].Split(new string[] { "Assets" }, System.StringSplitOptions.None)[1] + "/";
        }
        return dirs[0];
    }
}