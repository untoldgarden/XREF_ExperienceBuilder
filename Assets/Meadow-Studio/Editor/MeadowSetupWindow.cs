using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.UIElements;
using Newtonsoft.Json.Linq;
using UnityEditor.Build;
using Meadow.Studio;
using System.Linq;
using UnityEngine.Networking;
using System.Threading;
using UnityEditor.PackageManager;
using System.Threading.Tasks;
using UnityEditor.Analytics;
using UnityEditor.PackageManager.UI;
#if UNITY_VISUAL_SCRIPTING
using Unity.VisualScripting;
#endif

namespace Meadow.Studio
{
    [InitializeOnLoad]
    public class MeadowSetupWindow : EditorWindow
    {   
        static SetupConfig setupConfigJson;
        public static MeadowSetupData setupMetadata;
        List<string> optionalPackagesSelected = new List<string>();
        static bool setupProcessActive = false;

        //page handler
        private enum SetupPage
        {
            Start,
            OptionalPackages,
            Loading,
            Complete
        }
        private static SetupPage currentPage = SetupPage.Start;

        //setup flag tick
        private static float nextSetupTick = 0f;
        private const float setupInterval = 1f;

        //config update tick
        private static float nextConfigUpdateTick = 0f;
        private const float configUpdateInterval = 60f;
        private const float configUpdateDismissDelay = 7200f; //2 hours

        static MeadowSetupWindow()
        {
            //hook into package manager events to check if core or optional packages have been removed
            UnityEditor.PackageManager.Events.registeringPackages += (packageEventArgs) =>
            {
                if(setupMetadata != null && setupMetadata.SetupComplete && setupConfigJson != null)
                {
                    if(packageEventArgs.removed.Count > 0)
                    {
                        //check if any of the removed packages are the core or optional packages
                        foreach (var package in packageEventArgs.removed)
                        {
                            if(setupConfigJson.core.packages.dependencies.ContainsKey(package.name) || setupMetadata.OptionalPackagesInstalled.Contains(package.name.Split('.')[2]))
                            {
                                if(EditorUtility.DisplayDialog("Meadow Package Removed", package.name + " has been removed from the project. \nPlease reinstall the package to continue using Meadow.", "Reinstall", "Dismiss"))
                                {
                                    //If the user accepts then reinstall the package
                                    UpdatePackageManifest(new Packages { dependencies = new Dictionary<string, string> { { package.name, package.version } } });
                                }
                            }
                        }
                    }
                }
            };

            //disabled for a simplified approach
            // CheckInstalledPackages();

            //on editor update logic
            EditorApplication.update += OnEditorUpdate;
        }

        private static void InitializeConfig()
        {
            // if(setupConfigJson != null)
            //     return;
            string configString = GetSetupConfig();
            if(configString != "")
            {
                //deserialize the config file
                setupConfigJson = JsonConvert.DeserializeObject<SetupConfig>(configString);
            }
        }

        private static void InitializeSetupMetadata()
        {
            if(setupMetadata != null)
            {
                return;
            }
            
            setupMetadata = LoadMeadowSetupData();
            
            //if its still null create a new setup metadata file
            if(setupMetadata == null)
            {
                //create a new setup metadata file
                setupMetadata = new MeadowSetupData();
                WriteMeadowSetupData(setupMetadata);
            }
        }

        private static void OnEditorUpdate()
        {
            if(setupProcessActive)
                return;

            InitializeSetupMetadata();

            //config update tick
            if(EditorApplication.timeSinceStartup >= nextConfigUpdateTick)
            {
                nextConfigUpdateTick = (float)EditorApplication.timeSinceStartup + configUpdateInterval;
                InitializeConfig();
                if(setupMetadata != null && setupConfigJson != null && setupMetadata.SetupComplete && !setupMetadata.ConfigUpdateAvailable && CompareConfigVersions(setupMetadata.LastSetupConfigVersion, setupConfigJson.metadata.configurationVersion)) //check if the config file has been updated
                {
                    //Trigger a fast update process
                    if(EditorUtility.DisplayDialog("Meadow Config Update", "The Meadow config file has been updated. Would you like to update the project now?", "Yes", "Later"))
                    {
                        UpdateSetupProcess();
                    }
                    else
                    {
                        setupMetadata.ConfigUpdateAvailable = true;
                        WriteMeadowSetupData(setupMetadata);
                    }
                }
            }

            //run the core setup process if setup is not complete
            if(setupMetadata != null && !setupMetadata.SetupComplete && setupConfigJson != null)
            {
                //Run the core setup process
                #if !MEADOW_STUDIO_DEVELOPMENT
                //check if XREF-Experience-Builder is installed
                if(AssetDatabase.IsValidFolder("Packages/com.untoldgarden.xref-experience-builder"))
                {
                    //if XREF-Experience-Builder is installed then run the full setup process with both core and the already installed optional packages
                    //TODO update this logic when there are more optional packages
                    InitialSetupProcess(true);
                }
                else
                {
                    InitialSetupProcess();
                }
                #endif
                return;
            }

            //setup update tick
            if(EditorApplication.timeSinceStartup >= nextSetupTick)
            {
                nextSetupTick = (float)EditorApplication.timeSinceStartup + setupInterval;
                HandleSetupFlags();
            }

            //check if the loading ui was the last page and if the setup process is no longer active open the complete ui
            if(currentPage == SetupPage.Loading && setupMetadata != null && setupMetadata.SetupComplete && !setupMetadata.RefreshAssetDatabase && !setupProcessActive && !setupMetadata.InitializeVisualScripting && !setupMetadata.UpdateVisualScriptingForInstalledPackages && !setupMetadata.RegenerateVisualScriptingNodes)
            {
                //get the window instance
                MeadowSetupWindow window = GetWindow<MeadowSetupWindow>(true);
                window.CreateSetupCompleteUI(setupMetadata.UpdatingConfig);
             
                setupMetadata.UpdatingConfig = false;
                WriteMeadowSetupData(setupMetadata);
            }
            
            HandlePackageDefines();
        }

