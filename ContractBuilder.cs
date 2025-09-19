using System.IO;
using UnityEngine;
using System;

namespace MyUnityLibrary
{
    public class ContractBuilder
    {
        public static TInterface InitializeContract<TInterface, TClass>()
            where TInterface : class
            where TClass : TInterface, new() 
        {
            Type interfaceType = typeof(TInterface);
            Type classType = typeof(TClass);

            // Check if TClass implements TInterface
            if (!interfaceType.IsAssignableFrom(classType))
            {
                throw new InvalidOperationException($"{classType.Name} does not implement {interfaceType.Name}");
            }

            // Create instance and return as interface
            TInterface instance = (TInterface)new TClass();
            return instance;
        }
    }
}
