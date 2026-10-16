using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Extensions.Logging;

namespace DVModManager.Helpers;

/// <summary>
/// Applies/removes UMM's Assembly-method patch: embed the starter type into the
/// game's entry assembly and insert a call at the configured entry point
/// (same semantics as UMM's dnlib-based InjectAssembly, including its
/// <c>.original_</c> backup naming so UMM and DVMM stay mutually compatible).
/// </summary>
public class AssemblyPatcher
{
    /// <summary>UMM's backup suffix for the pristine entry assembly.</summary>
    public const string OriginalSuffix = ".original_";

    // [Assembly.dll]Class.Method:Before|After
    private static readonly Regex EntryPointRegex = new(
        @"^\[(?<assembly>[^\]]+)\](?<class>[\w.]+)\.(?<method>\w+)(?::(?<mode>\w+))?$",
        RegexOptions.Compiled);

    private readonly ILogger<AssemblyPatcher> _logger;

    public AssemblyPatcher(ILogger<AssemblyPatcher> logger) => _logger = logger;

    /// <summary>Parses UMM entry-point specs like
    /// <c>[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After</c>.</summary>
    public static bool TryParseEntryPoint(string? spec, out string assembly, out string className,
        out string methodName, out bool insertBefore)
    {
        assembly = className = methodName = "";
        insertBefore = false;
        if (string.IsNullOrWhiteSpace(spec)) return false;

        var m = EntryPointRegex.Match(spec.Trim());
        if (!m.Success) return false;

        assembly = m.Groups["assembly"].Value;
        className = m.Groups["class"].Value;
        methodName = m.Groups["method"].Value;
        insertBefore = string.Equals(m.Groups["mode"].Value, "before", StringComparison.OrdinalIgnoreCase);
        return assembly.Length > 0 && className.Length > 0 && methodName.Length > 0;
    }

    /// <summary>True when the entry assembly carries UMM's injected starter type.</summary>
    public bool IsPatched(string assemblyPath)
    {
        if (!File.Exists(assemblyPath)) return false;
        try
        {
            using var module = LoadModule(assemblyPath);
            return FindStarter(module) != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not inspect {Path}", assemblyPath);
            return false;
        }
    }

    /// <summary>
    /// Injects the starter and the call at the entry point. Idempotent: a
    /// patched assembly is left untouched and reported as success.
    /// </summary>
    public bool Inject(string assemblyPath, string? entryPointSpec)
    {
        if (!File.Exists(assemblyPath))
        {
            _logger.LogError("Entry assembly {Path} not found", assemblyPath);
            return false;
        }
        if (!TryParseEntryPoint(entryPointSpec, out _, out var className, out var methodName, out var insertBefore))
        {
            _logger.LogError("Invalid UMM entry point spec '{Spec}'", entryPointSpec);
            return false;
        }

        try
        {
            using var module = LoadModule(assemblyPath);

            var existing = FindStarter(module);
            if (existing != null)
            {
                if (!ReferencesForeignAssemblies(module, existing))
                {
                    _logger.LogInformation("{Path} is already patched", assemblyPath);
                    return true;
                }

                // Older DVMM builds emitted a starter that referenced
                // System.Private.CoreLib, which breaks the game under Mono.
                _logger.LogWarning("{Path} carries a broken starter — re-patching", assemblyPath);
                StripStarter(module, existing, className, methodName);
            }

            if (!TryFindMethod(module, className, methodName, out var target))
            {
                _logger.LogError("Entry point {Class}.{Method} not found in {Path}",
                    className, methodName, assemblyPath);
                return false;
            }

            // One pristine backup for the lifetime of the installation (UMM
            // keeps its .original_ the same way).
            var originalPath = assemblyPath + OriginalSuffix;
            if (!File.Exists(originalPath))
                File.Copy(assemblyPath, originalPath);

            var starter = UmmStarterEmitter.CreateStarterType(module);
            module.Types.Add(starter);
            var start = starter.Methods.First(m => m.Name == UmmStarterEmitter.MethodName);

            var call = Instruction.Create(OpCodes.Call, start);
            if (insertBefore)
                target.Body.Instructions.Insert(0, call);
            else
                target.Body.Instructions.Insert(target.Body.Instructions.Count - 1, call);

            module.Write(assemblyPath);
            _logger.LogInformation("Patched {Path} at {Class}.{Method}:{Mode}",
                assemblyPath, className, methodName, insertBefore ? "Before" : "After");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to patch {Path}", assemblyPath);
            return false;
        }
    }