        private static void HandleSetupFlags()
        {
            if(setupMetadata != null)
            {   
                //refresh the asset database if the flag is true
                if(setupMetadata.RefreshAssetDatabase)
                {
                    MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
                    wnd.CreateSetupLoadingUI(15f, setupMetadata.UpdatingConfig);

                    //refresh the asset database
                    AssetDatabase.Refresh();

                    //write setup data with RefreshAssetDatabase set to false
                    setupMetadata.RefreshAssetDatabase = false;
                    WriteMeadowSetupData(setupMetadata);

                    return;
                }

                //add the visual scripting define if the flag is true
                if(setupMetadata.AddVisualScriptingDefineSymbol)
                {
                    //check that visual scripting package is installed
                    if(!AssetDatabase.IsValidFolder("Packages/com.unity.visualscripting"))
                    {
                        // Debug.LogError("Visual Scripting package not found. Cannot add scripting define symbol.");
                        return;
                    }

                    UpdateScriptingDefineSymbols(new List<string> { "UNITY_VISUAL_SCRIPTING" });
                    
                    //write setup data with AddVisualScriptingDefineSymbol set to false
                    setupMetadata.AddVisualScriptingDefineSymbol = false;
                    WriteMeadowSetupData(setupMetadata);

                    MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
                    wnd.CreateSetupLoadingUI(35f, setupMetadata.UpdatingConfig);

                    //reload the domain
                    EditorUtility.RequestScriptReload();
                    return;
                }

                //if the regenerateVisualScriptingNodes flag is true rebuild the visual scripting nodes
                #if UNITY_VISUAL_SCRIPTING
                if(setupMetadata.InitializeVisualScripting)
                {
                    // Initialize visual scripting
                    // Debug.Log("Initializing Visual Scripting...");
                    if(!VSUsageUtility.isVisualScriptingUsed)
                    {
                        VSUsageUtility.isVisualScriptingUsed = true;
                        //write setup data with InitializeVisualScripting set to false
                        setupMetadata.InitializeVisualScripting = false;
                        WriteMeadowSetupData(setupMetadata);

                        return;
                    }
                    else
                    {
                        //write setup data with InitializeVisualScripting set to false
                        setupMetadata.InitializeVisualScripting = false;
                        WriteMeadowSetupData(setupMetadata);

                        MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
                        wnd.CreateSetupLoadingUI(50f, setupMetadata.UpdatingConfig);
                    }
                }

                if(setupMetadata.UpdateVisualScriptingForInstalledPackages)
                {
                    // Update visual scripting settings for installed packages
                    // Debug.Log("Updating Visual Scripting settings for installed packages...");

                    MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
                    wnd.CreateSetupLoadingUI(50f, setupMetadata.UpdatingConfig);

                    //TODO Update this to a better solution via reflection
                    // await SetupService.WaitForDelay(2000);

                    foreach (var package in setupMetadata.OptionalPackagesInstalled)
                    {
                        if(setupConfigJson.optionalPackages.ContainsKey(package))
                        {
                            UpdateVisualScriptingSettings(setupConfigJson.optionalPackages[package].visualScripting);
                        }
                    }

                    //write setup data with UpdateVisualScriptingForInstalledPackages set to false
                    setupMetadata.UpdateVisualScriptingForInstalledPackages = false;
                    WriteMeadowSetupData(setupMetadata);

                    wnd.CreateSetupLoadingUI(75f, setupMetadata.UpdatingConfig);

                    EditorUtility.RequestScriptReload();
                    return;
                }

                if(setupMetadata.RegenerateVisualScriptingNodes)
                {
                    MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
                    wnd.CreateSetupLoadingUI(95f, setupMetadata.UpdatingConfig);

                    // Rebuild the visual scripting nodes
                    // Debug.Log("Rebuilding Visual Scripting nodes...");
                    UnitBase.Rebuild();

                    //write setup data with RegenerateVisualScriptingNodes set to false
                    setupMetadata.RegenerateVisualScriptingNodes = false;
                    WriteMeadowSetupData(setupMetadata);
                }
                #endif
            }
        }

        private static void CheckInstalledPackages()
        {
            if(setupMetadata != null && setupConfigJson != null)
            {
                if ( !File.Exists("Packages/manifest.json") )
                {
                    //can't find the manifest.json file
                    return;
                }

                string jsonText = File.ReadAllText("Packages/manifest.json");
                
                if(setupMetadata.SetupComplete)
                {
                    Dictionary<string, string> missingPackagesList = new Dictionary<string, string>()   ;
                    foreach(string dependency in setupConfigJson.core.packages.dependencies.Keys)
                    {
                        if(!jsonText.Contains(dependency))
                        {
                            missingPackagesList.Add(dependency, setupConfigJson.core.packages.dependencies[dependency]);
                        }
                    }
                    
                    if(setupMetadata.OptionalPackagesInstalled.Count > 0)
                    {
                        foreach(string package in setupMetadata.OptionalPackagesInstalled)
                        {
                            if(setupConfigJson.optionalPackages.ContainsKey(package))
                            {
                                foreach(string dependency in setupConfigJson.optionalPackages[package].packages.dependencies.Keys)
                                {
                                    if(!jsonText.Contains(dependency))
                                    {
                                        missingPackagesList.Add(dependency, setupConfigJson.optionalPackages[package].packages.dependencies[dependency]);
                                    }
                                }
                            }
                        }
                    }

                    if(missingPackagesList.Count > 0)
                    {
                        if(EditorUtility.DisplayDialog("Missing Meadow packages", "The following packages are missing from the project: \n" + string.Join("\n ", missingPackagesList), "Ok"))
                        {
                            if(EditorUtility.DisplayDialog("Missing Meadow packages", "Would you like to install the missing packages", "Yes", "Later"))
                            {
                                Packages missingPackages = new Packages();
                                missingPackages.dependencies = new Dictionary<string, string>();
                                foreach(string dependency in missingPackagesList.Keys)
                                {
                                    missingPackages.dependencies.Add(dependency, missingPackagesList[dependency]);
                                }

                                UpdatePackageManifest(missingPackages);
                            }
                        }
                    }
                }
            }
        }

