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
}
