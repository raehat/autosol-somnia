using System;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;

public class ContractConverter
{
    public static void ConvertToSolidityAndABI(Type contractType, Type interfaceType)
    {
        var sbSol = new StringBuilder();
        var abiList = new List<Dictionary<string, object>>();

        sbSol.AppendLine("// SPDX-License-Identifier: MIT");
        sbSol.AppendLine("pragma solidity ^0.8.0;");
        sbSol.AppendLine($"contract {contractType.Name} {{");

        // Iterate over interface methods
        foreach (var method in interfaceType.GetMethods())
        {
            string methodName = method.Name;
            string returnType = ConvertType(method.ReturnType);
            var parameters = method.GetParameters();

            // Solidity function signature
            var paramList = new List<string>();
            foreach (var p in parameters)
                paramList.Add($"{ConvertType(p.ParameterType)} {p.Name}");
            string paramStr = string.Join(", ", paramList);

            sbSol.AppendLine($"    function {methodName}({paramStr}) public {(returnType != "void" ? "view returns (" + returnType + ")" : "")} {{");
            sbSol.AppendLine("        // TODO: implement logic");
            sbSol.AppendLine("    }");

            // Build ABI entry
            var inputsList = new List<object>();
            foreach (var p in parameters)
            {
                inputsList.Add(new { name = p.Name, type = ConvertType(p.ParameterType) });
            }

            var outputsList = new List<object>();
            if (returnType != "void")
            {
                outputsList.Add(new { type = returnType, name = "" });
            }

            var abiEntry = new Dictionary<string, object>
            {
                ["name"] = methodName,
                ["type"] = "function",
                ["stateMutability"] = returnType == "void" ? "nonpayable" : "view",
                ["inputs"] = inputsList,
                ["outputs"] = outputsList
            };
            abiList.Add(abiEntry);
        }

        sbSol.AppendLine("}");

        // Ensure build directory exists
        string buildDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
        Directory.CreateDirectory(buildDir);

        // File paths
        string solFilePath = Path.Combine(buildDir, $"{contractType.Name}.sol");
        string abiFilePath = Path.Combine(buildDir, $"{contractType.Name}.abi.json");

        // Write Solidity file
        File.WriteAllText(solFilePath, sbSol.ToString());

        // Write ABI file
        string abiJson = JsonConvert.SerializeObject(abiList, Formatting.Indented);
        File.WriteAllText(abiFilePath, abiJson);

        Console.WriteLine($"✅ Solidity file written: {solFilePath}");
        Console.WriteLine($"✅ ABI file written: {abiFilePath}");
    }

    private static string ConvertType(Type type)
    {
        if (type == typeof(int) || type == typeof(uint) || type == typeof(long))
            return "uint256";
        if (type == typeof(bool))
            return "bool";
        if (type == typeof(string))
            return "string";
        return "bytes"; // fallback
    }
}
