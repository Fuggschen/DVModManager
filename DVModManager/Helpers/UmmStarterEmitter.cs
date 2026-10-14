using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using MethodAttributes = dnlib.DotNet.MethodAttributes;
using MethodImplAttributes = dnlib.DotNet.MethodImplAttributes;
using TypeAttributes = dnlib.DotNet.TypeAttributes;

namespace DVModManager.Helpers;

/// <summary>
/// Builds the <c>UnityModManagerStarter</c> type that UMM's Assembly method
/// injects into the game's entry assembly. The emitted IL mirrors UMM's own
/// <c>Console/Starter.cs</c>: reflection-load
/// <c>UnityModManager/UnityModManager.dll</c> next to the patched assembly and
/// invoke <c>UnityModManagerNet.Injector.Run(false)</c>. Because the body uses
/// only reflection calls, the type can live inside any Unity assembly without
/// adding assembly references.
/// </summary>
public static class UmmStarterEmitter
{
    public const string NamespaceName = "UnityModManagerNet.Injection";
    public const string TypeName = "UnityModManagerStarter";
    public const string MethodName = "Start";

    /// <summary>
    /// Creates the starter type as a member of <paramref name="module"/>
    /// (references are imported against that module's corlib).
    /// </summary>
    public static TypeDef CreateStarterType(ModuleDef module)
    {
        var corlib = module.CorLibTypes;
        var importer = new Importer(module);

        var type = new TypeDefUser(NamespaceName, TypeName, corlib.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.BeforeFieldInit
                         | TypeAttributes.AutoLayout | TypeAttributes.Class | TypeAttributes.AnsiClass
        };

        var start = new MethodDefUser(
            MethodName,
            MethodSig.CreateStatic(corlib.Void),
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig);
        type.Methods.Add(start);

        var body = new CilBody { InitLocals = true };
        start.Body = body;
        var tException = importer.Import(typeof(Exception));
        body.Variables.Add(new Local(tException.ToTypeSig())); // catch (Exception e)

        // void Start() {
        //   try {
        //     var dir  = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        //     var file = Path.Combine(Path.Combine(dir, "UnityModManager"), "UnityModManager.dll");
        //     var run  = Assembly.LoadFrom(file).GetType("UnityModManagerNet.Injector")
        //                   .GetMethod("Run", BindingFlags.Static | BindingFlags.Public);
        //     run.Invoke(null, new object[] { false });
        //   } catch (Exception e) { Console.WriteLine(e.ToString()); }
        // }
        var mGetExecuting = importer.Import(
            typeof(Assembly).GetMethod(nameof(Assembly.GetExecutingAssembly), Type.EmptyTypes));
        var mGetLocation = importer.Import(
            typeof(Assembly).GetProperty(nameof(Assembly.Location))!.GetGetMethod());
        var mGetDirectoryName = importer.Import(
            typeof(Path).GetMethod(nameof(Path.GetDirectoryName), new[] { typeof(string) })!);
        var mCombine = importer.Import(
            typeof(Path).GetMethod(nameof(Path.Combine), new[] { typeof(string), typeof(string) })!);
        var mLoadFrom = importer.Import(
            typeof(Assembly).GetMethod(nameof(Assembly.LoadFrom), new[] { typeof(string) })!);
        var mGetType = importer.Import(
            typeof(Assembly).GetMethod(nameof(Assembly.GetType), new[] { typeof(string) })!);
        var mGetMethod = importer.Import(
            typeof(Type).GetMethod(nameof(Type.GetMethod), new[] { typeof(string), typeof(BindingFlags) })!);
        var mInvoke = importer.Import(
            typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), new[] { typeof(object), typeof(object[]) })!);
        var mWriteLine = importer.Import(
            typeof(Console).GetMethod(nameof(Console.WriteLine), new[] { typeof(string) })!);
        var mToString = importer.Import(
            typeof(Exception).GetMethod(nameof(Exception.ToString), Type.EmptyTypes)!);

        var il = body.Instructions;

        // try {
        var tryStart = OpCodes.Call.ToInstruction(mGetExecuting);
        il.Add(tryStart);
        il.Add(OpCodes.Callvirt.ToInstruction(mGetLocation));
        il.Add(OpCodes.Call.ToInstruction(mGetDirectoryName));
        il.Add(OpCodes.Ldstr.ToInstruction("UnityModManager"));
        il.Add(OpCodes.Call.ToInstruction(mCombine));
        il.Add(OpCodes.Ldstr.ToInstruction("UnityModManager.dll"));
        il.Add(OpCodes.Call.ToInstruction(mCombine));
        il.Add(OpCodes.Call.ToInstruction(mLoadFrom));
        il.Add(OpCodes.Ldstr.ToInstruction("UnityModManagerNet.Injector"));
        il.Add(OpCodes.Callvirt.ToInstruction(mGetType));
        il.Add(OpCodes.Ldstr.ToInstruction("Run"));
        il.Add(OpCodes.Ldc_I4.ToInstruction((int)(BindingFlags.Public | BindingFlags.Static)));
        il.Add(OpCodes.Callvirt.ToInstruction(mGetMethod));
        il.Add(OpCodes.Ldnull.ToInstruction());
        il.Add(OpCodes.Ldc_I4_1.ToInstruction());
        il.Add(OpCodes.Newarr.ToInstruction(corlib.Object.TypeDefOrRef));
        il.Add(OpCodes.Dup.ToInstruction());
        il.Add(OpCodes.Ldc_I4_0.ToInstruction());
        il.Add(OpCodes.Ldc_I4_0.ToInstruction());      // boxed false
        il.Add(OpCodes.Box.ToInstruction(corlib.Boolean.TypeDefOrRef));
        il.Add(OpCodes.Stelem_Ref.ToInstruction());
        il.Add(OpCodes.Callvirt.ToInstruction(mInvoke));
        il.Add(OpCodes.Pop.ToInstruction());

        // Branch opcodes need their target up front.
        var ret = OpCodes.Ret.ToInstruction();
        var leaveTry = Instruction.Create(OpCodes.Leave, ret);
        il.Add(leaveTry);

        // } catch (Exception e) {
        var handlerStart = OpCodes.Stloc_0.ToInstruction();
        il.Add(handlerStart);
        il.Add(OpCodes.Ldloc_0.ToInstruction());
        il.Add(OpCodes.Callvirt.ToInstruction(mToString));
        il.Add(OpCodes.Call.ToInstruction(mWriteLine));
        var leaveCatch = Instruction.Create(OpCodes.Leave, ret);
        il.Add(leaveCatch);

        // }
        il.Add(ret);

        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            CatchType = tException,
            TryStart = tryStart,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = ret
        });

        return type;
    }
}
