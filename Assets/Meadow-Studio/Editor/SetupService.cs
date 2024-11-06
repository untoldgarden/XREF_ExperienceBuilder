using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Meadow.Studio
{
    //TODO merge this into MeadowSetupWindow
    public static class SetupService
    {
        public static async Task<string> GetVisualScriptingSettingsString(string path)
        {
            if(string.IsNullOrEmpty(path))
            {
                return null;
            }

            string projectYamlString = null;
            while(projectYamlString == null || projectYamlString == "")
            {
                try
                {
                    projectYamlString = File.ReadAllText(path);
                }
                catch
                {
                    Debug.Log("Error reading Visual Scripting settings file. Retrying...");
                    await Task.Delay(100);
                }
            }

            return projectYamlString;
        }

        public static async Task WaitForDelay(int delay)
        {
            await Task.Delay(delay);
        }
    }
}