        //TODO Add logic to have platform specific scripting defines
        private static void HandlePackageDefines()
        {
            if(setupConfigJson != null && setupMetadata != null)
            {
                if ( !File.Exists("Packages/manifest.json") )
                {
                    //can't find the manifest.json file
                    return;
                }

                string jsonText = File.ReadAllText("Packages/manifest.json");
            
                if(setupMetadata.SetupComplete && setupMetadata.OptionalPackagesInstalled.Count > 0)
                {
                    //TODO Ignoring core for now because there aren't any package handlers

                    foreach(string packageName in setupMetadata.OptionalPackagesInstalled)
                    {
                        //check if the package exists in the setup config
                        if(!setupConfigJson.optionalPackages.ContainsKey(packageName))
                        {
                            // Debug.LogError("Optional package not found in setup config: " + packageName);
                            continue;
                        }

                        //check if the package has any package define handlers
                        if(setupConfigJson.optionalPackages[packageName].packageDefineHandlers == null || setupConfigJson.optionalPackages[packageName].packageDefineHandlers.Count == 0)
                        {
                            // Debug.LogError("No package define handlers found for optional package: " + packageName);
                            continue;
                        }

                        foreach(string define in setupConfigJson.optionalPackages[packageName].packageDefineHandlers.Keys)
                        {
                            //if the scripting define does not exist add it
                            bool requirementsMet = CheckRequirements(setupConfigJson.optionalPackages[packageName].packageDefineHandlers[define].requirements, jsonText, setupConfigJson.optionalPackages[packageName].packageDefineHandlers[define].anyRequirement);
                            SetScriptingDefineSymbol(NamedBuildTarget.Standalone, requirementsMet, define);
                            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                            {
                                SetScriptingDefineSymbol(NamedBuildTarget.iOS, requirementsMet, define);
                            }
                            if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                            {
                                SetScriptingDefineSymbol(NamedBuildTarget.Android, requirementsMet, define);
                            }
                        }
                    }
                }
            }
        }

        private static void SetScriptingDefineSymbol(NamedBuildTarget target, bool addDefine, string define)
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (addDefine)
            {
                if (!defines.Contains(define))
                {
                    PlayerSettings.SetScriptingDefineSymbols(target, defines + ";" + define);
                }
            }
            else
            {
                if (defines.Contains(define))
                {
                    PlayerSettings.SetScriptingDefineSymbols(target, defines.Replace(define, ""));
                }
            }
        }

        private static bool CheckRequirements(List<string> requirements, string packagesJson, bool anyRequirement)
        {
            if(anyRequirement)
            {
                foreach(string requirement in requirements)
                {
                    if(packagesJson.Contains(requirement))
                    {
                        return true;
                    }
                }
                return false;
            }
            else
            {
                bool requirementsMet = false;
                foreach(string requirement in requirements)
                {
                    if(packagesJson.Contains(requirement))
                    {
                        requirementsMet = true;
                    }
                    else
                    {
                        requirementsMet = false;
                        break;
                    }
                }
                return requirementsMet;
            }
        }

        // [MenuItem("Meadow/Meadow Setup Wizard", false, 100)]
        private static void MeadowSetupWizardMenubar()
        {
            MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
            wnd.titleContent = new GUIContent("Meadow Setup Wizard");
            wnd.minSize = new Vector2(400, 200);
        }

        [MenuItem("Meadow/Install Optional Packages", false, 101)]
        private static void InstallOptionalPackages()
        {
            MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
            wnd.CreateOptionalPackagesUI(true);
        }

        private static MeadowSetupWindow CreateWindow(string title)
        {
            MeadowSetupWindow wnd = GetWindow<MeadowSetupWindow>(true, "Meadow Setup Wizard", true);
            // Set desired window size
            float width = 800;
            float height = 400;
            
            // Center in Unity Editor main window
            var main = EditorGUIUtility.GetMainWindowPosition();
            float centerX = main.x + (main.width - width) / 2;
            float centerY = main.y + (main.height - height) / 2;
            
            // Set window position and size
            wnd.position = new Rect(centerX, centerY, width, height);
            
            wnd.minSize = new Vector2(800, 400);
            wnd.maxSize = new Vector2(800, 400);

            return wnd;
        }

        // [MenuItem("Meadow/Create Complete Setup Data", false, 100)]
        private static void CreateCompleteSetupData()
        {
            MeadowSetupData data = LoadMeadowSetupData();
            if(data == null)
            {
                data = new MeadowSetupData();
            }
            data.SetupComplete = true;

            if(EditorUtility.DisplayDialog("Create Complete Setup Data", "Do you want to test config updating", "Yes", "No"))
            {
                data.LastSetupConfigVersion = "0.0.1";
            }
            else
            {
                data.LastSetupConfigVersion = "9.9.9";
            }
            
            //ask if the setup data should include xref-experience-builder
            bool includeExperienceBuilder = false;
            if(EditorUtility.DisplayDialog("Include xref-experience-builder?", "Would you like to include the xref-experience-builder package in the setup data?", "Yes", "No"))
            {
                includeExperienceBuilder = true;
            }

            if(includeExperienceBuilder)
            {
                data.OptionalPackagesInstalled = new List<string> { "xref-experience-builder" };
            }

            data.ConfigUpdateAvailable = false;

            WriteMeadowSetupData(data);
            setupMetadata = data;
        }

        // [MenuItem("Meadow/Debug Setup Data", false, 101)]
        private static void DebugSetupData()
        {
            MeadowSetupData data = LoadMeadowSetupData();
            if(data != null)
            {
                Debug.Log("Setup data loaded: " + data.SetupComplete);
                if(data.OptionalPackagesInstalled != null && data.OptionalPackagesInstalled.Count > 0)
                {
                    Debug.Log("Optional packages installed: " + string.Join(", ", data.OptionalPackagesInstalled));
                }
                Debug.Log("Last setup config version: " + data.LastSetupConfigVersion);
                Debug.Log("Config update available: " + data.ConfigUpdateAvailable);
                Debug.Log("Refresh AssetDatabase: " + data.RefreshAssetDatabase);
                Debug.Log("Initialize Visual Scripting: " + data.InitializeVisualScripting);
                Debug.Log("Update Visual Scripting For Installed Packages: " + data.UpdateVisualScriptingForInstalledPackages);
                Debug.Log("Regenerate Visual Scripting Nodes: " + data.RegenerateVisualScriptingNodes);
            }
            else
            {
                Debug.Log("No Meadow setup data found.");
            }
        }

        // [MenuItem("Meadow/Clear Setup Data", false, 102)]
        private static void ClearSetupData()
        {
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-setup.data");

            // Check if the file exists
            if(File.Exists(path))
            {
                File.Delete(path);
                // Debug.Log("Meadow setup data cleared.");
            }
            else
            {
                // Debug.Log("No Meadow setup data found.");
            }
        }

