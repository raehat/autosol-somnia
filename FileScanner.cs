using System;
using System.IO;
using UnityEngine;

namespace MyUnityLibrary
{
    public class FileScanner
    {
        // Runs automatically when Unity enters Play mode
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnPlayModeStart()
        {
            string folderPath = Application.dataPath + "/web3"; // Example folder inside Assets

            if (Directory.Exists(folderPath))
            {
                string[] files = Directory.GetFiles(folderPath, "*.cs", SearchOption.AllDirectories);

                foreach (string file in files)
                {
                    Debug.Log("Found file: " + file);

                    string content = File.ReadAllText(file);

                    // Simple check if it contains a class or interface
                    if (content.Contains("class ") || content.Contains("interface "))
                    {
                        Debug.Log("File contains class or interface: " + Path.GetFileName(file));
                        ContractConverter.ProcessFile(file);
                    }
                    else
                    {
                        Debug.Log("Skipping (no class/interface): " + Path.GetFileName(file));
                    }
                }
            }

            else
            {
                Debug.LogWarning("Folder not found: " + folderPath);
            }
        }
    }
}
