using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Xml.Linq;
using Mirror.Weaver;
using Mono.CecilX;
using Mono.CecilX.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
            string[] references = ResolveReferences(root);
            var paths = references.GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            AssemblyLoadContext.Default.Resolving += (_, name) => paths.TryGetValue(name.Name, out string path)
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            return Check(root, references);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static string[] ResolveReferences(string root)
    {
        var references = new List<string>();
        // Prefer freshly built project assemblies to any older Library artifacts.
        references.AddRange(Directory.GetFiles(Path.Combine(root, "Temp/bin/Debug"), "*.dll"));
        foreach (string name in new[] { "Assembly-CSharp.csproj", "Unity.Mirror.CodeGen.csproj" })
        {
            XDocument project = XDocument.Load(Path.Combine(root, name));
            foreach (XElement hint in project.Descendants("HintPath"))
            {
                string path = Path.GetFullPath(hint.Value, root);
                if (File.Exists(path)) references.Add(path);
            }
        }
        return references.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static int Check(string root, string[] references)
    {
        string path = Path.Combine(root, "Temp/bin/Debug/Assembly-CSharp.dll");
        var input = new InMemoryAssembly(File.ReadAllBytes(path), File.ReadAllBytes(Path.ChangeExtension(path, ".pdb")));
        using (AssemblyDefinition source = Read(input))
        {
            if (source.MainModule.Types.Any(type => type.FullName == "Mirror.GeneratedNetworkCode"))
                throw new InvalidOperationException("Build fresh unweaved C# output before running this check.");
        }

        var assembly = new CompiledAssembly(input, references);
        var hook = new ILPostProcessorHook();
        if (!hook.WillProcess(assembly)) throw new InvalidOperationException("Mirror skipped the target assembly.");
        ILPostProcessResult result = hook.Process(assembly);
        foreach (DiagnosticMessage diagnostic in result.Diagnostics)
            Console.WriteLine($"{diagnostic.DiagnosticType}: {diagnostic.MessageData}");
        if (result.Diagnostics.Any(diagnostic => diagnostic.DiagnosticType == DiagnosticType.Error)) return 1;
        using (AssemblyDefinition output = Read(result.InMemoryAssembly))
        {
            TypeDefinition stats = output.MainModule.GetType("BattlePvp.Stats.StatManager");
            if (!output.MainModule.Types.Any(type => type.FullName == "Mirror.GeneratedNetworkCode") ||
                !stats.Methods.Any(method => method.Name.StartsWith("InvokeUserCode_CmdUpdateStats", StringComparison.Ordinal)))
                throw new InvalidOperationException("The expected generated Command/serializer code is missing.");
        }
        Console.WriteLine("PASS: installed Mirror ILPostProcessorHook processed the entire Assembly-CSharp assembly without errors and generated CmdUpdateStats dispatch.");

        // Reproduce the reported bug in an in-memory copy. Project binaries and source files are never overwritten.
        using (AssemblyDefinition broken = Read(input))
        using (var pe = new MemoryStream())
        using (var pdb = new MemoryStream())
        {
            MethodDefinition command = broken.MainModule.GetType("BattlePvp.Stats.StatManager").Methods
                .Single(method => method.Name == "CmdUpdateStats");
            ParameterDefinition parameter = command.Parameters[1];
            parameter.IsOptional = true;
            parameter.Constant = 0u;
            broken.Write(pe, new WriterParameters { WriteSymbols = true, SymbolStream = pdb, SymbolWriterProvider = new PortablePdbWriterProvider() });
            var control = new CompiledAssembly(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), references);
            ILPostProcessResult rejected = new ILPostProcessorHook().Process(control);
            if (!rejected.Diagnostics.Any(diagnostic => diagnostic.DiagnosticType == DiagnosticType.Error &&
                    diagnostic.MessageData.Contains("CmdUpdateStats cannot have optional parameters", StringComparison.Ordinal)))
                throw new InvalidOperationException("The negative control failed to reproduce the reported optional-parameter error.");
        }
        Console.WriteLine("PASS: restoring the old optional parameter in memory reproduces the reported Weaver error.");
        Console.WriteLine("Local code-generation check only; Unity import/build, player execution, RPC transport and Windows/WebGL validation are separate.");
        return 0;
    }

    private static AssemblyDefinition Read(InMemoryAssembly assembly) => AssemblyDefinition.ReadAssembly(
        new MemoryStream(assembly.PeData), new ReaderParameters
        {
            ReadSymbols = true, SymbolStream = new MemoryStream(assembly.PdbData)
        });

    private sealed class CompiledAssembly : ICompiledAssembly
    {
        public CompiledAssembly(InMemoryAssembly assembly, string[] references)
        { InMemoryAssembly = assembly; References = references; }
        public string Name => "Assembly-CSharp";
        public string[] References { get; }
        public string[] Defines => Array.Empty<string>();
        public InMemoryAssembly InMemoryAssembly { get; }
    }
}
