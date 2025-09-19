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
                string[] files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);

                foreach (string file in files)
                {
                    Debug.Log("Found file: " + file);
                }
            }
            else
            {
                Debug.LogWarning("Folder not found: " + folderPath);
            }
        }
    }
}
