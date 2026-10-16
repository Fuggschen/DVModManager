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

    // BindingFlags.Public (16) | BindingFlags.Static (8)
    private const int BindingFlagsPublicStatic = 16 | 8;

    /// <summary>
    /// Creates the starter type as a member of <paramref name="module"/>
    /// (all references point at that module's corlib).
    /// </summary>
    public static TypeDef CreateStarterType(ModuleDef module)
    {
        var corlib = module.CorLibTypes;

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

        // Every reference is built by hand against the target module's own
        // corlib (mscorlib on Unity/Mono). Importing System.Type/MethodInfo via
        // dnlib's Importer would resolve them through DVMM's .NET runtime and
        // reference System.Private.CoreLib, which Mono cannot load: the entry
        // point's cctor then throws TypeInitializationException in-game.
        var tAssembly = CorlibType(module, "System.Reflection", "Assembly");
        var tPath = CorlibType(module, "System.IO", "Path");
        var tType = CorlibType(module, "System", "Type");
        var tMethodInfo = CorlibType(module, "System.Reflection", "MethodInfo");
        var tMethodBase = CorlibType(module, "System.Reflection", "MethodBase");
        var tBindingFlags = CorlibType(module, "System.Reflection", "BindingFlags");
        var tConsole = CorlibType(module, "System", "Console");
        var tException = CorlibType(module, "System", "Exception");

        var sAssembly = new ClassSig(tAssembly);
        var sString = corlib.String;
        var sObject = corlib.Object;

        var body = new CilBody { InitLocals = true };
        start.Body = body;
        body.Variables.Add(new Local(new ClassSig(tException))); // catch (Exception e)

        // void Start() {
        //   try {
        //     var dir  = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        //     var file = Path.Combine(Path.Combine(dir, "UnityModManager"), "UnityModManager.dll");
        //     var run  = Assembly.LoadFrom(file).GetType("UnityModManagerNet.Injector")
        //                   .GetMethod("Run", BindingFlags.Static | BindingFlags.Public);
        //     run.Invoke(null, new object[] { false });
        //   } catch (Exception e) { Console.WriteLine(e.ToString()); }
        // }
        var mGetExecuting = new MemberRefUser(module, "GetExecutingAssembly",
            MethodSig.CreateStatic(sAssembly), tAssembly);
        var mGetLocation = new MemberRefUser(module, "get_Location",
            MethodSig.CreateInstance(sString), tAssembly);
        var mGetDirectoryName = new MemberRefUser(module, "GetDirectoryName",
            MethodSig.CreateStatic(sString, sString), tPath);
        var mCombine = new MemberRefUser(module, "Combine",
            MethodSig.CreateStatic(sString, sString, sString), tPath);
        var mLoadFrom = new MemberRefUser(module, "LoadFrom",
            MethodSig.CreateStatic(sAssembly, sString), tAssembly);
        var mGetType = new MemberRefUser(module, "GetType",
            MethodSig.CreateInstance(new ClassSig(tType), sString), tAssembly);
        var mGetMethod = new MemberRefUser(module, "GetMethod",
            MethodSig.CreateInstance(new ClassSig(tMethodInfo), sString, new ValueTypeSig(tBindingFlags)), tType);
        var mInvoke = new MemberRefUser(module, "Invoke",
            MethodSig.CreateInstance(sObject, sObject, new SZArraySig(sObject)), tMethodBase);
        var mWriteLine = new MemberRefUser(module, "WriteLine",
            MethodSig.CreateStatic(corlib.Void, sString), tConsole);
        var mToString = new MemberRefUser(module, "ToString",
            MethodSig.CreateInstance(sString), tException);

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
        il.Add(OpCodes.Ldc_I4.ToInstruction(BindingFlagsPublicStatic));
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

    private static TypeRefUser CorlibType(ModuleDef module, string ns, string name) =>
        new(module, ns, name, module.CorLibTypes.AssemblyRef);
}
