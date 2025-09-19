using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;

namespace Autosol
{
    /// <summary>
    /// ContractBuilder helps manage game contracts in Unity.
    /// It allows initialization, mock deployment, persistence, and interaction.
    /// </summary>
    public class ContractBuilder
    {
        private static string buildPath = Path.Combine(Application.dataPath, "Web3Build");
        private static string configFile = Path.Combine(buildPath, "contracts.config.json");

        /// <summary>
        /// Initializes a contract by binding an interface to its class implementation.
        /// </summary>
        public static TInterface InitializeContract<TInterface, TClass>()
            where TInterface : class
            where TClass : TInterface, new()
        {
            Type interfaceType = typeof(TInterface);
            Type classType = typeof(TClass);

            if (!interfaceType.IsAssignableFrom(classType))
            {
                throw new InvalidOperationException($"{classType.Name} does not implement {interfaceType.Name}");
            }

            TInterface instance = (TInterface)new TClass();
            Debug.Log($"✅ Initialized contract {classType.Name} implementing {interfaceType.Name}");
            return instance;
        }

        /// <summary>
        /// Deploys a contract and saves its metadata into config.
        /// In reality this would deploy to blockchain, here we simulate.
        /// </summary>
        public static string DeployContract<TInterface, TClass>()
            where TInterface : class
            where TClass : TInterface, new()
        {
            string contractName = typeof(TClass).Name;

            // Simulate address
            string address = "0x" + Guid.NewGuid().ToString("N").Substring(0, 40);

            SaveContractAddress(contractName, address);

            Debug.Log($"🚀 Deployed contract {contractName} at {address}");
            return address;
        }

        /// <summary>
        /// Gets stored address of a deployed contract.
        /// </summary>
        public static string GetContractAddress(string contractName)
        {
            var dict = LoadConfig();
            if (dict.ContainsKey(contractName))
            {
                return dict[contractName];
            }
            Debug.LogWarning($"⚠️ Contract {contractName} not found in config.");
            return null;
        }

        /// <summary>
        /// Saves address into config file.
        /// </summary>
        private static void SaveContractAddress(string contractName, string address)
        {
            var dict = LoadConfig();
            dict[contractName] = address;
            SaveConfig(dict);
        }

        /// <summary>
        /// Load config dictionary from file.
        /// </summary>
        private static Dictionary<string, string> LoadConfig()
        {
            try
            {
                if (!Directory.Exists(buildPath))
                {
                    Directory.CreateDirectory(buildPath);
                }

                if (!File.Exists(configFile))
                {
                    return new Dictionary<string, string>();
                }

                string json = File.ReadAllText(configFile);
                return JsonUtility.FromJson<SerializableDictionary>(json).ToDictionary();
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ Failed to load config: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Saves config dictionary to file.
        /// </summary>
        private static void SaveConfig(Dictionary<string, string> dict)
        {
            try
            {
                var serializable = new SerializableDictionary(dict);
                string json = JsonUtility.ToJson(serializable, true);
                File.WriteAllText(configFile, json);
                Debug.Log("💾 Saved contract config.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ Failed to save config: {ex.Message}");
            }
        }

        /// <summary>
        /// Simulates a contract function call by logging.
        /// </summary>
        public static void CallFunction(string contractName, string function, params object[] args)
        {
            string address = GetContractAddress(contractName);
            if (address == null)
            {
                Debug.LogError($"❌ Cannot call {function}, contract {contractName} not deployed.");
                return;
            }

            string argsStr = args != null ? string.Join(", ", args) : "";
            Debug.Log($"📞 Calling {function} on {contractName} ({address}) with args: {argsStr}");
        }

        /// <summary>
        /// Clears all deployed contract configs.
        /// </summary>
        public static void ClearContracts()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    File.Delete(configFile);
                    Debug.Log("🧹 Cleared contract config.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ Failed to clear contracts: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Serializable dictionary wrapper for saving config.
    /// </summary>
    [Serializable]
    public class SerializableDictionary
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public string value;
        }

        public List<Entry> entries = new List<Entry>();

        public SerializableDictionary() { }

        public SerializableDictionary(Dictionary<string, string> dict)
        {
            foreach (var kv in dict)
            {
                entries.Add(new Entry { key = kv.Key, value = kv.Value });
            }
        }

        public Dictionary<string, string> ToDictionary()
        {
            var dict = new Dictionary<string, string>();
            foreach (var e in entries)
            {
                dict[e.key] = e.value;
            }
            return dict;
        }
    }
}
