using System;

using System.IO;

using System.Linq;

using System.Collections.Generic;

using System.Reflection;

using System.Reflection.Emit;

using System.Xml;

using Mono.Cecil;

using HarmonyLib;

using BepInEx;

using BepInEx.Bootstrap;

using TCG.SaveManager.Core;



namespace TCG.ModProfiles.Preloader

{

    public static class Bootstrap

    {

        private static string root = "";

        public static void Install()

        {

            try

            {

                var config = Path.Combine(Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)!, "TCGModProfiles.root.txt");

                root = File.Exists(config) ? File.ReadAllText(config).Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TCGSM");

                if (!Path.IsPathRooted(root)) throw new IOException("An absolute root folder is required.");

                root = Path.GetFullPath(root);

                for (var d = new DirectoryInfo(root); d != null; d = d.Parent)

                    if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Folder links are refused.");

                if (!File.Exists(Path.Combine(root, ".tcg-test-instance")) && !File.Exists(Path.Combine(root, ".tcg-managed-instance")))

                    throw new IOException("Save Manager's folder is not initialised: install TCG Save Manager (with its preloader).");

                new Harmony("local.tcg.modprofiles.preloader").Patch(AccessTools.Method(typeof(Chainloader), "Start"), transpiler: new HarmonyMethod(typeof(Bootstrap), nameof(Inject)));

                Console.WriteLine("Mod Profiles: start-up filter installed.");

            }

            catch (Exception ex) { Console.WriteLine("Mod Profiles: start-up filter not installed, all mods load: " + ex.Message); } // 0.4.1 : never close the game.

        }

        private static IEnumerable<CodeInstruction> Inject(IEnumerable<CodeInstruction> input)

        {

            var output = new List<CodeInstruction>(); var count = 0;

            foreach (var instruction in input)

            {

                output.Add(instruction);

                if (instruction.operand is MethodInfo method && method.Name == "FindPluginTypes" && method.IsGenericMethod && method.GetGenericArguments().Single() == typeof(PluginInfo))

                { output.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Bootstrap), nameof(Filter)))); count++; }

            }

            if (count != 1) throw new InvalidOperationException("Unknown BepInEx Chainloader signature.");

            return output;

        }

        public static Dictionary<string, List<PluginInfo>> Filter(Dictionary<string, List<PluginInfo>> plugins)

        {

            try

            {

                var selection = Path.Combine(root, "mod-profile-selection.xml");

                if (!File.Exists(selection)) return plugins;

                var xml = new XmlDocument { XmlResolver = null }; xml.Load(selection);

                var id = xml.DocumentElement!.InnerText;

                var profile = TCG.SaveManager.Core.ModProfiles.Load(Path.Combine(root, "campaigns"), id);

                if (profile.Mods.Length == 0) { Console.WriteLine("Mod Profiles: no saved choice for this shop, all mods loaded."); return plugins; } // 0.4.1 : never chosen = every mod loaded.

                var result = new Dictionary<string, List<PluginInfo>>(plugins);

                foreach (var pair in plugins)

                {

                    var disabled = pair.Value.Where(p => profile.Mods.Any(d => d.Id == p.Metadata.GUID && d.Choice == ModChoice.Disabled)).ToArray();

                    if (disabled.Length == 0) continue;

                    if (pair.Value.Any(p => p.Metadata.GUID == "local.tcg.modprofiles" || p.Metadata.GUID == "local.tcg.savemanager.diagnostics" || p.Metadata.GUID == "local.tcg.gamemodes"))

                        throw new IOException("Inconsistent selection inside one DLL.");

                    // 0.3.0 : a disabled mod stays disabled after an update. A DLL that gained a plugin

                    // the shop has not decided yet keeps loading, so the player is asked in the menu.

                    if (disabled.Length != pair.Value.Count)

                    {

                        Console.WriteLine("Mod Profiles: new plugin to decide in " + Path.GetFileName(pair.Key) + ", DLL kept.");

                        continue;

                    }

                    result.Remove(pair.Key);

                    Console.WriteLine("Mod Profiles disabled: " + string.Join(", ", disabled.Select(p => p.Metadata.Name)));

                }

                File.WriteAllText(Path.Combine(root, "mod-profile-filter-ready.txt"), id);

                return result;

            }

            catch (Exception ex) { Console.WriteLine("Mod Profiles: choices not applied, all mods loaded: " + ex.Message); return plugins; } // 0.4.1 : never close the game here.

        }

    }

}



