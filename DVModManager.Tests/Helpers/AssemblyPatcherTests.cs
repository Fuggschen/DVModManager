using dnlib.DotNet;
using dnlib.DotNet.Emit;
using DVModManager.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DVModManager.Tests.Helpers;

public class AssemblyPatcherTests : IDisposable
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "Patcher_" + Guid.NewGuid().ToString("N"));
    private readonly AssemblyPatcher _patcher = new(NullLogger<AssemblyPatcher>.Instance);

    public AssemblyPatcherTests() => Directory.CreateDirectory(_workDir);

    public void Dispose() => TestData.DeleteDirectory(_workDir);

    [Theory]
    [InlineData("[UnityEngine.CoreModule.dll]UnityEngine.Display.cctor:After",
        "UnityEngine.CoreModule.dll", "UnityEngine.Display", "cctor", false)]
    [InlineData("[Assembly-CSharp.dll]DV.UI.MainMenu.Start:Before",
        "Assembly-CSharp.dll", "DV.UI.MainMenu", "Start", true)]
    [InlineData("[Dummy.dll]Dummy.Entry.Run",
        "Dummy.dll", "Dummy.Entry", "Run", false)]
    public void TryParseEntryPoint_ParsesUmmSpecs(string spec, string assembly, string cls,
        string method, bool before)
    {
        Assert.True(AssemblyPatcher.TryParseEntryPoint(spec, out var a, out var c, out var m, out var b));
        Assert.Equal(assembly, a);
        Assert.Equal(cls, c);
        Assert.Equal(method, m);
        Assert.Equal(before, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no brackets Dummy.Entry.Run")]
    [InlineData("[Dummy.dll]NoMethod")]
    public void TryParseEntryPoint_RejectsGarbage(string? spec)
    {
        Assert.False(AssemblyPatcher.TryParseEntryPoint(spec, out _, out _, out _, out _));
    }

    [Fact]
    public void Inject_AddsStarterTypeAndCall_AndCreatesBackup()
    {
        var asm = TestData.CreateDummyEntryAssembly(_workDir);

        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.Run:After"));
        Assert.True(_patcher.IsPatched(asm));
        Assert.True(File.Exists(asm + AssemblyPatcher.OriginalSuffix));

        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        var starter = module.Types.Single(t => t.Name == UmmStarterEmitter.TypeName);
        Assert.Equal(UmmStarterEmitter.NamespaceName, starter.Namespace);

        var entry = module.Types.Single(t => t.FullName == "Dummy.Entry");
        var run = entry.Methods.Single(m => m.Name == "Run");
        // :After → the call sits right before the final ret.
        var last = run.Body.Instructions[^1];
        var call = run.Body.Instructions[^2];
        Assert.Equal(OpCodes.Ret, last.OpCode);
        Assert.Equal(OpCodes.Call, call.OpCode);
        Assert.Equal(UmmStarterEmitter.MethodName, ((IMethod)call.Operand).Name);

        // The emitted starter must carry its exception handler.
        var start = starter.Methods.Single(m => m.Name == UmmStarterEmitter.MethodName);
        Assert.Single(start.Body.ExceptionHandlers);
    }

    [Fact]
    public void Inject_Before_InsertsAtIndexZero()
    {
        var asm = TestData.CreateDummyEntryAssembly(_workDir);

        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.Run:Before"));

        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        var run = module.Types.Single(t => t.FullName == "Dummy.Entry").Methods.Single(m => m.Name == "Run");
        Assert.Equal(OpCodes.Call, run.Body.Instructions[0].OpCode);
    }

    [Fact]
    public void Inject_IsIdempotent()
    {
        var asm = TestData.CreateDummyEntryAssembly(_workDir);

        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.Run:After"));
        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.Run:After"));

        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        Assert.Single(module.Types, t => t.Name == UmmStarterEmitter.TypeName);
    }

    [Fact]
    public void Inject_MissingEntryPoint_FailsWithoutTouchingFile()
    {
        var asm = TestData.CreateDummyEntryAssembly(_workDir);
        var before = File.ReadAllBytes(asm);

        Assert.False(_patcher.Inject(asm, "[Dummy.dll]No.Such.Method:After"));

        Assert.False(_patcher.IsPatched(asm));
        Assert.Equal(before, File.ReadAllBytes(asm));
    }

    [Fact]
    public void Remove_RestoresCleanState()
    {
        var asm = TestData.CreateDummyEntryAssembly(_workDir);
        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.Run:After"));

        Assert.True(_patcher.Remove(asm, "[Dummy.dll]Dummy.Entry.Run:After"));

        Assert.False(_patcher.IsPatched(asm));
        Assert.False(File.Exists(asm + AssemblyPatcher.OriginalSuffix));
        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        Assert.DoesNotContain(module.Types, t => t.Name == UmmStarterEmitter.TypeName);
        var run = module.Types.Single(t => t.FullName == "Dummy.Entry").Methods.Single(m => m.Name == "Run");
        Assert.Single(run.Body.Instructions); // just ret again
    }

    [Fact]
    public void RestoreOriginal_BringsBackPristineAssembly()
    {
        var asm = TestData.CreateDummyEntryAssembly(_workDir);
        var pristine = File.ReadAllBytes(asm);
        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.Run:After"));

        Assert.True(_patcher.RestoreOriginal(asm));

        Assert.False(_patcher.IsPatched(asm));
        Assert.Equal(pristine, File.ReadAllBytes(asm));
    }

    [Fact]
    public void Inject_UmmStyleCctorMapping()
    {
        // UMM configs spell static constructors "cctor"; IL calls them ".cctor".
        var asm = TestData.CreateDummyEntryAssembly(_workDir);

        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.cctor:After"));

        Assert.True(_patcher.IsPatched(asm));
    }

    [Fact]
    public void Inject_MscorlibTarget_ReferencesOnlyMscorlib()
    {
        // Unity/Mono assemblies target mscorlib; the starter must not drag in
        // DVMM's own runtime (System.Private.CoreLib / System.Runtime / System.Console).
        var asm = CreateMscorlibEntryAssembly();

        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.cctor:After"));

        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        Assert.Equal(["mscorlib"], module.GetAssemblyRefs().Select(a => a.Name.String).ToArray());
    }

    [Fact]
    public void Inject_RepairsStarterThatReferencesForeignCorlib()
    {
        var asm = CreateMscorlibEntryAssembly();
        WriteBrokenStarter(asm);
        Assert.Contains(ReadAssemblyRefs(asm), n => n == "System.Private.CoreLib");

        Assert.True(_patcher.Inject(asm, "[Dummy.dll]Dummy.Entry.cctor:After"));

        Assert.Equal(["mscorlib"], ReadAssemblyRefs(asm));
        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        Assert.Single(module.Types, t => t.Name == UmmStarterEmitter.TypeName);
        var cctor = module.Types.Single(t => t.FullName == "Dummy.Entry").Methods.Single(m => m.Name == ".cctor");
        Assert.Single(cctor.Body.Instructions, i => i.OpCode == OpCodes.Call);
    }

    private string CreateMscorlibEntryAssembly()
    {
        var module = new ModuleDefUser("Dummy.dll", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        new AssemblyDefUser("Dummy", new Version(1, 0, 0, 0)).Modules.Add(module);

        var type = new TypeDefUser("Dummy", "Entry", module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.BeforeFieldInit
        };
        var cctor = new MethodDefUser(".cctor", MethodSig.CreateStatic(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Private | dnlib.DotNet.MethodAttributes.Static
            | dnlib.DotNet.MethodAttributes.HideBySig | dnlib.DotNet.MethodAttributes.SpecialName
            | dnlib.DotNet.MethodAttributes.RTSpecialName);
        cctor.Body = new CilBody();
        cctor.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        type.Methods.Add(cctor);
        module.Types.Add(type);

        var path = Path.Combine(_workDir, "Dummy.dll");
        module.Write(path);
        return path;
    }

    /// <summary>Mimics the old emitter: a starter whose IL is imported from the host runtime.</summary>
    private static void WriteBrokenStarter(string asm)
    {
        var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        var importer = new Importer(module);
        var starter = new TypeDefUser(UmmStarterEmitter.NamespaceName, UmmStarterEmitter.TypeName,
            module.CorLibTypes.Object.TypeDefOrRef);
        var start = new MethodDefUser(UmmStarterEmitter.MethodName, MethodSig.CreateStatic(module.CorLibTypes.Void),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static);
        start.Body = new CilBody();
        start.Body.Instructions.Add(OpCodes.Call.ToInstruction(importer.Import(
            typeof(System.Reflection.Assembly).GetMethod(nameof(System.Reflection.Assembly.GetExecutingAssembly))!)));
        start.Body.Instructions.Add(OpCodes.Pop.ToInstruction());
        start.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        starter.Methods.Add(start);
        module.Types.Add(starter);

        var cctor = module.Types.Single(t => t.FullName == "Dummy.Entry").Methods.Single(m => m.Name == ".cctor");
        cctor.Body.Instructions.Insert(0, OpCodes.Call.ToInstruction(start));
        module.Write(asm + ".tmp");
        module.Dispose();
        File.Move(asm + ".tmp", asm, overwrite: true);
    }

    private static string[] ReadAssemblyRefs(string asm)
    {
        using var module = ModuleDefMD.Load(File.ReadAllBytes(asm));
        return module.GetAssemblyRefs().Select(a => a.Name.String).ToArray();
    }
}
