using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
namespace TCG.ModProfiles.Preloader
{
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs => new[] { "UnityEngine.CoreModule.dll" };
        public static void Patch(AssemblyDefinition assembly)
        {
            try
            {
                var cctor = assembly.MainModule.GetType("UnityEngine.Application").Methods.Single(m => m.Name == ".cctor");
                var start = cctor.Body.Instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "BepInEx.Bootstrap.Chainloader" && m.Name == "Start");
                using (var own = AssemblyDefinition.ReadAssembly(typeof(Patcher).Assembly.Location))
                {
                    var install = own.MainModule.GetType("TCG.ModProfiles.Preloader.Bootstrap").Methods.Single(m => m.Name == "Install");
                    cctor.Body.GetILProcessor().InsertBefore(start, Instruction.Create(OpCodes.Call, assembly.MainModule.ImportReference(install)));
                }
            }
            catch (Exception ex) { Console.WriteLine("Mod Profiles: start-up filter not installed, all mods load: " + ex.Message); } // 0.4.1 : never close the game.
        }
    }
}
