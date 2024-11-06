//-----------------------------------------------------------------------
// <copyright file="MeadowStudioWindow.cs" company="Untold Garden LTD">
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meadow.Studio
{
    [InitializeOnLoad]
    public class MeadowStudioWindow : EditorWindow
    {
        static MeadowStudioData studioMetadata;
        private JObject parsedResponse;
        private HashSet<string> selectedExperienceIds = new HashSet<string>();
        private Dictionary<string, List<string>> assetBundlePaths = new Dictionary<string, List<string>>();

        private Texture2D loadingIcon;
        float angle = 0.0f;
        private bool isLoading = false;

        private static bool CheckingForUpdates = false;

        private bool simplifiedExperiencesView = false;

        //upload page
        private string bundleType = "Experience";
        private bool inspectBundleFoldoutOpen = false;
        private string selectedABName = "";
        private bool iosPlatformEnabled = false;
        private bool androidPlatformEnabled = false;

        readonly AuthService authService = new();
        readonly BundleService bundleService = new();
        readonly MetadataService metadataService = new();
        readonly static UpdateService  updateService = new();
        readonly PluginUtils pluginUtil = new();

        User user;


        [MenuItem("Meadow/Meadow Studio", false, 0)]
        public static void MeadowStudio()
        {
            PluginUtils pluginUtil = new PluginUtils();
            Texture logo = AssetDatabase.LoadAssetAtPath<Texture>(pluginUtil.GetPluginDir(true)+ "/Resources/logo_icon.png");

            MeadowStudioWindow wnd = GetWindow<MeadowStudioWindow>();
            wnd.titleContent = new GUIContent("Meadow Studio", logo);
        }

        [MenuItem("Meadow/Check for Updates", false, 200)]
        public static void CheckForUpdatesMenu()
        {
            studioMetadata.LastUpdateCheckTime = (float)EditorApplication.timeSinceStartup;
            CheckForUpdates();
        }

        // [MenuItem("Meadow/Sign Out", false, 1000)]
        public static void SignOut()
        {
            MeadowStudioWindow wnd = GetWindow<MeadowStudioWindow>();
            wnd.authService.DeleteLoginData();
            wnd.CreateSignInUI();
        }

        void OnGUI()
        {
            if (isLoading && loadingIcon != null)
            {
                Rect rect = new Rect(position.width - 50, 0, 50, 50);
                Matrix4x4 matrix = GUI.matrix;

                GUIUtility.RotateAroundPivot(angle, rect.center);
                GUI.DrawTexture(rect, loadingIcon);
                GUI.matrix = matrix;
            }
        }

        static MeadowStudioWindow()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        void OnEnable()
        {
            loadingIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(pluginUtil.GetPluginDir(true)+"/Resources/loading.png");
            EditorApplication.update += OnEditorUpdate;
            CreateSignInUI();
            
            // pluginUtil.RetrieveAllAssetPaths(assetBundlePaths);
        }
        private static void OnEditorUpdate()
        {
            InitializeStudioMetadata();

            if((EditorApplication.timeSinceStartup % 240 <= 0.01 || (studioMetadata != null && (EditorApplication.timeSinceStartup - studioMetadata.LastUpdateCheckTime) > 240)) && !CheckingForUpdates)
            {
                CheckingForUpdates = true;
                // Debug.Log("Checking for updates");
                studioMetadata.LastUpdateCheckTime = (float)EditorApplication.timeSinceStartup;
                CheckForUpdates();
            }
            else if(EditorApplication.timeSinceStartup % 240 > 0.01 && CheckingForUpdates)
            {
                CheckingForUpdates = false;
            }
        }
        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private async void UpdateExperienceList(string email)
        {
            //update asset bundle paths
            assetBundlePaths = pluginUtil.RetrieveAllAssetPaths();
            
            try
            {
                await RunOnMainThread(() =>
                {
                    isLoading = true;
                });

                var res = await metadataService.GetUserExperienceMetadatas(email);

                if (res.success)
                {
                    parsedResponse = res.data as JObject;
                    await RunOnMainThread(() =>
                    {
                        CreateMainUI(parsedResponse);
                    });
                }
                else
                {
                    Debug.LogError("Error:" + res.message);
                    isLoading = false;
                    EditorUtility.DisplayDialog("Error", "Error authenticating user", "OK");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Error:" + e.Message);
                isLoading = false;
                EditorUtility.DisplayDialog("Error", "Error authenticating user", "OK");
            }
        }

        /// <summary>
        /// Creates the sign in UI
        /// </summary>
        private void CreateSignInUI()
        {
            //Clear the root visual element
            VisualElement root = rootVisualElement;
            root.Clear();

            //Check if user is already signed in
            User savedUser = authService.LoadLoginData();

            //Saved user exists so we can skip the sign in process
            if (savedUser != null)
            {
                authService.currentUser = savedUser;
                user = savedUser;
                UpdateExperienceList(savedUser.Email);
                // CoroutineRunner.Instance.StartCoroutine(GetUserExperienceMetadatas(savedUser.Email));
                return;
            }
            //create the sign in UI from a uidocument
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true) + "/UI/Studio/sign-in.uxml");
            visualTree.CloneTree(root);

            //Check if any banners need to be displayed and add them
            CheckForBanners();

            //set the logo-text-container sprite color depending on the theme
            VisualElement logoTextContainer = root.Query<VisualElement>("logo-text-container");
            logoTextContainer.style.unityBackgroundImageTintColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f) : new Color(0.2f, 0.2f, 0.2f);

            //setup the email field
            TextField emailField = root.Query<TextField>("email-text-field");
            emailField.value = savedUser != null ? savedUser.Email : "";

            //setup the password field
            TextField passwordField = root.Query<TextField>("password-text-field");

            //setup the sign in button
            Button signInButton = root.Query<Button>("sign-in-button");
            signInButton.clicked += async () =>
            {
                isLoading = true;


                // CoroutineRunner.Instance.StartCoroutine(SignInLoadingProcess(10f));
                EditorUtility.DisplayProgressBar("Meadow", "Signing in...", 0.2f);

                var res = await authService.AuthenticateUser(emailField.value, passwordField.value);
                EditorUtility.DisplayProgressBar("Meadow", "Signing in...", 1);
                EditorUtility.ClearProgressBar();
                if (res.data == null)
                {
                    Debug.LogError("Error:" + res.message);
                    isLoading = false;
                    EditorUtility.DisplayDialog("Error", "Error authenticating user", "OK");
                }
                else
                {
                    user = res.data as User;
                    authService.currentUser = user;
                    isLoading = false;
                    UpdateExperienceList(user.Email);

                    // CoroutineRunner.Instance.StartCoroutine(GetUserExperienceMetadatas(emailField.value));
                }
            };
        }

        /// <summary>
        /// Opens the Main UI
        /// </summary>
        /// <param name="metadata"></param>
        private async void CreateMainUI(JObject metadata)
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            //create from ui document
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true) + "/UI/Studio/main.uxml");
            visualTree.CloneTree(root);

            //Check if any banners need to be displayed and add them
            CheckForBanners();

            //set the user profile information
            VisualElement userImage = root.Query<VisualElement>("user-image");
            var imgResp = await ImageService.GetImage(authService.currentUser.ProfileThumbnail);

            if (imgResp.success)
            {
                userImage.style.backgroundImage = imgResp.data as Texture2D;
                userImage.style.unityBackgroundImageTintColor = Color.white;
            }
            else
            {
                userImage.style.backgroundImage = AssetDatabase.LoadAssetAtPath<Texture2D>(pluginUtil.GetPluginDir(true) + "/Resources/profile-placeholder.png");
                // Debug.LogError("Error getting user image: " + imgResp.message);
            }

            //set user button
            Button userButton = root.Query<Button>("user-button");
            VisualElement signoutIcon = root.Query<VisualElement>("user-signout-icon");
            userButton.clicked += () =>
            {
                //create sign out dialog
                if (EditorUtility.DisplayDialog("Meadow Sign Out", "Are you sure you want to sign out?", "Yes", "No"))
                {
                    SignOut();
                }
                else
                {
                    userImage.style.display = DisplayStyle.Flex;
                    signoutIcon.style.display = DisplayStyle.None;
                }
            };

            //swap user image with signout-icon on hover
            userButton.RegisterCallback<MouseEnterEvent>((evt) =>
            {
                userImage.style.display = DisplayStyle.None;
                signoutIcon.style.display = DisplayStyle.Flex;
            });

            //swap signout-icon with user image on mouse leave
            userButton.RegisterCallback<MouseLeaveEvent>((evt) =>
            {
                userImage.style.display = DisplayStyle.Flex;
                signoutIcon.style.display = DisplayStyle.None;
            });

            Label userName = root.Query<Label>("user-name");
            userName.text = authService.currentUser.DisplayName;

            //if root width is less than 350, hide the profile picture and name
            if (root.layout.width < 375)
            {
                // userImage.style.display = DisplayStyle.None;
                userName.style.display = DisplayStyle.None;
            }

            //setup an event on the width changed event to hide the profile picture and name below a certain width
            root.RegisterCallback<GeometryChangedEvent>((evt) =>
            {
                if (evt.newRect.width < 375)
                {
                    // userImage.style.display = DisplayStyle.None;
                    userName.style.display = DisplayStyle.None;
                }
                else
                {
                    // userImage.style.display = DisplayStyle.Flex;
                    userName.style.display = DisplayStyle.Flex;
                }
            });

            //set the refresh button
            Button refreshButton = root.Query<Button>("refresh-button");
            refreshButton.clicked += () =>
            {

                UpdateExperienceList(authService.currentUser.Email);
                // CoroutineRunner.Instance.StartCoroutine(GetUserExperienceMetadatas(authService.currentUser.Email));
            };

            //set filter buttons
            Button compactButton = root.Query<Button>("compact-button");
            Button expandButton = root.Query<Button>("expand-button");
            compactButton.style.display = simplifiedExperiencesView ? DisplayStyle.None : DisplayStyle.Flex;
            expandButton.style.display = simplifiedExperiencesView ? DisplayStyle.Flex : DisplayStyle.None;

            compactButton.clicked += () =>
            {
                compactButton.style.display = DisplayStyle.None;
                expandButton.style.display = DisplayStyle.Flex;
                simplifiedExperiencesView = true;
                RemoveExperienceList();
                CreateExperienceList(metadata, root);
            };
            expandButton.clicked += () =>
            {
                compactButton.style.display = DisplayStyle.Flex;
                expandButton.style.display = DisplayStyle.None;
                simplifiedExperiencesView = false;
                RemoveExperienceList();
                CreateExperienceList(metadata, root);
            };

            CreateExperienceList(metadata, root);
        }

        private void CheckForBanners()
        {
            //check if there is a config update available
            if(MeadowSetupWindow.setupMetadata != null && MeadowSetupWindow.setupMetadata.SetupComplete && MeadowSetupWindow.setupMetadata.ConfigUpdateAvailable)
            {
                //get the banner-config.uxml file
                var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true) + "/UI/Studio/banner-config.uxml");
                
                //add the banner to the root visual element
                VisualElement root = rootVisualElement;
                visualTree.CloneTree(root);

                //setup the setup button
                Button setupButton = root.Query<Button>("config-button");
                setupButton.clicked += () =>
                {
                    if(EditorUtility.DisplayDialog("Meadow Config Update", "A Meadow config update is available. Would you like to update the project now?", "Yes", "Later"))
                    {
                        MeadowSetupWindow.UpdateSetupProcess();
                    }
                };
            }

            //check if there is an update available
            //Add logic here that is loaded from a metadata file
            if(studioMetadata != null && studioMetadata.UpdateAvailable == true && (studioMetadata.UpdateVersion != updateService.GetCurrentVersion()))
            {
                //get the banner-update.uxml file
                var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true) + "/UI/Studio/banner-update.uxml");

                //add the banner to the root visual element
                VisualElement root = rootVisualElement;
                visualTree.CloneTree(root);

                //setup the update button
                Button updateButton = root.Query<Button>("update-button");
                updateButton.clicked += () =>
                {
                    //check for updates
                    studioMetadata.LastUpdateCheckTime = (float)EditorApplication.timeSinceStartup;
                    CheckForUpdates(true);
                };
            }
        }

        /// <summary>
        /// Creates the experience list view and adds it to the content-container
        /// </summary>
        /// <param name="metadata">the experience metadata</param>
        /// <param name="root">the root visual element</param>
        private void CreateExperienceList(JObject metadata, VisualElement root)
        {
            VisualTreeAsset experienceCardAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true)+"/UI/Studio/experience-card.uxml");
            Func<VisualElement> makeItem = () => experienceCardAsset.CloneTree();
            Action<VisualElement, int> bindItem = (e, i) => 
            {
                var property = metadata.Properties().ElementAt(i);
                SetExperiencePost(e, property.Name, property.Value as JObject);
            };

            int itemHeight = (int)experienceCardAsset.CloneTree().layout.height;
            if(simplifiedExperiencesView)
                itemHeight = 20;

            //create a listview from the metadata
            ListView listView = new ListView(metadata, itemHeight, makeItem, bindItem);
            listView.name = "experience-list";
            listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            

            //on selection change
            listView.selectionChanged += objects =>
            {
                if (objects.Count() > 0)
                {
                    var property = objects.ElementAt(0) as JProperty;
                    CreateUploadUI(property.Name, property.Value["titles"]["en"]?.ToString() ?? property.Value["titles"]["English"]?.ToString() ?? "Experience");
                }
            };

            //add the listview to the root
            VisualElement contentContainer = root.Query<VisualElement>("content-container");
            contentContainer.Add(listView);
        }

        /// <summary>
        /// Removes the experience list from the root visual element
        /// </summary>
        public void RemoveExperienceList()
        {
            ListView listView = rootVisualElement.Query<ListView>("experience-list");
            if (listView != null)
            {
                listView.RemoveFromHierarchy();
            }
        }

        /// <summary>
        /// Rebuilds the experience list
        /// </summary>
        public void RebuidExperienceList()
        {
            ListView listView = rootVisualElement.Query<ListView>("experience-list");
            if (listView != null)
            {
                listView.Rebuild();
            }
        }

        /// <summary>
        /// Sets the experience list posts
        /// </summary>
        /// <param name="post">the list view post to set</param>
        /// <param name="id">experience id</param>
        /// <param name="metadata">experience metadata</param>
        public async void SetExperiencePost(VisualElement post, string id, JObject metadata)
        {
            //set experience name
            Label title = post.Query<Label>("experience-title");
            title.text = metadata["titles"]["en"]?.ToString() ?? metadata["titles"]["English"]?.ToString() ?? "Experience";

            //set experience thumbnail
            VisualElement thumbnail = post.Query<VisualElement>("experience-thumbnail");
            thumbnail.style.display = simplifiedExperiencesView ? DisplayStyle.None : DisplayStyle.Flex;
            string url = $"https://storage.googleapis.com/xref-client.appspot.com/artworkdata%2F{id}%2Fimages%2Fthumbs%2Fcover_200x200";
            var imgResp = await ImageService.GetImage(url);
            if (imgResp.success)
            {
                thumbnail.style.backgroundImage = imgResp.data as Texture2D;
            }
            else
            {
                // Debug.LogError("Error getting experience thumbnail: " + imgResp.message);
                thumbnail.style.backgroundImage = null;
            }
        }

        private async void CreateUploadUI(string experienceId, string title, bool refresh = false)
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            //create the upload ui from a uxml document
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true)+"/UI/Studio/upload.uxml");
            visualTree.CloneTree(root);

            //Check if any banners need to be displayed and add them
            CheckForBanners();

            //set the title
            Label experienceTitleLabel = root.Query<Label>("experience-title-label");
            experienceTitleLabel.text = title;

            //set the artwork image
            VisualElement experienceImage = root.Query<VisualElement>("experience-image");
            string artworkUrl = $"https://storage.googleapis.com/xref-client.appspot.com/artworkdata%2F{experienceId}%2Fimages%2Fthumbs%2Fcover_600x600";
            var imgResp = await ImageService.GetImage(artworkUrl);
            if (imgResp.success)
            {   
                experienceImage.style.display = DisplayStyle.Flex;
                experienceImage.style.backgroundImage = imgResp.data as Texture2D;
            }
            else
            {
                experienceImage.style.display = DisplayStyle.None;
            }

            //set the back button
            Button backButton = root.Query<Button>("back-button");
            backButton.clicked += () =>
            {
                CreateMainUI(parsedResponse);
            };

            //set the refresh button
            Button refreshButton = root.Query<Button>("refresh-button");
            refreshButton.clicked += () =>
            {
                assetBundlePaths = pluginUtil.RetrieveAllAssetPaths();
                CreateUploadUI(experienceId, title, true);
            };

            //set the user profile information
            VisualElement userImage = root.Query<VisualElement>("user-image");
            Label userName = root.Query<Label>("user-name");
            userName.text = authService.currentUser.DisplayName;

            //if root width is less than 375, hide the profile picture and name
            if (root.layout.width < 375)
            {
                userName.style.display = DisplayStyle.None;
            }

            //on geometry changed event, hide the profile picture and name if the root width is less than 375
            root.RegisterCallback<GeometryChangedEvent>((evt) =>
            {
                if (evt.newRect.width < 375)
                {
                    userName.style.display = DisplayStyle.None;
                }
                else
                {
                    userName.style.display = DisplayStyle.Flex;
                }
            });

            imgResp = await ImageService.GetImage(authService.currentUser.ProfileThumbnail);

            if (imgResp.success)
            {
                userImage.style.backgroundImage = imgResp.data as Texture2D;
                userImage.style.unityBackgroundImageTintColor = Color.white;
            }
            else
            {
                userImage.style.backgroundImage = AssetDatabase.LoadAssetAtPath<Texture2D>(pluginUtil.GetPluginDir(true) + "/Resources/profile-placeholder.png");
                // Debug.LogError("Error getting user image: " + imgResp.message);
            }

            //set user button
            Button userButton = root.Query<Button>("user-button");
            VisualElement signoutIcon = root.Query<VisualElement>("user-signout-icon");
            userButton.clicked += () =>
            {
                //create sign out dialog
                if (EditorUtility.DisplayDialog("Meadow Sign Out", "Are you sure you want to sign out?", "Yes", "No"))
                {
                    SignOut();
                }
                else
                {
                    userImage.style.display = DisplayStyle.Flex;
                    signoutIcon.style.display = DisplayStyle.None;
                }
            };

            //swap user image with signout-icon on hover
            userButton.RegisterCallback<MouseEnterEvent>((evt) =>
            {
                userImage.style.display = DisplayStyle.None;
                signoutIcon.style.display = DisplayStyle.Flex;
            });

            //swap signout-icon with user image on mouse leave
            userButton.RegisterCallback<MouseLeaveEvent>((evt) =>
            {
                userImage.style.display = DisplayStyle.Flex;
                signoutIcon.style.display = DisplayStyle.None;
            });

            //set the inspect bundle foldout
            Foldout inspectBundleFoldout = root.Query<Foldout>("inspect-bundle-foldout");
            inspectBundleFoldout.SetEnabled(false);
            inspectBundleFoldout.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                if (evt.newValue)
                {
                    inspectBundleFoldoutOpen = evt.newValue;
                }
            });

            //set the inspect columns
            VisualElement filesColumn = root.Query<VisualElement>("files-column");
            VisualElement fileInfoColumn = root.Query<VisualElement>("info-column");
            Label fileSizeLabel = root.Query<Label>("file-size-label");

            //set up the asset bundle dropdown
            PopupField<string> dropdown = root.Query<PopupField<string>>("asset-bundle-dropdown");
            dropdown.choices = assetBundlePaths.Keys.ToList();
            //event on value change to set selectedABName
            if(assetBundlePaths.ContainsKey(selectedABName) && refresh)
            {
                dropdown.value = selectedABName;
                inspectBundleFoldout.SetEnabled(true);
                inspectBundleFoldout.style.opacity = 1;

                //if the foldout was already open, reopen it on refresh
                if(inspectBundleFoldoutOpen)
                {
                    inspectBundleFoldout.value = true;
                }

                UpdateInspectBundleUI(selectedABName);
            }
            else if(studioMetadata.ExperienceBundles.Keys.Contains(experienceId) && assetBundlePaths.ContainsKey(studioMetadata.ExperienceBundles[experienceId]))
            {
                dropdown.value = studioMetadata.ExperienceBundles[experienceId];
                selectedABName = studioMetadata.ExperienceBundles[experienceId];
                inspectBundleFoldout.SetEnabled(true);
                inspectBundleFoldout.style.opacity = 1;

                if(inspectBundleFoldoutOpen)
                {
                    inspectBundleFoldout.value = true;
                }

                UpdateInspectBundleUI(selectedABName);
            }
            else
            {
                inspectBundleFoldoutOpen = false;
                selectedABName = "";
            }
            dropdown.RegisterCallback<ChangeEvent<string>>((evt) =>
            {
                selectedABName = evt.newValue;
                inspectBundleFoldout.SetEnabled(true);
                inspectBundleFoldout.style.opacity = 1;

                //set the files in the selected folder
                UpdateInspectBundleUI(selectedABName);
            });

            //set bundle type dropdown
            PopupField<string> bundleTypeDropdown = root.Query<PopupField<string>>("bundle-type-dropdown");
            bundleTypeDropdown.choices = new List<string> { "Experience", "MapMarker" };
            bundleTypeDropdown.value = "Experience";

            bundleTypeDropdown.RegisterCallback<ChangeEvent<string>>((evt) =>
            {
                bundleType = evt.newValue;

                if(bundleType == "Experience" && studioMetadata.ExperienceBundles.Keys.Contains(experienceId) && assetBundlePaths.ContainsKey(studioMetadata.ExperienceBundles[experienceId]))
                {
                    selectedABName = studioMetadata.ExperienceBundles[experienceId];
                    dropdown.value = selectedABName;
                    UpdateInspectBundleUI(selectedABName);
                }
                else if(bundleType == "MapMarker" && studioMetadata.MapMarkerBundles.Keys.Contains(experienceId) && assetBundlePaths.ContainsKey(studioMetadata.MapMarkerBundles[experienceId]))
                {
                    selectedABName = studioMetadata.MapMarkerBundles[experienceId];
                    dropdown.value = selectedABName;
                    UpdateInspectBundleUI(selectedABName);
                }
                else
                {
                    dropdown.value = "";
                    selectedABName = "";
                }
            });
            if(refresh)
            {
                bundleTypeDropdown.value = bundleType;
            }

            //set ios toggle
            Toggle iosToggle = root.Query<Toggle>("ios-platform-toggle");
            if(refresh)
            {
                iosToggle.value = iosPlatformEnabled;
            }
            else
            {
                iosPlatformEnabled = true;
            }
            iosToggle.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                iosPlatformEnabled = evt.newValue;
            });
            
            //set the android toggle
            Toggle androidToggle = root.Query<Toggle>("android-platform-toggle");
            if(refresh)
            {
                androidToggle.value = androidPlatformEnabled;
            }
            else
            {
                androidPlatformEnabled = true;
            }
            androidToggle.RegisterCallback<ChangeEvent<bool>>((evt) =>
            {
                androidPlatformEnabled = evt.newValue;
            });

            //set the platform toggles
            Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>
            {
                { "Android", androidToggle },
                { "iOS", iosToggle },
            };

            //set the upload button
            Button uploadButton = root.Query<Button>("upload-button");
            uploadButton.clicked += () =>
            {
                if (selectedABName == "")
                {
                    EditorUtility.DisplayDialog("Error", "Please select an asset bundle", "OK");
                    return;
                }

                //save the bundles to the studioMetadata
                if(bundleTypeDropdown.value == "Experience" && !studioMetadata.ExperienceBundles.Keys.Contains(experienceId))
                {
                    studioMetadata.ExperienceBundles.Add(experienceId, selectedABName);
                    WriteMeadowStudioData(studioMetadata);
                }
                else if(bundleTypeDropdown.value == "MapMarker" && !studioMetadata.MapMarkerBundles.Keys.Contains(experienceId))
                {
                    studioMetadata.MapMarkerBundles.Add(experienceId, selectedABName);
                    WriteMeadowStudioData(studioMetadata);
                }

                if(toggles["iOS"].value || toggles["Android"].value)
                {
                    if(CheckMetaGuidelines())
                    {
                        //check that the AssetBundles/iOS and AssetBundles/Android directories exist if not create them
                        string iosPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../AssetBundles/iOS"));
                        if (!Directory.Exists(iosPath))
                        {
                            Directory.CreateDirectory(iosPath);
                        }

                        string androidPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../AssetBundles/Android"));
                        if (!Directory.Exists(androidPath))
                        {
                            Directory.CreateDirectory(androidPath);
                        }
                        
                        var toggleMap = new Dictionary<Func<bool>, (string, string)>
                        {
                            { () => toggles["iOS"].value, (Path.GetFullPath(Path.Combine(Application.dataPath, "../AssetBundles/iOS")), "IOS") },
                            { () => toggles["Android"].value, (Path.GetFullPath(Path.Combine(Application.dataPath, "../AssetBundles/Android")), "Android") },
                        };

                        // Debug.Log("Experience Components Found: " + CheckForExperienceComponents(selectedABName));

                        //Checks for experience components in asset bundle
                        int experienceComponentsFound = CheckForExperienceComponents(selectedABName);

                        //ensures that experience bundles contain only one experience component
                        if(bundleTypeDropdown.value == "Experience")
                        {
                            if(experienceComponentsFound == 0)
                            {
                                if(EditorUtility.DisplayDialog("Error", "Experience component not found. Please ensure that the asset bundle contains an Experience component", "OK"))
                                {
                                    return;
                                }
                            }
                            else if(experienceComponentsFound > 1)
                            {
                                if(EditorUtility.DisplayDialog("Error", experienceComponentsFound + " Experience components found. Please ensure that the asset bundle contains only one Experience component", "OK"))
                                {
                                    return;
                                }
                            }
                        }

                        //ensures that map marker bundles do not contain experience components
                        if(bundleTypeDropdown.value == "MapMarker" && experienceComponentsFound > 0)
                        {
                            if(EditorUtility.DisplayDialog("Error", experienceComponentsFound + " Experience components found. Please ensure that the Map Marker asset bundle does not contain any Experience components", "OK"))
                            {
                                return;
                            }
                        }
                        
                        // Debug.Log("Selected AB Name: " + selectedABName);

                        foreach (var toggle in toggleMap)
                        {
                            if (toggle.Key())
                            {
                                SetABParameters(toggle.Value.Item1, toggle.Value.Item2, bundleTypeDropdown.value, selectedABName, experienceId);
                            }
                        }
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Please select a platform", "OK");
                }
            };
        }

        private bool CheckMetaGuidelines()
        {
            string metaGuidelines = pluginUtil.GetMetaGuidelines();
            if (metaGuidelines == "")
            {
                Debug.LogError("GetMetaGuidelines failed");
                return false;
            }

            string unityVersion = Application.unityVersion;
            // Debug.Log("unityVersion: " + unityVersion);
            Dictionary<string, object> dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(metaGuidelines);
            if (dictionary == null)
            {
                Debug.LogError("parse json failed");
                return false;
            }

            string text = dictionary["unityVersion"] as string;
            int compareResult;
            bool majorMinorIsEqual;
            string minCompatibleVersion;
            bool patchIsEqual;
            pluginUtil.CheckUnityVersionCompatibility(text, out majorMinorIsEqual, out patchIsEqual, out compareResult, out minCompatibleVersion);
            if (compareResult == -1)
            {
                Debug.LogError("You are not using the correct version of Unity. Minimum compatible version: " + minCompatibleVersion + "; Recommended version: " + text);
                EditorUtility.DisplayDialog("Error", "You are not using the correct version of Unity. \n Minimum compatible version: " + minCompatibleVersion + " \n Recommended version: " + text, "OK");
                return false;
            }

            if (compareResult >= 0 && !patchIsEqual)
            {
                if (!EditorUtility.DisplayDialog("Warning", "You are not using the recommended version of Unity. \n Unknown issues might occur. \n Recommended version: " + text + ". \n Do you want to continue?", "Yes", "No"))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateInspectBundleUI(string selectedABName)
        {
            //get the columns
            VisualElement filesColumn = rootVisualElement.Query<VisualElement>("files-column");
            VisualElement fileInfoColumn = rootVisualElement.Query<VisualElement>("info-column");
            fileInfoColumn.Clear();
            filesColumn.Clear();

            List<string> filesWithLabel = new List<string>();
            foreach (string path in assetBundlePaths[selectedABName])
            {
                filesWithLabel.AddRange(GetDirectoryFiles(path));
            }

            filesWithLabel.Distinct();

            //set the estimated bundle size value label
            Label bundleSizeLabel = rootVisualElement.Query<Label>("bundle-size-value-label");
            long combinedSize = 0;
            foreach (string file in filesWithLabel)
            {
                //check file is not a directory
                if(!Directory.Exists(file) && Path.GetExtension(file) != ".meta")
                {
                    FileInfo fileInfo = new FileInfo(file);
                    combinedSize += fileInfo.Length;
                }
            }
            bundleSizeLabel.text = FormatBytes(combinedSize);

            VisualTreeAsset filesCard = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(pluginUtil.GetPluginDir(true)+"/UI/Studio/files-card.uxml");
            Func<VisualElement> makeItem = () => filesCard.CloneTree();
            Action<VisualElement, int> bindItem = (e, i) => SetFilesPost(e, filesWithLabel[i]);
            ListView filesListView = new ListView(filesWithLabel, 20, makeItem, bindItem);
            filesListView.name = "files-listview";
            filesListView.fixedItemHeight = filesCard.CloneTree().layout.height;
            filesListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;

            //on selection change
            filesListView.selectionChanged += objects =>
            {
                if (objects.Count() > 0)
                {
                    var file = objects.ElementAt(0) as string;
                    fileInfoColumn.Clear();
                    Label fileInfoLabel = new Label("Path: " + file);
                    fileInfoColumn.Add(fileInfoLabel);

                    //add a label for why the file was included in the bundle
                    Label fileReasonLabel = new Label("Reason: " + (assetBundlePaths[selectedABName].Contains(file) ? "Included" : "Auto (From parent)"));
                    fileInfoColumn.Add(fileReasonLabel);
                }
            };

            filesColumn.Add(filesListView);
        }

        private List<string> GetDirectoryFiles(string path)
        {
            List<string> files = new List<string>();

            //add the directory to the list
            if(!files.Contains(path))
                files.Add(path);

            if (Directory.Exists(path))
            {
                string[] filesTmp = Directory.GetFiles(path);
                foreach (string file in filesTmp)
                {
                    if(Path.GetExtension(file) != ".meta")
                    {
                        files.Add(file);
                    }
                }

                string[] directories = Directory.GetDirectories(path);
                foreach (string directory in directories)
                {
                    files.AddRange(GetDirectoryFiles(directory));
                }
            }
            return files;
        }

        private void SetFilesPost(VisualElement post, string file)
        {
            Label fileNameLabel = post.Query<Label>("file-name-label");
            fileNameLabel.text = Path.GetFileName(file);
            Label fileSizeLabel = post.Query<Label>("file-size-label");
            if (!Directory.Exists(file))
            {
                FileInfo fileInfo = new FileInfo(file);
                fileSizeLabel.text = FormatBytes(fileInfo.Length);
            }
            else
            {
                fileSizeLabel.text = "Size";
            }
        }

        private void SetABParameters(string outputDirectory, string buildTarget, string dropdown, string selectedABName, string experienceId)
        {
            BundleInfo buildInfo = new BundleInfo
            {
                outputDirectory = outputDirectory,
                options = BuildAssetBundleOptions.None,
                buildType = "artwork",
                bundleType = dropdown == "MapMarker" ? "Metabundle" : "",
                buildTarget = buildTarget switch
                {
                    "Android" => BuildTarget.Android,
                    "IOS" => BuildTarget.iOS,
                    _ => BuildTarget.NoTarget
                }
            };

            if (pluginUtil.BuildAssetBundles(buildInfo, selectedABName))
            {
                var bundlePath = Path.Combine(buildInfo.outputDirectory, selectedABName.ToLower());
    
                Debug.Log("updating file name");
                Debug.Log("File Exists: " + File.Exists(bundlePath));
                //if dropdown is experience rename the file at the bundle path adding ".meb" to the name
                if(dropdown == "Experience" && File.Exists(bundlePath))
                {
                    if(File.Exists(bundlePath + ".meb"))
                    {
                        File.Delete(bundlePath + ".meb");
                    }
                    File.Move(bundlePath, bundlePath + ".meb");
                    Debug.Log("Bundle Path: " + bundlePath);
                }

                //if dropdown is MapMarker rename the file at the bundle path adding ".mmb" to the name
                if(dropdown == "MapMarker" && File.Exists(bundlePath))
                {
                    if(File.Exists(bundlePath + ".mmb"))
                    {
                        File.Delete(bundlePath + ".mmb");
                    }
                    File.Move(bundlePath, bundlePath + ".mmb");
                    Debug.Log("Bundle Path: " + bundlePath);
                }

                string extension = dropdown == "Experience" ? ".meb" : ".mmb";

                UploadAssetBundle(bundlePath + extension, experienceId, buildInfo, buildTarget, selectedABName, user);

            }
            else
            {
                Debug.Log("Failed to create asset bundle for " + buildInfo.buildTarget);
            }
        }

        private int CheckForExperienceComponents(string selectedABName)
        {
            int experienceComponentsFound = 0;
            List<string> filesWithLabel = new List<string>();
            foreach (string path in assetBundlePaths[selectedABName])
            {
                filesWithLabel.AddRange(GetDirectoryFiles(path));
            }
            filesWithLabel = filesWithLabel.Distinct().ToList();

            // Get the Experience type using reflection
            Type experienceType = Type.GetType("XREF.Experience, XREF.Core");

            if (experienceType == null)
            {
                // Debug.LogWarning("Experience type not found. Make sure the package is installed or the type name is correct.");
                return 0;
            }

            foreach (string file in filesWithLabel)
            {
                if (Path.GetExtension(file) == ".prefab")
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                    if (prefab != null)
                    {
                        // experienceComponentsFound += CountExperienceComponentsInChildren(prefab.transform, experienceType);
                        MethodInfo getComponentsInChildrenMethod = typeof(GameObject).GetMethod("GetComponentsInChildren", new Type[] { typeof(Type), typeof(bool) });
                        if (getComponentsInChildrenMethod != null)
                        {
                            // Call GetComponentsInChildren on the prefab for the Experience type
                            object[] components = (object[])getComponentsInChildrenMethod.Invoke(prefab, new object[] { experienceType, true });
                            experienceComponentsFound += components.Length;
                        }
                    }
                }
            }

            return experienceComponentsFound;
        }

        private int CountExperienceComponentsInChildren(Transform transform, Type experienceType)
        {
            int count = 0;
            
            // Check if the current GameObject has the Experience component
            if (transform.GetComponent(experienceType) != null)
            {
                count++;
            }

            // Recursively check all children
            foreach (Transform child in transform)
            {
                count += CountExperienceComponentsInChildren(child, experienceType);
            }

            return count;
        }

        private async void UploadAssetBundle(string bundlePath, string experienceId, BundleInfo buildInfo, string buildTarget, string selectedABName, User user)
        {
            EditorUtility.DisplayProgressBar("Uploading Asset Bundle", "Uploading...", 0.3f);

            var resp = await bundleService.GetUploadUrl(user, experienceId, buildInfo.buildType, buildTarget, buildInfo.bundleType);

            if (resp.data == null || !resp.success)
            {

                if (resp.code == 401)
                {
                    // Debug.Log("Session expired. Refreshing ");

                    var authResp = await authService.RefreshToken();

                    if (authResp.success)
                    {
                        User u = authResp.data as User;
                        // Debug.Log("Token refreshed");
                        UploadAssetBundle(bundlePath, experienceId, buildInfo, buildTarget, selectedABName, u);
                    }
                    else
                    {
                        Debug.LogError("Error refreshing token: " + authResp.message);
                        EditorUtility.DisplayDialog("Session Expired", "Please sign in again.", "OK");
                        SignOut();
                    }

                }
                else
                {

                    Debug.LogError("Error getting upload URL: " + resp.code + " -  " + resp.message);
                    EditorUtility.DisplayDialog("Error", "Error when uploading asset bundle", "OK");
                }
                EditorUtility.ClearProgressBar();
                return;
            }
            JObject json = resp.data as JObject;
            string uploadUrl = json["url"].ToString();
            string guid = json["GUID"].ToString();

            EditorUtility.DisplayProgressBar("Uploading Asset Bundle", "Uploading...", 0.6f);
            var uploadResp = await bundleService.UploadFile(uploadUrl, bundlePath);

            if (uploadResp.success)
            {
                EditorUtility.DisplayProgressBar("Uploading Asset Bundle", "Uploading...", 0.9f);
                var deleteResp = await bundleService.DeleteExistingAssetBundleAndUpdateDB(guid, experienceId, buildInfo.buildType, buildTarget, buildInfo.bundleType);

                if (deleteResp.success)
                {
                    // Debug.Log("Old file deleted from storage and DB updated. " + buildTarget);
                    Debug.Log("<color=#559859><b>Meadow: </b></color>" + buildTarget + " Bundle uploaded successfully\n\n");
                    EditorUtility.DisplayDialog("Success", buildTarget + " asset bundle uploaded successfully", "OK");
                    EditorUtility.ClearProgressBar();

                }
                else
                {
                    Debug.LogError("Error deleting old file: " + deleteResp.message);
                    EditorUtility.DisplayDialog("Error", "Error when uploading asset bundle", "OK");
                    EditorUtility.ClearProgressBar();
                }
            }
            else
            {
                Debug.LogError("Error uploading file: " + uploadResp.message);
                EditorUtility.DisplayDialog("Error", "Error when uploading asset bundle", "OK");
                EditorUtility.ClearProgressBar();
            }
        }

        public static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes = bytes / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
            // show a single decimal place, and "{0}{1}" would show whole numbers only.
            return String.Format("{0:0.##} {1}", bytes, sizes[order]);
        }

        private static async void CheckForUpdates(bool openPrompt = false){
            var resp = await updateService.CheckForUpdates();

            if (resp.success)
            {
                await RunOnMainThread(() =>
                {
                    if(resp.message == "no-update")
                    {
                        // no update available
                        if(studioMetadata.UpdateAvailable == true)
                        {
                            studioMetadata.UpdateAvailable = false;
                            studioMetadata.UpdateVersion = "";
                            WriteMeadowStudioData(studioMetadata);
                        }
                    }
                    if(resp.message == "update-available")
                    {
                        string updateUrl = resp.data.ToString();
                        if(studioMetadata.UpdateAvailable == false || openPrompt)
                        {
                            studioMetadata.UpdateAvailable = true;
                            studioMetadata.UpdateVersion = resp.version;
                            WriteMeadowStudioData(studioMetadata);
                            if(EditorUtility.DisplayDialog("Update Available", "A new version of Meadow Studio is available. Do you want to download it?", "Yes", "No"))
                            {
                                Application.OpenURL(updateUrl);
                            }
                        }
                    }
                });
            }
            else
            {
                Debug.LogError("Error checking for updates: " + resp.message);
            }
        }

        private static Task RunOnMainThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            void EditorUpdate()
            {
                EditorApplication.update -= EditorUpdate; // Unsubscribe
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                }
            }

            EditorApplication.update += EditorUpdate; // Subscribe
            return tcs.Task;
        }

        [System.Serializable]
        public class MeadowStudioData
        {
            //studio metadata
            public bool UpdateAvailable = false;
            public string UpdateVersion = "";
            public float LastUpdateCheckTime = 0;

            public Dictionary<string, string> ExperienceBundles = new Dictionary<string, string>();
            public Dictionary<string, string> MapMarkerBundles = new Dictionary<string, string>();
        }

        public static MeadowStudioData LoadMeadowStudioData()
        {   
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-studio.data");

            // Check if the file exists
            if(File.Exists(path))
            {
                // Create a binary formatter
                BinaryFormatter formatter = new BinaryFormatter();

                // Create a file stream
                using (FileStream stream = new FileStream(path, FileMode.Open))
                {
                    // Deserialize the User object and return it
                    MeadowStudioData data = formatter.Deserialize(stream) as MeadowStudioData;
                    return data;
                }
            }
            else
            {
                // Create a new MeadowSetupData object and return it
                MeadowStudioData data = new MeadowStudioData();
                return data;
            }
        }

        public static void WriteMeadowStudioData(MeadowStudioData data)
        {
            // Path to the file
            string path = Path.Combine(Application.persistentDataPath, "meadow-studio.data");

            // Create a binary formatter
            BinaryFormatter formatter = new BinaryFormatter();

            // Create a file stream
            using (FileStream stream = new FileStream(path, FileMode.Create))
            {
                // Serialize the User object and write it to the file
                formatter.Serialize(stream, data);
            }
        }

        private static void InitializeStudioMetadata()
        {
            if(studioMetadata != null)
                return;
            
            studioMetadata = LoadMeadowStudioData();
            
            //if its still null create a new setup metadata file
            if(studioMetadata == null)
            {
                //create a new setup metadata file
                studioMetadata = new MeadowStudioData();
                WriteMeadowStudioData(studioMetadata);
            }
        }

        // Upload asset bundle using the Signed URL
        void OnDestroy()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}
