using System;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
public class ContractConverter
{
    public static void ProcessFile(string filePath)
    {
        string code = File.ReadAllText(filePath);

        // Parse syntax tree
        var syntaxTree = CSharpSyntaxTree.ParseText(code);

        // Compilation unit
        var compilation = CSharpCompilation.Create("TempAssembly")
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            )
            .AddSyntaxTrees(syntaxTree);

        // Emit in memory
        using (var ms = new MemoryStream())
        {
            var result = compilation.Emit(ms);

            if (!result.Success)
            {
                Console.WriteLine("❌ Compilation failed for: " + Path.GetFileName(filePath));
                foreach (var diag in result.Diagnostics)
                {
                    Console.WriteLine(diag.ToString());
                }
                return;
            }

            ms.Seek(0, SeekOrigin.Begin);

            var assembly = Assembly.Load(ms.ToArray());
            var types = assembly.GetTypes();

            // Expecting one interface and one class
            var interfaceType = types.FirstOrDefault(t => t.IsInterface);
            var contractType = types.FirstOrDefault(t => t.IsClass);

            if (interfaceType != null && contractType != null)
            {
                ConvertToSolidityAndABI(contractType, interfaceType);
            }
            else
            {
                Console.WriteLine("⚠️ No valid interface/class found in: " + Path.GetFileName(filePath));
            }
        }
    }
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