        // [MenuItem("Meadow/debug loading window", false, 103)]
        private static void OpenLoadingUIDebug()
        {
            MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
            wnd.titleContent = new GUIContent("Meadow Setup");
            wnd.minSize = new Vector2(400, 200);
            wnd.CreateSetupLoadingUI();
        }
        private void OnEnable()
        {
            // Debug.Log("OnEnable running...");
            //clear optional packages list
            optionalPackagesSelected.Clear();

            //load setup metadata
            setupMetadata = LoadMeadowSetupData();

            //Get the setup config file
            string configString = GetSetupConfig();
            if(configString != "")
            {
                //deserialize the config file
                setupConfigJson = JsonConvert.DeserializeObject<SetupConfig>(configString);
            }
            else
            {
                //unable to download the config file and no local file found.
                // Debug.LogError("Unable to download the config file and no local file found.");
            }

            //if setup metadata does not exist then it needs to be created at this stage
            if(setupMetadata == null)
            {
                // Debug.Log("No Meadow setup data found. Not reaching flags");
                //create a new setup metadata file
                setupMetadata = new MeadowSetupData();
                WriteMeadowSetupData(setupMetadata);
            }

            //check if the project needs to be setup or updated
            if(setupMetadata == null || !setupMetadata.SetupComplete) //check if the project has been setup before
            {
                // Debug.Log("Project requires setup: Project has not been setup before");
                CreateSetupStartUI();
            }
            else if(CompareConfigVersions(setupMetadata.LastSetupConfigVersion, setupConfigJson.metadata.configurationVersion) && !setupMetadata.ConfigUpdateAvailable) //check if the config file has been updated
            {
                // Debug.Log("Project requires setup: Config file has been updated");
                //Trigger a fast update process
                if(EditorUtility.DisplayDialog("Meadow Config Update", "The Meadow config file has been updated. Would you like to update the project now?", "Yes", "Later"))
                {
                    UpdateSetupProcess();
                }
                else
                {
                    CreateSetupCompleteUI();
                }
            }
            else if(setupProcessActive || setupMetadata.InitializeVisualScripting || setupMetadata.UpdateVisualScriptingForInstalledPackages || setupMetadata.RegenerateVisualScriptingNodes || setupMetadata.RefreshAssetDatabase) //check if the setup process is active
            {
                // Debug.Log("Project Setup process is active");
                CreateSetupLoadingUI();
            }
            else //the project is up to date
            {
                // Debug.Log("Project is up to date");
                CreateSetupCompleteUI();
            }
        }
        
    #region UI
        private void CreateSetupStartUI()
        {    
            rootVisualElement.Clear();

            //update the current page
            currentPage = SetupPage.Start;

            //create the setup UI
            var setup = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Meadow-Studio/UI/Wizard/setup.uxml");
            setup.CloneTree(rootVisualElement);

            //get the elements
            Button nextButton = rootVisualElement.Query<Button>("next-button");

            //set up the start button
            nextButton.clickable.clicked += () => 
            {
                if(EditorUtility.DisplayDialog("Meadow Setup Warning", "Project settings will be changed. \nPlease ensure that you have a backup.", "Proceed", "Cancel"))
                {
                    CreateOptionalPackagesUI();
                }
            };
        }

        private void CreateOptionalPackagesUI(bool postSetup = false)
        {
            if(postSetup)
            {
                if(setupMetadata == null)
                {
                    setupMetadata = LoadMeadowSetupData();
                }
                if(!setupMetadata.SetupComplete)
                {
                    //if the project has not been setup yet, warn the user and show the setup start UI
                    if(EditorUtility.DisplayDialog("Meadow not setup", "The project has not been setup to use Meadow yet. Please complete Meadow setup first.", "OK"))
                    {
                        CreateSetupStartUI();
                    }
                    return;
                }
            }

            rootVisualElement.Clear();

            //update the current page
            currentPage = SetupPage.OptionalPackages;

            //create the core packages setup UI
            var setup = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Meadow-Studio/UI/Wizard/optional-packages.uxml");
            setup.CloneTree(rootVisualElement);

            //set up the back button
            Button backButton = rootVisualElement.Query<Button>("back-button");
            if(postSetup)
            {
                backButton.style.display = DisplayStyle.None;
            }
            else
            {
                backButton.clickable.clicked += () => 
                {
                    CreateSetupStartUI();
                };
            }

            //set up the install button
            Button installButton = rootVisualElement.Query<Button>("install-button");

            //set up the toggle buttons
            Toggle experienceBuilderToggle = rootVisualElement.Query<Toggle>("experiencebuilder-toggle");
            if(postSetup && setupMetadata.OptionalPackagesInstalled.Contains("xref-experience-builder"))
            {
                //TODO Update this in the future when we have more optional packages
                experienceBuilderToggle.value = true;
                experienceBuilderToggle.SetEnabled(false);
                experienceBuilderToggle.style.opacity = 0.5f;
                installButton.SetEnabled(false);
                installButton.style.opacity = 0.5f;
            }
            else
            {
                installButton.clickable.clicked += async () => 
                {   
                    if(postSetup)
                    {
                        if(optionalPackagesSelected.Count == 0 )
                        {
                            EditorUtility.DisplayDialog("No optional packages selected", "Please select at least one optional package to install.", "OK");
                            return;
                        }
                    }

                    if(EditorUtility.DisplayDialog("Meadow Setup Warning", "Project settings will be changed. \nPlease ensure that you have a backup.", "Proceed", "Cancel"))
                    {
                        setupProcessActive = true;
                        
                        CreateSetupLoadingUI();

                        await SetupService.WaitForDelay(200);

                        SetupProcess(!postSetup);
                    }
                };
                experienceBuilderToggle.RegisterValueChangedCallback((evt) => 
                {
                    //Add logic to include this optional package in the setup process
                    if(evt.newValue && !optionalPackagesSelected.Contains("xref-experience-builder"))
                        optionalPackagesSelected.Add("xref-experience-builder");
                    else if(!evt.newValue && optionalPackagesSelected.Contains("xref-experience-builder"))
                        optionalPackagesSelected.Remove("xref-experience-builder");
                });
            }
        }

