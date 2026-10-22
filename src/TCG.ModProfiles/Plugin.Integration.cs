using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TCG.SaveManager.Core;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    public sealed partial class Plugin
    {
        private static Plugin? integration;
        private Harmony? patches;
        private object? saveManager;
        private object? loadTarget;
        private string loadId = "", loadKind = "", loadSnapshot = "";
        private bool resuming;
        private object? Read(object target, string name) => AccessTools.Field(target.GetType(), name)?.GetValue(target);

        private void InstallIntegration()
        {
            try
            {
                var type = AccessTools.TypeByName("TCG.SaveManager.Diagnostics.Plugin") ?? throw new MissingMemberException(T("Save Manager indisponible."));
                var draw = AccessTools.Method(type, "DrawCampaignScreen") ?? throw new MissingMethodException("DrawCampaignScreen");
                var choose = AccessTools.Method(type, "ChooseCampaign", new[] { typeof(string), typeof(string), typeof(string) }) ?? throw new MissingMethodException("ChooseCampaign");
                var gui = AccessTools.Method(type, "OnGUI") ?? throw new MissingMethodException("OnGUI");
                integration = this;
                patches = new Harmony("local.tcg.modprofiles.integration");
                patches.Patch(draw, postfix: new HarmonyMethod(typeof(Plugin), nameof(DrawButtons)));
                patches.Patch(AccessTools.Method(type,"Refresh"),postfix:new HarmonyMethod(typeof(Plugin),nameof(RefreshShopNames)));
                patches.Patch(choose, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeLoad)));
                patches.Patch(gui, prefix: new HarmonyMethod(typeof(Plugin), nameof(BeforeSaveGui)));
                Logger.LogInfo("Mod Profiles : bouton Mods intégré, contrôle avant chargement. DLL Save Manager conservées.");
            }
            catch (Exception ex) { patches?.UnpatchSelf(); integration = null; enabled = false; Logger.LogError(ex); }
        }
        private void OnDestroy() { patches?.UnpatchSelf(); if (integration == this) integration = null; }
        private static bool BeforeSaveGui(object __instance)
        {
            if (integration == null) return true;
            integration.saveManager = __instance;
            return !integration.managerVisible;
        }
        private bool BorrowTheme()
        {
            if (saveManager == null || Read(saveManager, "cardStyle") == null) return false;
            foreach (var name in new[] { "cardStyle", "activeCardStyle", "nameStyle", "bodyStyle", "mutedStyle", "blueButton", "quietButton", "goldButton" })
                AccessTools.Field(typeof(Plugin), name).SetValue(this, Read(saveManager, name));
            return true;
        }
        private static void DrawButtons(object __instance, Campaign? current)
        {
            var plugin = integration;
            if (plugin == null || !GUI.enabled) return;
            plugin.saveManager = __instance;
            plugin.EnsureTheme();
            if(!plugin.shopNamesReady)RefreshShopNames(__instance);
            var search = ((string?)plugin.Read(__instance, "search") ?? "").Trim();
            var entries = ((Campaign[]?)plugin.Read(__instance, "campaigns") ?? Array.Empty<Campaign>())
                .Where(c => search == "" || c.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(c => c.Id == current?.Id).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var scroll = (Vector2)(plugin.Read(__instance, "scroll") ?? Vector2.zero);
            GUI.BeginGroup(new Rect(28, 218, 920, 344));
            try
            {
                for (var i = 0; i < entries.Length; i++)
                {
                    var rect = new Rect(i % 2 * 466 + 342 - scroll.x, i / 2 * 180 + 45 - scroll.y, 96, 30);
                    if (rect.yMax < 0 || rect.y > 344) continue;
                    var caption=new Rect(i%2*466+16-scroll.x,i/2*180+48-scroll.y,250,24);
                    plugin.Fill(caption,entries[i].Id==current?.Id?new Color(.79f,.91f,.97f):new Color(1f,.97f,.89f));
                    var shopName=plugin.shopNames.TryGetValue(entries[i].Id,out var stored)?stored:T("Nom en jeu indisponible");
                    var mode=plugin.shopModes.TryGetValue(entries[i].Id,out var savedMode)?savedMode:T("Classique");
                    var description=mode+" · "+shopName;
                    GUI.Label(caption,new GUIContent(description,description),new GUIStyle(plugin.mutedStyle){wordWrap=false,clipping=TextClipping.Clip});
                    var rulesPlugin=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("local.tcg.gamemodes",out var rulesInfo)?rulesInfo.Instance:null;
                    var rulesType=rulesPlugin?.GetType();
                    if(rulesPlugin!=null)
                    {
                        if(GUI.Button(new Rect(rect.x-68,rect.y,64,30),T("Règles"),new GUIStyle(plugin.quietButton){fontSize=12}))
                            plugin.Run(()=>AccessTools.Method(rulesType,"OpenCampaignRules").Invoke(rulesPlugin,new object[]{__instance,entries[i]}));
                    }
                    if (GUI.Button(rect, "Mods", plugin.quietButton))
                        plugin.Run(() => { plugin.OpenModProfile(entries[i]); plugin.loadTarget = null; plugin.managerVisible = true; });
                }
            }
            finally { GUI.EndGroup(); }
        }
        private static bool BeforeLoad(object __instance, string id, string kind, string snapshotId)
        {
            var plugin = integration;
            if (plugin == null || plugin.resuming) return true;
            try
            {
                if (!(bool)AccessTools.Method(__instance.GetType(), "IsAtTitle").Invoke(__instance, null)) return true;
                plugin.saveManager = __instance;
                var campaign = new LabStore(plugin.campaignsRoot).List().Single(c => c.Id == id);
                plugin.OpenModProfile(campaign);
                var pending = plugin.HasPendingChoices();
                var problems = TCG.SaveManager.Core.ModProfiles.Validate(plugin.editingProfile!, plugin.profileInventory);
                var disabled = plugin.SessionDiffers();
                if (!pending && problems.Length == 0 && !disabled) { plugin.RememberStartupProfile(); plugin.profileCampaign = null; return true; }
                plugin.loadTarget = __instance; plugin.loadId = id; plugin.loadKind = kind; plugin.loadSnapshot = snapshotId;
                plugin.managerVisible = true;
                plugin.onlyPending = pending;
                if (!pending) plugin.profileMessage = disabled ? T("Ce profil nécessite un redémarrage. Enregistrez pour préparer le prochain lancement.") : plugin.DescribeProblems(problems);
                return false;
            }
            catch (Exception ex)
            {
                plugin.Logger.LogError(ex);
                AccessTools.Field(__instance.GetType(), "status").SetValue(__instance, F("Contrôle des mods impossible : {0}", ex.Message));
                return false;
            }
        }
        private void ResumeLoad()
        {
            if (HasPendingChoices()) { profileMessage = T("Profil enregistré. Choisissez les mods encore à décider avant de charger."); return; }
            var problems = TCG.SaveManager.Core.ModProfiles.Validate(editingProfile!, profileInventory);
            if (problems.Length != 0) { profileMessage = DescribeProblems(problems); return; }
            if (SessionDiffers())
            {
                SelectStartupProfile();
                profileMessage = T("Choix enregistrés pour le prochain lancement. Quittez puis relancez le jeu, et chargez cette boutique.");
                return;
            }
            RememberStartupProfile();
            var target = loadTarget!;
            resuming = true;
            try { AccessTools.Method(target.GetType(), "ChooseCampaign").Invoke(target, new object[] { loadId, loadKind, loadSnapshot }); managerVisible = false; profileCampaign = null; loadTarget = null; }
            finally { resuming = false; }
        }
    }
}

