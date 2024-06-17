// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;
// using UnityEditor;
// using System.IO;
// using System.Runtime.Serialization.Formatters.Binary;
// using UnityEngine.UIElements;

// [InitializeOnLoad]
// public class MeadowSetupWindow : EditorWindow
// {
//     [MenuItem("Meadow/Meadow Setup Wizard", false, 10)]
//     public static void MeadowSetupWizardMenubar()
//     {
//         MeadowSetupWindow wnd = GetWindow<MeadowSetupWindow>(false);
//         wnd.titleContent = new GUIContent("Meadow Setup Wizard");
//     }

//     static MeadowSetupWindow()
//     {
//         //TODO Fill out this method or perhaps this logic needs to move into a separate script
//         Debug.Log("Checking if the plugin in upto date and if the project needs to be setup...");


//         //if the plugin is not up to date show the update window

//         //if the project needs to be setup show the setup window or prompt the user to run the setup process
//     }

//     void OnEnable()
//     {
//         MeadowSetupData setupData = LoadSetupData();
//         if(setupData == null)
//         {
//             Debug.Log("No setup data found, starting setup process...");
//             CreateSetupUI();
//         }
//         else
//         {
//             if(setupData.SetupComplete)
//             {
//                 //!CHANGE THE HARDCODED VERSION NUMBER TO THE CURRENT VERSION NUMBER
//                 if(int.Parse(setupData.LastSetupVersion) < 10)
//                 {
//                     Debug.Log("Setup complete, no need to run setup again. Showing complete UI.");
//                     CreateSetupCompleteUI(false);
//                 }
//                 else
//                 {
//                     Debug.Log("Setup completed on a previous verion. Running setup again for new version.");
//                     CreateSetupUI();
//                 }
//             }
//             else
//             {
//                 Debug.Log("Setup not complete, running setup process...");
//                 CreateSetupUI();
//             }
//         }
//     }
    
//     void CreateSetupUI()
//     {
//         VisualElement root = rootVisualElement;
    
//         //create the setup UI
//         Debug.Log("Creating setup UI...");
//         var setup = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Meadow Studio/UI/Wizard/setup.uxml");
//         setup.CloneTree(root);
//     }

//     void CreateSetupCompleteUI(bool allowSetupAgain)
//     {
//         //create the setup complete UI
//         Debug.Log("Creating setup complete UI...");
//     }

//     MeadowSetupData LoadSetupData()
//     {   
//         // Path to the file
//         string path = Path.Combine(Application.persistentDataPath, "meadow-setup.data");

//         // Check if the file exists
//         if(File.Exists(path))
//         {
//             // Create a binary formatter
//             BinaryFormatter formatter = new BinaryFormatter();

//             // Create a file stream
//             using (FileStream stream = new FileStream(path, FileMode.Open))
//             {
//                 // Deserialize the User object and return it
//                 MeadowSetupData data = formatter.Deserialize(stream) as MeadowSetupData;
//                 return data;
//             }
//         }
//         else
//         {
//             // Create a new MeadowSetupData object and return it
//             MeadowSetupData data = new MeadowSetupData();
//             return null;
//         }
//     }

//     //TODO Should this be moved to a separate file?
//     [System.Serializable]
//     class MeadowSetupData
//     {
//         public bool SetupComplete = false;
//         public string LastSetupVersion = "0.0.0";
//     }
// }
