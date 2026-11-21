using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Mono.Cecil;
using TCG.SaveManager.Core;

using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    public sealed partial class Plugin
    {
        private IEnumerable<PluginInfo> InstalledPlugins()
        {
            var entries = new List<PluginInfo>();
            using (var resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(Paths.BepInExAssemblyDirectory);
                resolver.AddSearchDirectory(Paths.ManagedPath);
                foreach (var file in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        // 1.0.1 : read from the file on demand instead of loading it whole in memory
                        // (Collection Tracker's DLL is over 750 MB of embedded images). The stream is
                        // shared and closed right after, so the DLL is never locked.
                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var assembly = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred }))
                        foreach (var type in Types(assembly.MainModule.Types))
                        {
                            var info = (PluginInfo?)AccessTools.Method(typeof(Chainloader), "ToPluginInfo").Invoke(null, new object[] { type });
                            if (info == null || info.Processes.Any() && !info.Processes.Any(p => string.Equals(p.ProcessName, Paths.ProcessName, StringComparison.OrdinalIgnoreCase))) continue;
                            AccessTools.Property(typeof(PluginInfo), "Location").SetValue(info, file, null);
                            entries.Add(info);
                        }
                    }
                    catch (BadImageFormatException) { }
                }
            }
            return entries;
        }
        private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
        }
        private bool HasPendingChoices() => profileInventory.Any(m => editingProfile!.Mods.Single(d => d.Id == m.Id).Choice == ModChoice.Pending);
        private void RememberStartupProfile()
        {
            if (PreloaderPath() != null) SelectStartupProfile();
            else if (SessionDiffers()) throw new IOException(T("Installer le préchargeur Mod Profiles pour appliquer les changements."));
        }
        // Mod managers (r2modman, Thunderstore) install each package's patchers in a subfolder:
        // BepInEx/patchers/<Author-Package>/TCG.ModProfiles.Preloader.dll.
        private static string? PreloaderPath()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TCG.ModProfiles.Preloader");
            try { if (loaded != null && File.Exists(loaded.Location)) return loaded.Location; } catch (NotSupportedException) { }
            if (!Directory.Exists(Paths.PatcherPluginPath)) return null;
            return Directory.EnumerateFiles(Paths.PatcherPluginPath, "TCG.ModProfiles.Preloader.dll", SearchOption.AllDirectories).FirstOrDefault();
        }
        private bool SessionDiffers() => profileInventory.Any(m =>
            (editingProfile!.Mods.Single(d => d.Id == m.Id).Choice == ModChoice.Enabled) != SessionEnabled(m.Id));
        private void SelectStartupProfile()
        {
            var root = Directory.GetParent(campaignsRoot)!.FullName;
            var patcher = PreloaderPath() ?? throw new IOException(T("Installer le préchargeur Mod Profiles dans BepInEx/patchers pour appliquer ce choix."));
            // The preloader reads its configuration next to its own DLL.
            var config = Path.Combine(Path.GetDirectoryName(patcher)!, "TCGModProfiles.root.txt");
            if (File.Exists(config) && !string.Equals(Path.GetFullPath(File.ReadAllText(config).Trim()), root, StringComparison.OrdinalIgnoreCase))
                throw new IOException(T("Le préchargeur vise un autre espace de boutiques."));
            if (!File.Exists(config) && !string.Equals(root, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TCGSM"), StringComparison.OrdinalIgnoreCase))
                throw new IOException(T("Configurer TCGModProfiles.root.txt pour cet espace de test."));
            // 0.4.1 : a shop loaded without opening its Mods screen keeps the choices shown there
            // (the mods loaded now): they are saved, so the next start applies a real profile.
            if (TCG.SaveManager.Core.ModProfiles.Load(campaignsRoot, editingProfile!.CampaignId).Mods.Length == 0)
                TCG.SaveManager.Core.ModProfiles.Save(campaignsRoot, editingProfile!);
            TCG.ModProfiles.Shared.StartupSelection.Save(root, editingProfile!.CampaignId);
        }
    }
}