    /// <summary>
    /// Removes the injected call and starter type. Falls back to restoring the
    /// <c>.original_</c> backup when the patch cannot be written back (e.g. the
    /// game updated and rewrote the assembly). Idempotent when nothing is patched.
    /// </summary>
    public bool Remove(string assemblyPath, string? entryPointSpec)
    {
        if (!File.Exists(assemblyPath))
        {
            _logger.LogInformation("Entry assembly {Path} not found — nothing to unpatch", assemblyPath);
            return true;
        }

        try
        {
            using var module = LoadModule(assemblyPath);
            var starter = FindStarter(module);

            if (starter == null)
            {
                // Not patched: clean up a stale backup if one exists.
                DeleteOriginal(assemblyPath);
                return true;
            }

            TryParseEntryPoint(entryPointSpec, out _, out var className, out var methodName, out _);
            StripStarter(module, starter, className, methodName);
            module.Write(assemblyPath);
            DeleteOriginal(assemblyPath);
            _logger.LogInformation("Unpatched {Path}", assemblyPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unpatch of {Path} failed — trying .original_ restore", assemblyPath);
            return RestoreOriginal(assemblyPath);
        }
    }

    /// <summary>Restores the pristine assembly from the <c>.original_</c> backup.</summary>
    public bool RestoreOriginal(string assemblyPath)
    {
        var originalPath = assemblyPath + OriginalSuffix;
        if (!File.Exists(originalPath))
        {
            _logger.LogError("No backup {Backup} to restore", originalPath);
            return false;
        }
        try
        {
            File.Copy(originalPath, assemblyPath, overwrite: true);
            DeleteOriginal(assemblyPath);
            _logger.LogInformation("Restored {Path} from backup", assemblyPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore {Path} from backup", assemblyPath);
            return false;
        }
    }

    private static void DeleteOriginal(string assemblyPath)
    {
        var originalPath = assemblyPath + OriginalSuffix;
        if (File.Exists(originalPath))
        {
            try { File.Delete(originalPath); } catch { /* best-effort */ }
        }
    }

    /// <summary>Removes the starter type and its call at the entry point (in memory only).</summary>
    private static void StripStarter(ModuleDef module, TypeDef starter, string className, string methodName)
    {
        if (className.Length > 0 && TryFindMethod(module, className, methodName, out var target))
        {
            var call = target.Body.Instructions.FirstOrDefault(i =>
                i.OpCode == OpCodes.Call && i.Operand is IMethod m && m.Name == UmmStarterEmitter.MethodName);
            if (call != null) target.Body.Instructions.Remove(call);
        }
        module.Types.Remove(starter);
    }

    /// <summary>
    /// True when the starter's IL points at any assembly other than the
    /// module's own corlib (e.g. System.Private.CoreLib from DVMM's runtime).
    /// </summary>
    private static bool ReferencesForeignAssemblies(ModuleDef module, TypeDef starter)
    {
        var corlib = module.CorLibTypes.AssemblyRef.Name.String;
        return starter.Methods
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions.Select(i => i.Operand switch
                {
                    IMethod method => method.DeclaringType,
                    ITypeDefOrRef type => type,
                    _ => null
                })
                .Concat(m.Body.ExceptionHandlers.Select(h => h.CatchType)))
            .Any(t => t != null && t.DefinitionAssembly?.Name.String != corlib);
    }

    /// <summary>Loads from bytes so the file is never held open during Write.</summary>
    private static ModuleDefMD LoadModule(string assemblyPath) =>
        ModuleDefMD.Load(File.ReadAllBytes(assemblyPath));

    private static TypeDef? FindStarter(ModuleDef module) =>
        module.Types.FirstOrDefault(t =>
            t.Name == UmmStarterEmitter.TypeName && t.Namespace == UmmStarterEmitter.NamespaceName);

    /// <summary>
    /// Finds the entry method; UMM configs spell static constructors "cctor"
    /// while IL calls them ".cctor", so both spellings are accepted.
    /// </summary>
    private static bool TryFindMethod(ModuleDef module, string className, string methodName, out MethodDef target)
    {
        target = null!;
        var type = module.Types.FirstOrDefault(t => t.FullName == className)
                   ?? module.Types.SelectMany(t => t.NestedTypes).FirstOrDefault(t => t.FullName == className);
        if (type == null) return false;

        var ilName = methodName switch
        {
            "cctor" => ".cctor",
            "ctor" => ".ctor",
            _ => methodName
        };

        var method = type.Methods.FirstOrDefault(m => m.Name == ilName && m.HasBody && !m.IsAbstract);
        if (method == null) return false;

        target = method;
        return true;
    }
}