        private void CreateSetupLoadingUI(float progress = 0f, bool updating = false)
        {
            //create the setup loading UI
            // Debug.Log("Creating setup loading UI...");

            rootVisualElement.Clear();

            //update the current page
            currentPage = SetupPage.Loading;
            
            VisualTreeAsset loading = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Meadow-Studio/UI/Wizard/setup-loading.uxml");
            loading.CloneTree(rootVisualElement);

            //if updating config
            if(updating)
            {
                rootVisualElement.Q<Label>("title-label").text = "Config Update in progress";
                rootVisualElement.Q<Label>("description-label").text = "Please wait while the project is updated with the latest configuration.";
            }

            //get the progress bar element
            ProgressBar progressBar = rootVisualElement.Query<ProgressBar>("progress-bar");
            progressBar.value = progress;

            // //get elements
            // VisualElement loadingSpinner = rootVisualElement.Query<VisualElement>("loading-icon");

            // //wait a single frame
            // await SetupService.WaitForDelay(100);

            // //start the loading spinner
            // loadingSpinner.AddToClassList("loading-start");
        }

        private void CreateSetupCompleteUI(bool updated = false)
        {
            //create the setup complete UI
            // Debug.Log("Creating setup complete UI...");

            rootVisualElement.Clear();

            //update the current page
            currentPage = SetupPage.Complete;
            
            VisualTreeAsset complete = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Meadow-Studio/UI/Wizard/setup-complete.uxml");
            complete.CloneTree(rootVisualElement);

            //get elements
            Button finishButton = rootVisualElement.Query<Button>("finish-button");
            Button reinstallButton = rootVisualElement.Query<Button>("reinstall-button");
            VisualElement corePackagesContainer = rootVisualElement.Query<VisualElement>("core-container");
            Label corePackagesBody = corePackagesContainer.Query<Label>("core-packages-body");
            VisualElement dividerLine = rootVisualElement.Query<VisualElement>("divider-line");
            VisualElement optionPackagesContainer = rootVisualElement.Query<VisualElement>("optional-packages-container");
            Label optionalPackagesBody = optionPackagesContainer.Query<Label>("added-packages-body");
            Label visualScriptingTypeBody = rootVisualElement.Query<Label>("visual-scripting-type-body");
            Label visualScriptingAssemblyBody = rootVisualElement.Query<Label>("visual-scripting-assembly-body");

            // if(updated)
            // {
            //     rootVisualElement.Q<Label>("title-label").text = "Config Update Complete";
            //     rootVisualElement.Q<Label>("description-label").text = "The project has been updated with the latest configuration.";
            // }

            //set the core packages body text
            corePackagesBody.text = "-com.untoldgarden.xref : " + setupConfigJson.core.packages.dependencies["com.untoldgarden.xref"];

            //show the optional packages container if there are optional packages installed
            if(setupMetadata != null && setupConfigJson != null && setupMetadata.OptionalPackagesInstalled != null && setupMetadata.OptionalPackagesInstalled.Count > 0)
            {
                optionalPackagesBody.text = "-com.untoldgarden.xref-experience-builder : " + setupConfigJson.optionalPackages["xref-experience-builder"].packages.dependencies["com.untoldgarden.xref-experience-builder"];
                visualScriptingTypeBody.text = "Type Options: " + setupConfigJson.optionalPackages["xref-experience-builder"].visualScripting.typeOptions.Count();
                visualScriptingAssemblyBody.text = "Assembly Options: " + setupConfigJson.optionalPackages["xref-experience-builder"].visualScripting.assemblyOptions.Count();
                optionPackagesContainer.style.display = DisplayStyle.Flex;
                dividerLine.style.display = DisplayStyle.Flex;
            }
            else
            {
                optionPackagesContainer.style.display = DisplayStyle.None;
                dividerLine.style.display = DisplayStyle.None;
            }

            //set up the finish button
            finishButton.clickable.clicked += () => 
            {
                Close();
            };

            //setup the reinstall button
            // reinstallButton.clickable.clicked += () => 
            // {
            //     //create a dialog to confirm the reinstall
            //     if(EditorUtility.DisplayDialog("Reinstall Meadow Setup", "Are you sure you want to reinstall the Meadow setup?", "Yes", "No"))
            //     {
            //         //clear the setup data
            //         ClearSetupData();

            //         //open the setup wizard
            //         CreateSetupStartUI();
            //     }
            // };
        }

    #endregion

    #region Setup Process

        private void SetupProcess(bool includeCore = true)
        {
            setupProcessActive = true;

            if(includeCore)
            {
                //handle core packages
                UpdatePackageManifest(setupConfigJson.core.packages);
                if(setupConfigJson.core.packages.scriptingDefines != null && setupConfigJson.core.packages.scriptingDefines.Count > 0)
                    UpdateScriptingDefineSymbols(setupConfigJson.core.packages.scriptingDefines);
                if(setupConfigJson.core.tags != null && setupConfigJson.core.tags.Count > 0)
                    UpdateTags(setupConfigJson.core.tags);
            }

            //handle optional packages
            foreach (var package in optionalPackagesSelected)
            {
                if(setupConfigJson.optionalPackages.ContainsKey(package))
                {
                    // Debug.Log("Installing optional package: " + package);
                    UpdatePackageManifest(setupConfigJson.optionalPackages[package].packages);
                    
                    #if UNITY_VISUAL_SCRIPTING
                    UpdateVisualScriptingSettings(setupConfigJson.optionalPackages[package].visualScripting);
                    #else
                    //set a flag for the visual scripting define symbol to be added after domain reload
                    setupMetadata.AddVisualScriptingDefineSymbol = true;
                    //Set a flag to trigger visual scripting initialization after domain reload.
                    setupMetadata.InitializeVisualScripting = true;
                    //Set a flag to Update visual scripting settings after a domain reload.
                    setupMetadata.UpdateVisualScriptingForInstalledPackages = true;
                    WriteMeadowSetupData(setupMetadata);
                    #endif

                    if(setupConfigJson.optionalPackages[package].packages.scriptingDefines != null && setupConfigJson.optionalPackages[package].packages.scriptingDefines.Count > 0)
                        UpdateScriptingDefineSymbols(setupConfigJson.optionalPackages[package].packages.scriptingDefines);
                    
                    if(setupConfigJson.optionalPackages[package].tags != null && setupConfigJson.optionalPackages[package].tags.Count > 0)
                        UpdateTags(setupConfigJson.optionalPackages[package].tags);
                }
            }

            //add the optional packages to the setup metadata
            setupMetadata.OptionalPackagesInstalled = optionalPackagesSelected;

            // write setup data with SetupComplete set to true
            setupMetadata.SetupComplete = true;
            setupMetadata.LastSetupConfigVersion = setupConfigJson.metadata.configurationVersion;
            WriteMeadowSetupData(setupMetadata);

            //domain reload the editor
            EditorUtility.RequestScriptReload();

            setupProcessActive = false;
        }

