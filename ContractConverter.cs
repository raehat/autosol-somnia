// File: UniversalContractCompiler.cs
// Requires: Microsoft.CodeAnalysis.CSharp, System.Text.Json
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoSol
{
    #region Public API / Entry

    public static class ContractConverter
    {
        // Entry: process one source file and emit for a specific backend
        public static void ProcessFile(string sourcePath, string backendId = "solidity-0.8")
        {
            var source = File.ReadAllText(sourcePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(source);
            var refs = DefaultReferences();
            var compilation = CSharpCompilation.Create("Temp", new[] { syntaxTree }, refs);
            var model = compilation.GetSemanticModel(syntaxTree);

            // find candidate interface + class pairs
            var root = syntaxTree.GetRoot();
            var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

            // naive pair strategy: for each interface find the implementing class with same stem name or that implements it
            var contracts = new List<ContractIR>();
            foreach (var ifaceDecl in interfaces)
            {
                var ifaceSym = model.GetDeclaredSymbol(ifaceDecl) as INamedTypeSymbol;
                if (ifaceSym == null) continue;

                // find class that implements iface or has same name minus 'I'
                INamedTypeSymbol impl = null;
                foreach (var cDecl in classes)
                {
                    var cSym = model.GetDeclaredSymbol(cDecl) as INamedTypeSymbol;
                    if (cSym == null) continue;
                    if (cSym.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, ifaceSym)))
                    {
                        impl = cSym;
                        break;
                    }
                    // fallback name matching: IExample -> Example
                    var fallback = ifaceSym.Name.StartsWith("I") ? ifaceSym.Name.Substring(1) : null;
                    if (!string.IsNullOrEmpty(fallback) && cSym.Name.Equals(fallback, StringComparison.OrdinalIgnoreCase))
                    {
                        impl = cSym;
                        break;
                    }
                }

                if (impl == null) continue;

                // Build IR
                var ir = IRBuilder.BuildFromSymbols(ifaceSym, impl, model);
                contracts.Add(ir);
            }

            if (contracts.Count == 0)
            {
                Console.WriteLine("No contract pairs found in file: " + Path.GetFileName(sourcePath));
                return;
            }

            // get requested backend
            var backend = BackendRegistry.Get(backendId);
            if (backend == null)
            {
                Console.WriteLine($"Backend '{backendId}' not registered.");
                return;
            }

            // For each contract, generate outputs
            foreach (var c in contracts)
            {
                var result = backend.Generate(c);
                var outdir = Path.Combine(Directory.GetCurrentDirectory(), "build", backendId);
                Directory.CreateDirectory(outdir);
                File.WriteAllText(Path.Combine(outdir, result.FileName + backend.FileExtension), result.SourceCode, Encoding.UTF8);
                if (!string.IsNullOrEmpty(result.AbiJson))
                    File.WriteAllText(Path.Combine(outdir, result.FileName + ".abi.json"), result.AbiJson, Encoding.UTF8);

                var manifest = new { c.CSharpInterface, c.CSharpClass, backend = backendId, generated = DateTime.UtcNow, files = new[] { result.FileName + backend.FileExtension } };
                File.WriteAllText(Path.Combine(outdir, result.FileName + ".manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"Generated {result.FileName}{backend.FileExtension} (ABI: {(string.IsNullOrEmpty(result.AbiJson) ? "no" : "yes")})");
            }
        }

        // Basic references for Roslyn compilation; extend as needed
        private static MetadataReference[] DefaultReferences()
        {
            var list = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            };
            return list.ToArray();
        }
    }

    #endregion

    #region IR Builder

    // IR models
    public class ContractIR
    {
        public string CSharpInterface { get; set; }
        public string CSharpClass { get; set; }
        public string Name { get; set; }            // suggested solidity name
        public List<FunctionIR> Functions { get; } = new List<FunctionIR>();
        public List<StructIR> Structs { get; } = new List<StructIR>();
        public List<EventIR> Events { get; } = new List<EventIR>();
    }

    public class FunctionIR
    {
        public string Name { get; set; }
        public string ReturnCSharpType { get; set; }
        public string ReturnSolType { get; set; }
        public List<ParamIR> Params { get; } = new List<ParamIR>();
        public bool IsView { get; set; }
    }

    public class ParamIR { public string Name; public string CSharpType; public string SolType; }
    public class StructIR { public string Name; public List<ParamIR> Fields = new List<ParamIR>(); }
    public class EventIR { public string Name; public List<ParamIR> Params = new List<ParamIR>(); }

    static class IRBuilder
    {
        public static ContractIR BuildFromSymbols(INamedTypeSymbol iface, INamedTypeSymbol impl, SemanticModel model)
        {
            var ir = new ContractIR
            {
                CSharpInterface = iface.ToDisplayString(),
                CSharpClass = impl.ToDisplayString(),
                Name = MakeSafeName(impl.Name)
            };

            // methods from interface
            foreach (var m in iface.GetMembers().OfType<IMethodSymbol>().Where(x => x.MethodKind == MethodKind.Ordinary))
            {
                var f = new FunctionIR { Name = m.Name, ReturnCSharpType = m.ReturnType.ToDisplayString() };
                f.IsView = m.ReturnsVoid == false && m.Parameters.Length == 0; // heuristic
                foreach (var p in m.Parameters)
                {
                    var param = new ParamIR { Name = p.Name, CSharpType = p.Type.ToDisplayString() };
                    param.SolType = TypeMapper.MapToSolidity(p.Type);
                    f.Params.Add(param);
                }
                f.ReturnSolType = TypeMapper.MapToSolidity(m.ReturnType);
                ir.Functions.Add(f);
            }

            // naive struct detection: find struct declarations in same syntax trees
            foreach (var declRef in iface.DeclaringSyntaxReferences.Concat(impl.DeclaringSyntaxReferences))
            {
                var node = declRef.GetSyntax();
                var root = node.SyntaxTree.GetRoot();
                var structs = root.DescendantNodes().OfType<StructDeclarationSyntax>();
                foreach (var sd in structs)
                {
                    var sIR = new StructIR { Name = sd.Identifier.Text };
                    foreach (var field in sd.Members.OfType<FieldDeclarationSyntax>())
                    {
                        var t = field.Declaration.Type.ToString();
                        foreach (var v in field.Declaration.Variables)
                        {
                            sIR.Fields.Add(new ParamIR { Name = v.Identifier.Text, CSharpType = t, SolType = TypeMapper.MapToSolidityText(t) });
                        }
                    }
                    ir.Structs.Add(sIR);
                }
            }

            // auto-event heuristic
            foreach (var f in ir.Functions.Where(x => x.Name.ToLower().Contains("score") || x.Name.ToLower().StartsWith("set")))
            {
                ir.Events.Add(new EventIR { Name = f.Name + "Event", Params = { new ParamIR { Name = "player", CSharpType = "address", SolType = "address" } } });
            }

            return ir;
        }

        private static string MakeSafeName(string s)
        {
            var cleaned = new string(s.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(cleaned)) return "Contract";
            if (char.IsDigit(cleaned[0])) cleaned = "_" + cleaned;
            return cleaned;
        }
    }

    #endregion

    #region Type Mapping (pluggable)

    // Registry for type mappers per backend
    public static class TypeMapper
    {
        // default: solidity-0.8
        private static readonly Dictionary<string, Func<ITypeSymbol, string>> perBackend = new Dictionary<string, Func<ITypeSymbol, string>>()
        {
            ["solidity-0.8"] = (sym) =>
            {
                if (sym == null) return "bytes";
                if (sym.SpecialType == SpecialType.System_Int32 || sym.SpecialType == SpecialType.System_Int64 || sym.Name == "Int32" || sym.Name == "Int64") return "uint256";
                if (sym.SpecialType == SpecialType.System_Boolean || sym.Name == "Boolean") return "bool";
                if (sym.SpecialType == SpecialType.System_String || sym.Name == "String") return "string";
                if (sym is IArrayTypeSymbol arr) return TypeMapper.MapToSolidity(arr.ElementType) + "[]";
                if (sym.TypeKind == TypeKind.Enum) return "uint8";
                return "bytes";
            }
        };

        // call using Roslyn ITypeSymbol
        public static string MapToSolidity(ITypeSymbol typeSymbol, string backend = "solidity-0.8")
        {
            if (typeSymbol == null) return "bytes";
            if (!perBackend.ContainsKey(backend)) backend = "solidity-0.8";
            return perBackend[backend](typeSymbol);
        }

        // helper for simple textual mapping (used with string-based struct parsing)
        public static string MapToSolidityText(string csharpType)
        {
            if (csharpType.Contains("int")) return "uint256";
            if (csharpType.Contains("string")) return "string";
            if (csharpType.Contains("bool")) return "bool";
            if (csharpType.EndsWith("[]")) return "uint256[]";
            return "bytes";
        }
    }

    #endregion

    #region Backend + Generator Registration

    // Backend result
    public class GenerationResult { public string FileName; public string SourceCode; public string AbiJson; }

    // Backend interface
    public interface IBackend
    {
        string Id { get; }
        string FileExtension { get; } // ".sol"
        GenerationResult Generate(ContractIR contract);
    }

    // Simple Solidity 0.8 backend
    public class Solidity08Backend : IBackend
    {
        public string Id => "solidity-0.8";
        public string FileExtension => ".sol";

        public GenerationResult Generate(ContractIR c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Generated by UniversalContractCompiler -> solidity-0.8");
            sb.AppendLine("pragma solidity ^0.8.0;");
            sb.AppendLine($"contract {c.Name} {{");

            // storage placeholders
            foreach (var s in c.Structs)
            {
                sb.AppendLine($"    struct {s.Name} {{");
                foreach (var f in s.Fields) sb.AppendLine($"        {f.SolType} {f.Name};");
                sb.AppendLine("    }");
            }

            // events
            foreach (var e in c.Events)
            {
                var ps = string.Join(", ", e.Params.Select(p => $"{p.SolType} {p.Name}"));
                sb.AppendLine($"    event {e.Name}({ps});");
            }

            sb.AppendLine();

            // functions
            foreach (var fn in c.Functions)
            {
                var p = string.Join(", ", fn.Params.Select(x => $"{x.SolType} {x.Name}"));
                var retPart = fn.ReturnSolType == "void" ? "" : $" returns ({fn.ReturnSolType})";
                var mut = fn.IsView ? " view" : "";
                sb.AppendLine($"    function {fn.Name}({p}) public{mut}{retPart} {{");
                // heuristics:
                if (fn.Name.StartsWith("Set", StringComparison.OrdinalIgnoreCase) && fn.Params.Count == 1)
                {
                    var field = "_" + char.ToLower(fn.Name[3]) + fn.Name.Substring(4);
                    sb.AppendLine($"        // write to storage (placeholder)");
                    sb.AppendLine($"        // {field} = {fn.Params[0].Name};");
                }
                else if (fn.IsView && fn.ReturnSolType != "void")
                {
                    sb.AppendLine($"        // return placeholder default for {fn.ReturnSolType}");
                    sb.AppendLine($"        return {DefaultValue(fn.ReturnSolType)};");
                }
                else
                {
                    sb.AppendLine("        // TODO: implement");
                }
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            var src = sb.ToString();

            // ABI build (very simple)
            var abi = new List<object>();
            foreach (var fn in c.Functions)
            {
                var inputs = fn.Params.Select(p => new { name = p.Name, type = p.SolType }).ToArray();
                var outputs = fn.ReturnSolType == "void" ? new object[0] : new[] { new { name = "", type = fn.ReturnSolType } };
                abi.Add(new { name = fn.Name, type = "function", stateMutability = fn.IsView ? "view" : "nonpayable", inputs, outputs });
            }
            var abiJson = JsonSerializer.Serialize(abi, new JsonSerializerOptions { WriteIndented = true });

            return new GenerationResult { FileName = c.Name, SourceCode = src, AbiJson = abiJson };
        }

        private static string DefaultValue(string solType)
        {
            if (solType.StartsWith("uint") || solType.StartsWith("int")) return "0";
            if (solType == "bool") return "false";
            if (solType == "string") return "\"\"";
            return "0";
        }
    }

    // Backend registry (allow multiple backends)
    public static class BackendRegistry
    {
        static BackendRegistry()
        {
            // register default backend(s)
            Register(new Solidity08Backend());
            // more backends could be registered here
        }

        private static readonly Dictionary<string, IBackend> backends = new Dictionary<string, IBackend>();
        public static void Register(IBackend b) => backends[b.Id] = b;
        public static IBackend Get(string id) => backends.ContainsKey(id) ? backends[id] : null;
    }

    #endregion
}
