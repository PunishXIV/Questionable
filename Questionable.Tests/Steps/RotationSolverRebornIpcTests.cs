using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using Questionable.Controller.Steps;
using Questionable.External;
using Xunit;
using Mode = Questionable.External.RotationSolverRebornIpc.StateCommandType;

namespace Questionable.Tests.Steps;

public sealed class RotationSolverRebornIpcTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModeReader_SupportsMergedAndReferencedBasicAssembly(bool separateBasic)
    {
        var context = new AssemblyLoadContext($"RSR shape {Guid.NewGuid()}", isCollectible: true);
        try
        {
            using var scope = context.EnterContextualReflection();
            var plugin = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"RotationSolver{Guid.NewGuid():N}"),
                AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Plugin");
            ModuleBuilder stateModule = separateBasic
                ? AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotationSolver.Basic"),
                    AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Basic")
                : plugin;
            Type state = DefineModeState(stateModule);
            object provider = DefineProvider(plugin, separateBasic ? state : null);
            Assert.Same(context, AssemblyLoadContext.GetLoadContext(provider.GetType().Assembly));
            Assert.Equal(separateBasic, state.Assembly != provider.GetType().Assembly);
            if (separateBasic)
                Assert.Contains(provider.GetType().Assembly.GetReferencedAssemblies(), x => x.Name == "RotationSolver.Basic");
            Assert.Same(state, RotationSolverRebornIpc.ResolveModeType(provider.GetType().Assembly));

            foreach (Mode mode in new[] { Mode.Off, Mode.Auto, Mode.Manual, Mode.TargetOnly, Mode.Henched, Mode.AutoDuty })
            {
                Set("State", mode != Mode.Off);
                Set("IsManual", mode is Mode.Manual or Mode.Henched);
                Set("IsHenched", mode == Mode.Henched);
                Set("IsAutoDuty", mode == Mode.AutoDuty);
                Set("IsTargetOnly", mode == Mode.TargetOnly);
                Assert.Equal(mode, RotationSolverRebornIpc.ReadMode(provider));
            }
            Set("IsPvPStateEnabled", true);
            Assert.Throws<TaskException>(() => RotationSolverRebornIpc.ReadMode(provider));

            void Set(string name, bool value) => state.GetProperty(name)!.SetValue(null, value);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void ModeReader_DoesNotUseBasicFromAnotherProviderLoadContext()
    {
        var oldContext = new AssemblyLoadContext($"Old RSR {Guid.NewGuid()}", isCollectible: true);
        var newContext = new AssemblyLoadContext($"New RSR {Guid.NewGuid()}", isCollectible: true);
        try
        {
            Type oldState;
            using (oldContext.EnterContextualReflection())
                oldState = DefineModeState(AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RotationSolver.Basic"),
                    AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Basic"));
            using (newContext.EnterContextualReflection())
            {
                object provider = DefineProvider(AssemblyBuilder.DefineDynamicAssembly(new AssemblyName($"RotationSolver{Guid.NewGuid():N}"),
                    AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("Plugin"), oldState);
                Assert.DoesNotContain(newContext.Assemblies, x => x == oldState.Assembly);
                Assert.Throws<TaskException>(() => RotationSolverRebornIpc.ReadMode(provider));
            }
        }
        finally
        {
            oldContext.Unload();
            newContext.Unload();
        }
    }

    private static object DefineProvider(ModuleBuilder module, Type? reference)
    {
        TypeBuilder type = module.DefineType("RotationSolver.RotationSolverPlugin", TypeAttributes.Public);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        if (reference != null)
            type.DefineField("BasicReference", reference, FieldAttributes.Public);
        return Activator.CreateInstance(type.CreateType()!)!;
    }

    private static Type DefineModeState(ModuleBuilder module)
    {
        // Match the verified provider shape: an internal static class with public static bool properties.
        TypeBuilder type = module.DefineType("RotationSolver.Basic.DataCenter",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        foreach (string name in new[] { "State", "IsManual", "IsHenched", "IsAutoDuty", "IsTargetOnly", "IsPvPStateEnabled" })
        {
            FieldBuilder field = type.DefineField($"_{name}", typeof(bool), FieldAttributes.Private | FieldAttributes.Static);
            PropertyBuilder property = type.DefineProperty(name, PropertyAttributes.None, typeof(bool), Type.EmptyTypes);
            const MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName;
            MethodBuilder getter = type.DefineMethod($"get_{name}", attributes, typeof(bool), Type.EmptyTypes);
            ILGenerator get = getter.GetILGenerator();
            get.Emit(OpCodes.Ldsfld, field);
            get.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);
            MethodBuilder setter = type.DefineMethod($"set_{name}", attributes, typeof(void), [typeof(bool)]);
            ILGenerator set = setter.GetILGenerator();
            set.Emit(OpCodes.Ldarg_0);
            set.Emit(OpCodes.Stsfld, field);
            set.Emit(OpCodes.Ret);
            property.SetSetMethod(setter);
        }
        return type.CreateType()!;
    }
}