        public static void InitialSetupProcess(bool includeOptionalPackages = false)
        {
            setupProcessActive = true;

            //open the setup loading UI
            MeadowSetupWindow wnd = CreateWindow("Meadow Setup");
            wnd.CreateSetupLoadingUI();

            //handle core packages
            UpdatePackageManifest(setupConfigJson.core.packages);
            if(setupConfigJson.core.packages.scriptingDefines != null && setupConfigJson.core.packages.scriptingDefines.Count > 0)
                UpdateScriptingDefineSymbols(setupConfigJson.core.packages.scriptingDefines);
            if(setupConfigJson.core.tags != null && setupConfigJson.core.tags.Count > 0)
                UpdateTags(setupConfigJson.core.tags);

            if(includeOptionalPackages)
            {
                //TODO update this logic when there are more optional packages
                string package = "xref-experience-builder";

                //handle optional packages
                if(setupConfigJson.optionalPackages.ContainsKey(package))
                {
                    // Debug.Log("Installing optional package: " + package);
                    UpdatePackageManifest(setupConfigJson.optionalPackages[package].packages);
                    
                    #if UNITY_VISUAL_SCRIPTING
                    UpdateVisualScriptingSettings(setupConfigJson.optionalPackages[package].visualScripting);
                    #else
                    //set a flag for the visual scripting define symbol to be added after domain reload
                    setupMetadata.AddVisualScriptingDefineSymbol = true;
                    //Set a flag to trigger visual scripting initialization after domain reload.
                    setupMetadata.InitializeVisualScripting = true;
                    //Set a flag to Update visual scripting settings after a domain reload.
                    setupMetadata.UpdateVisualScriptingForInstalledPackages = true;
                    WriteMeadowSetupData(setupMetadata);
                    #endif

                    if(setupConfigJson.optionalPackages[package].packages.scriptingDefines != null && setupConfigJson.optionalPackages[package].packages.scriptingDefines.Count > 0)
                        UpdateScriptingDefineSymbols(setupConfigJson.optionalPackages[package].packages.scriptingDefines);
                    
                    if(setupConfigJson.optionalPackages[package].tags != null && setupConfigJson.optionalPackages[package].tags.Count > 0)
                        UpdateTags(setupConfigJson.optionalPackages[package].tags);
                }
            
                //add the optional packages to the setup metadata
                setupMetadata.OptionalPackagesInstalled = new List<string> { "xref-experience-builder" };
            }

            //write setup data with SetupComplete set to true
            setupMetadata.SetupComplete = true;
            setupMetadata.LastSetupConfigVersion = setupConfigJson.metadata.configurationVersion;
            WriteMeadowSetupData(setupMetadata);

            //domain reload the editor
            EditorUtility.RequestScriptReload();

            setupProcessActive = false;
        }

        public static void UpdateSetupProcess()
        {
            setupProcessActive = true;

            if(setupMetadata == null)
            {
                setupMetadata = LoadMeadowSetupData();
            }

            if(setupMetadata.SetupComplete)
            {
                //update core packages
                UpdatePackageManifest(setupConfigJson.core.packages);
                if(setupConfigJson.core.packages.scriptingDefines != null && setupConfigJson.core.packages.scriptingDefines.Count > 0)
                    UpdateScriptingDefineSymbols(setupConfigJson.core.packages.scriptingDefines);
                if(setupConfigJson.core.tags != null && setupConfigJson.core.tags.Count > 0)
                    UpdateTags(setupConfigJson.core.tags);
                
                if(setupMetadata.OptionalPackagesInstalled != null && setupMetadata.OptionalPackagesInstalled.Count > 0)
                {
                    foreach(string package in setupMetadata.OptionalPackagesInstalled)
                    {
                        if(setupConfigJson.optionalPackages.ContainsKey(package))
                        {
                            //update optional packages
                            UpdatePackageManifest(setupConfigJson.optionalPackages[package].packages);
                            #if UNITY_VISUAL_SCRIPTING
                            if(setupConfigJson.optionalPackages[package].visualScripting != null)
                                UpdateVisualScriptingSettings(setupConfigJson.optionalPackages[package].visualScripting);
                            #endif
                            if(setupConfigJson.optionalPackages[package].packages.scriptingDefines != null && setupConfigJson.optionalPackages[package].packages.scriptingDefines.Count > 0)
                                UpdateScriptingDefineSymbols(setupConfigJson.optionalPackages[package].packages.scriptingDefines);
                            if(setupConfigJson.optionalPackages[package].tags != null && setupConfigJson.optionalPackages[package].tags.Count > 0)
                                UpdateTags(setupConfigJson.optionalPackages[package].tags);
                        }
                    }
                }

                //write setup data with SetupComplete set to true
                setupMetadata.SetupComplete = true;
                setupMetadata.LastSetupConfigVersion = setupConfigJson.metadata.configurationVersion;
                setupMetadata.ConfigUpdateAvailable = false;
                setupMetadata.UpdatingConfig = true;
                WriteMeadowSetupData(setupMetadata);

                //domain reload the editor
                EditorUtility.RequestScriptReload();

                setupProcessActive = false;
            }
        }

