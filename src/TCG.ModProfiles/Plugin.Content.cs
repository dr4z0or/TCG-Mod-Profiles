using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using TCG.SaveManager.Core;
using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    public sealed partial class Plugin
    {
        [Serializable] private sealed class ContentDescriptor { public string BundleId="",Name=""; }
        private readonly Dictionary<string,bool> contentSession=new Dictionary<string,bool>(StringComparer.Ordinal);
        private readonly Dictionary<string,string> contentPaths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private IEnumerable<InstalledMod> ContentInventory()
        {
            foreach(var file in Directory.EnumerateFiles(Paths.PluginPath,"*.json",SearchOption.AllDirectories).Where(p=>p.IndexOf("_prefabloader"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)>=0))
            {
                ContentDescriptor? descriptor=null;
                try {descriptor=JsonUtility.FromJson<ContentDescriptor>(File.ReadAllText(file));}catch(Exception ex){Logger.LogWarning(F("Pack illisible : {0} / {1}",file,ex.Message));}
                if(descriptor==null || string.IsNullOrWhiteSpace(descriptor.BundleId))continue;
                var id="content:"+descriptor.BundleId;
                modNames[id]="Pack · "+(string.IsNullOrWhiteSpace(descriptor.Name)?descriptor.BundleId:descriptor.Name);
                contentPaths[Path.GetFullPath(file)]=id;
                yield return new InstalledMod{Id=id,Version="1",Sha256=LabStore.Hash(file),Assembly=file,Dependencies=new[]{new ModDependency{Id="EnhancedPrefabLoader",Required=true},new ModDependency{Id="EnhancedPrefabLoader.API",Required=true}}};
            }
        }
        private void InstallContentFilter()
        {
            var inventory=ContentInventory().GroupBy(m=>m.Id).Select(g=>g.First()).ToArray();
            ModProfile? startup=null;
            var root=Directory.GetParent(campaignsRoot)!.FullName;
            if(File.Exists(Path.Combine(root,"mod-profile-selection.xml")))startup=TCG.SaveManager.Core.ModProfiles.Load(campaignsRoot,Shared.StartupSelection.Read(root));
            foreach(var mod in inventory)
            {
                var choice=startup?.Mods.FirstOrDefault(d=>d.Id==mod.Id);
                contentSession[mod.Id]=Chainloader.PluginInfos.ContainsKey("EnhancedPrefabLoader") && !(choice?.Choice==ModChoice.Disabled && choice.ReviewedHash==mod.Sha256);
            }
            var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name=="EnhancedPrefabLoader");
            var type=assembly?.GetType("EnhancedPrefabLoader.Shared.Helpers.FileHelper");
            if(type!=null)patches!.Patch(AccessTools.Method(type,"GetAllBundleDescriptorsIn"),postfix:new HarmonyMethod(typeof(Plugin),nameof(FilterContentDescriptors)));
            Logger.LogInfo("Packs de contenu inventoriés : "+inventory.Length);
        }
        private static void FilterContentDescriptors(ref List<string> __result)
        {
            var self=integration;if(self==null)return;
            __result.RemoveAll(path=>self.contentPaths.TryGetValue(Path.GetFullPath(path),out var id) && self.contentSession.TryGetValue(id,out var active) && !active);
        }
        private bool SessionEnabled(string id)=>id.StartsWith("content:",StringComparison.Ordinal)?contentSession.TryGetValue(id,out var enabled) && enabled:Chainloader.PluginInfos.ContainsKey(id);
    }
}