        private static void UpdatePackageManifest(Packages packagesConfig)
        {
            //get the manifest.json file
            var manifestPath = Path.Combine(Application.dataPath, "..", "Packages/manifest.json");
            if (File.Exists(manifestPath))
            {
                var manifestJson = File.ReadAllText(manifestPath);
                var manifest = JsonConvert.DeserializeObject<ManifestJson>(manifestJson);

                if(packagesConfig.scopedRegistries != null && packagesConfig.scopedRegistries.Count > 0)
                {
                    //TODO handle multiple scoped registries
                    //get the scoped registry from the setup config
                    ScopedRegistry sr = packagesConfig.scopedRegistries[0];

                    //if the scoped registry is not in the manifest add it
                    bool registryExists = false;
                    foreach (var registry in manifest.scopedRegistries)
                    {
                        if (registry.name == sr.name || registry.url == sr.url)
                        {
                            registryExists = true;
                            break;
                        }
                    }
                    if(!registryExists)
                        manifest.scopedRegistries.Add(sr);
                }

                //get the packages from the setup config
                Dictionary<string, string> dependencies = packagesConfig.dependencies;

                if(dependencies == null || dependencies.Count == 0)
                {
                    // Debug.LogError("No dependencies found in the setup config.");
                    return;
                }
                
                //if the package is not in the manifest add it, if it is check if the version is correct
                //TODO update packages using the packagemanager api
                foreach (var dependency in dependencies)
                {
                    if (manifest.dependencies.ContainsKey(dependency.Key))
                    {
                        //TODO Add logic to check if the version is lower or higher than the required version
                        if(manifest.dependencies[dependency.Key] == dependency.Value)
                            continue;
                        else
                            manifest.dependencies[dependency.Key] = dependency.Value;
                    }
                    else
                    {
                        manifest.dependencies.Add(dependency.Key, dependency.Value);
                    }
                }

                //write the manifest back to the file
                File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented));

                //write setup data with RefreshAssetDatabase set to true
                //refreshing the assetdatabase needs to happen after domain reload, had some issues with the domain reload always happening first so this is the workaround
                setupMetadata.RefreshAssetDatabase = true;
                WriteMeadowSetupData(setupMetadata);
            }
        }

        private static void UpdateVisualScriptingSettings(VisualScripting visualScriptingConfig)
        {
            // Debug.Log("Updating Visual Scripting settings...");
            //check that the template visual scripting settings file exists
            if(setupMetadata != null)
            {
                //load the visual scripting settings file from the project
                var visualScriptingSettingsPath = Path.Combine(Application.dataPath, "..", "ProjectSettings/VisualScriptingSettings.asset");
                // string projectYamlString = await SetupService.GetVisualScriptingSettingsString(visualScriptingSettingsPath);
                string projectYamlString = File.ReadAllText(visualScriptingSettingsPath);

                if(string.IsNullOrEmpty(projectYamlString))
                {
                    // Debug.LogError("Error loading Visual Scripting settings file.");
                    return;
                }

                // Debug.Log("Visual Scripting settings loaded: " + projectYamlString);

                //extract the json from the project visual scripting settings file
                JObject projectJson = ExtractVisualScriptingSettingsJSON(projectYamlString);
                
                //get the assembly options and add the required assemblies
                var templateAssemblyOptions = visualScriptingConfig.assemblyOptions;
                var projectAssemblyOptions = projectJson["dictionary"]?["assemblyOptions"]?["$content"] as JArray;
                if(templateAssemblyOptions != null && projectAssemblyOptions != null)
                {
                    // Create a HashSet for fast lookup of existing assembly options
                    HashSet<string> existingOptions = new HashSet<string>(projectAssemblyOptions.ToObject<List<string>>());

                    // Loop over the template assemblyOptions and add to the project assemblyOptions if not present
                    foreach (var option in templateAssemblyOptions)
                    {
                        string optionString = option.ToString();
                        if (!existingOptions.Contains(optionString))
                        {
                            projectAssemblyOptions.Add(optionString);
                            existingOptions.Add(optionString);
                        }
                    }
                }

                //get the type options and add the required types
                var templateTypeOptions = visualScriptingConfig.typeOptions;
                var projectTypeOptions = projectJson["dictionary"]?["typeOptions"]?["$content"] as JArray;
                if(templateTypeOptions != null && projectTypeOptions != null)
                {
                    // Create a HashSet for fast lookup of existing type options
                    HashSet<string> existingOptions = new HashSet<string>(projectTypeOptions.ToObject<List<string>>());

                    // Loop over the template typeOptions and add to the project typeOptions if not present
                    foreach (var option in templateTypeOptions)
                    {
                        string optionString = option.ToString();
                        if (!existingOptions.Contains(optionString))
                        {
                            projectTypeOptions.Add(optionString);
                            existingOptions.Add(optionString);
                        }
                    }
                }
                
                //write the updated projectAssemblyOptions and projectTypeOptions back to the projectJson
                projectJson["dictionary"]["assemblyOptions"]["$content"] = projectAssemblyOptions;
                projectJson["dictionary"]["typeOptions"]["$content"] = projectTypeOptions;

                //modify the project visual scripting settings file
                string updatedYamlContent = ReplaceVisualScriptingSettingsJSON(projectYamlString, projectJson);

                // Save the updated YAML content back to the file (optional)
                File.WriteAllText(visualScriptingSettingsPath, updatedYamlContent);

                //write setup data with RegenerateVisualScriptingNodes set to true
                //regenerating nodes needs to happen after domain reload, had some issues with the domain reload always happening first so this is the workaround
                setupMetadata.RegenerateVisualScriptingNodes = true;
                WriteMeadowSetupData(setupMetadata);
            }
        }

        

        private static void UpdateScriptingDefineSymbols(List<string> defineSymbolsConfig)
        {
            foreach (var defineSymbol in defineSymbolsConfig)
            {
                //if ios build target exists
                if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS))
                {
                    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.iOS, PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS) + ";" + defineSymbol);
                }
                //if android build target exists
                if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android) + ";" + defineSymbol);
                }
                //if standalone build target exists
                if (BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows) || BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                {
                    PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) + ";" + defineSymbol);
                }
            }
        }

        private static void UpdateTags(List<string> tagsConfig)
        {
            foreach (string tag in tagsConfig)
            {
                if (!UnityEditorInternal.InternalEditorUtility.tags.Contains(tag))
                {
                    UnityEditorInternal.InternalEditorUtility.AddTag(tag);
                }
            }
        }

    #endregion

    #region Setup Helpers

        private static string GetSetupConfig()
        {
            using UnityWebRequest unityWebRequest = UnityWebRequest.Get("https://firebasestorage.googleapis.com/v0/b/xref-client.appspot.com/o/appconfig%2Fmeadow-unity-config.json?alt=media");
            unityWebRequest.SendWebRequest();
            while (!unityWebRequest.isDone)
            {
                Thread.Sleep(10);
            }

            if (unityWebRequest.result == UnityWebRequest.Result.ConnectionError || unityWebRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                // Debug.LogError("Error downloading file: " + unityWebRequest.error);

                //check if there is a meadow-unity-config.json file in the persistent data path
                string cachedPath = Path.Combine(Application.persistentDataPath, "meadow-unity-config.json");
                if(File.Exists(cachedPath))
                {
                    return File.ReadAllText(cachedPath);
                }
                else
                {
                    //get the default config file from the resources folder
                    string defaultPath = "Assets/Meadow-Studio/Resources/meadow-unity-config.json";
                    if(File.Exists(defaultPath))
                    {
                        return File.ReadAllText(defaultPath);
                    }
                }

                //unable to download the config file and no local file found.
                return "";
            }

            //cache the config file in the persistent data path
            string configString = unityWebRequest.downloadHandler.text;
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "meadow-unity-config.json"), configString);

            return unityWebRequest.downloadHandler.text;
        }

        private static bool CompareConfigVersions(string currentVersion, string latestVersion)
        {
            string[] currentParts = currentVersion.Split('.');
            string[] latestParts = latestVersion.Split('.');
            
            for (int i = 0; i < Mathf.Max(currentParts.Length, latestParts.Length); i++)
            {
                int currentPart = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;
                int latestPart = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;

                if (latestPart > currentPart)
                {
                    return true;
                }
                else if (latestPart < currentPart)
                {
                    return false;
                }
            }
            
            return false;
        }
        
        private static JObject ExtractVisualScriptingSettingsJSON(string yamlString)
        {
            //parse the yaml file and extract the json content
            string jsonKey = "_json: '";
            int jsonStartIndex = yamlString.IndexOf(jsonKey) + jsonKey.Length;
            int jsonEndIndex = yamlString.IndexOf("'", jsonStartIndex);
            string jsonContent = yamlString.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex);

            // Replace escaped quotes and newline characters
            jsonContent = jsonContent.Replace("\\\"", "\"").Replace("\\n", "\n");
            
            //create a json object from the json content
            JObject jObject = JObject.Parse(jsonContent);

            return jObject;
        }

        private static string ReplaceVisualScriptingSettingsJSON(string yamlString, JObject json)
        {
            string jsonKey = "_json: '";
            int jsonStartIndex = yamlString.IndexOf(jsonKey) + jsonKey.Length;
            int jsonEndIndex = yamlString.IndexOf("'", jsonStartIndex);
            string originalJsonContent = yamlString.Substring(jsonStartIndex, jsonEndIndex - jsonStartIndex);

            // Replace the original JSON content with the updated JSON content
            string jsonContent = json.ToString(Formatting.None);
            
            // Properly escape the JSON string for YAML
            jsonContent = jsonContent.Replace("\\n", "\n");

            //replace the json content in the yaml string
            string updatedYamlContent = yamlString.Replace(originalJsonContent, jsonContent);

            return updatedYamlContent;
        }
        
        public static MeadowSetupData LoadMeadowSetupData()
        {   
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-setup.data");

            // Check if the file exists
            if(File.Exists(path))
            {
                // Create a binary formatter
                BinaryFormatter formatter = new BinaryFormatter();

                // Create a file stream
                using (FileStream stream = new FileStream(path, FileMode.Open))
                {
                    // Deserialize the User object and return it
                    MeadowSetupData data = formatter.Deserialize(stream) as MeadowSetupData;
                    return data;
                }
            }
            else
            {
                // Create a new MeadowSetupData object and return it
                MeadowSetupData data = new MeadowSetupData();
                return null;
            }
        }

        private static void WriteMeadowSetupData(MeadowSetupData data)
        {
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-setup.data");

            // Create a binary formatter
            BinaryFormatter formatter = new BinaryFormatter();

            // Create a file stream
            using (FileStream stream = new FileStream(path, FileMode.Create))
            {
                // Serialize the User object and write it to the file
                formatter.Serialize(stream, data);
            }
        }
    #endregion

    #region JSON deserialization classes
        private class ScopedRegistry {
            public string name;
            public string url;
            public string[] scopes;
        }

        private class ManifestJson {
            public Dictionary<string,string> dependencies = new Dictionary<string, string>();
    
            public List<ScopedRegistry> scopedRegistries = new List<ScopedRegistry>();
        }

        private class SetupConfig
        {
            public Metadata metadata { get; set; }
            public MeadowPackage core { get; set; }
            public Dictionary<string, MeadowPackage> optionalPackages { get; set; }
        }

        private class Metadata
        {
            public string configurationVersion { get; set; }
        }

        private class MeadowPackage
        {
            public Packages packages { get; set; }
            public VisualScripting visualScripting { get; set; }
            public List<string> tags { get; set; }
            public Dictionary<string, packageDefineHandler> packageDefineHandlers { get; set; }
        }

        private class Packages
        {
            public Dictionary<string, string> dependencies { get; set; }
            public List<ScopedRegistry> scopedRegistries { get; set; }
            public List<string> scriptingDefines { get; set; }
        }

        private class packageDefineHandler
        {
            public bool anyRequirement { get; set; }
            public List<string> requirements { get; set; } 
        }
        
        private class VisualScripting
        {
            public List<string> assemblyOptions { get; set; }
            public List<string> typeOptions { get; set; }
        }
    #endregion
    }

    [System.Serializable]
    public class MeadowSetupData
    {
        //Setup metadata
        public bool SetupComplete = false;
        public string LastSetupConfigVersion = "0.0.0";
        public bool userPromptedForSetup = false;
        public bool ConfigUpdateAvailable = false;
        public bool UpdatingConfig = false;
        public List<string> OptionalPackagesInstalled = new List<string>();
        
        //Visual Scripting flags
        /*
        Logic:
        We are using flags inside of the metadata to trigger certain actions after a domain reload.
        For example if we are adding typeOption or assemblyOptions to the VisualScriptingSettings we need to do it after the domain reload has been fully completed.
        After a domain reload the OnEnable method is called and we can check these flags to see if we need to perform any actions left over from before the domain needed reloading.
        There may be a better way of achieving these actions.
        */

        //Refresh flags
        public bool InitializeVisualScripting = false;
        public bool AddVisualScriptingDefineSymbol = false;
        public bool UpdateVisualScriptingForInstalledPackages = false;
        public bool RegenerateVisualScriptingNodes = false;
        public bool RefreshAssetDatabase = false;
    }
}