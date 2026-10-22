using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using TCG.SaveManager.Core;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    [BepInPlugin("local.tcg.modprofiles", "TCG Mod Profiles", "1.0.0")]
    [BepInDependency("local.tcg.savemanager.diagnostics")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private string campaignsRoot = "";
        private GUIStyle cardStyle = null!, activeCardStyle = null!, nameStyle = null!, bodyStyle = null!, mutedStyle = null!, blueButton = null!, quietButton = null!, goldButton = null!;
        private bool managerVisible;
        private Campaign[] campaigns = Array.Empty<Campaign>();
        private bool IsAtTitle()
        {
            var type = HarmonyLib.AccessTools.TypeByName("CGameManager");
            var game = type == null ? null : UnityEngine.Object.FindObjectOfType(type);
            return game != null && !(bool)HarmonyLib.AccessTools.Field(type, "m_IsGameLevel").GetValue(game);
        }
        private string DisplayName(Campaign campaign) => campaign.Name;
        private void Fill(Rect rect, Color color) { var old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        private void Run(Action action) { try { action(); } catch (Exception ex) { profileMessage = ex.Message; Logger.LogError(ex); } }
        private void Awake()
        {
            campaignsRoot = Path.Combine(Directory.GetParent(Application.persistentDataPath)!.FullName, "campaigns");
            resumeAfter = Time.realtimeSinceStartup + 3f;
            InstallIntegration();
            Run(InstallContentFilter);
            Run(() => Logger.LogInfo("Inventaire sur disque : " + InstalledPlugins().Count() + " plugins."));
        }
        private void EnsureTheme()
        {
            if (BorrowTheme()) return;
            if (cardStyle != null) return;
            cardStyle = new GUIStyle(GUI.skin.box); cardStyle.normal.background = Texture2D.whiteTexture;
            activeCardStyle = new GUIStyle(cardStyle);
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold }; nameStyle.normal.textColor = new Color(.1f,.2f,.3f);
            bodyStyle = new GUIStyle(nameStyle) { fontSize = 16, fontStyle = FontStyle.Normal, wordWrap = true };
            mutedStyle = new GUIStyle(bodyStyle) { fontSize = 14 };
            blueButton = new GUIStyle(GUI.skin.button) { fontSize = 16 }; quietButton = new GUIStyle(blueButton); goldButton = new GUIStyle(blueButton);
        }
        private void OnGUI()
        {
            if (!managerVisible) return;
            EnsureTheme();
            var matrix = GUI.matrix; var depth = GUI.depth; GUI.depth = -1100;
            var scale = Math.Max(.1f, Math.Min(1.25f, Math.Min((Screen.width - 32f) / 1000f, (Screen.height - 32f) / 700f)));
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1000f * scale)/2, (Screen.height - 700f * scale)/2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            try {
                if (profileCampaign != null) { DrawModProfile(); return; }
                Fill(new Rect(0,0,1000,700), new Color(0,0,0,.8f));
                GUI.Box(new Rect(80,45,840,610), GUIContent.none, cardStyle);
                GUI.Label(new Rect(105,65,780,40), T("Profils de mods — choisir une boutique"), nameStyle);
                profileScroll = GUI.BeginScrollView(new Rect(105,130,790,420),profileScroll,new Rect(0,0,765,Math.Max(420,campaigns.Length*60)));
                try { for (var i=0;i<campaigns.Length;i++) { var campaign=campaigns[i]; if(GUI.Button(new Rect(0,i*60,740,48),campaign.Name,blueButton)) Run(()=>OpenModProfile(campaign)); } }
                finally { GUI.EndScrollView(); }
                if(GUI.Button(new Rect(105,590,180,42),T("Fermer"),quietButton)) managerVisible=false;
                if(profileMessage!="") GUI.Label(new Rect(300,580,570,60),profileMessage,bodyStyle);
            } finally { GUI.matrix=matrix; GUI.depth=depth; }
        }
        private Campaign? profileCampaign;
        private ModProfile? editingProfile;
        private InstalledMod[] profileInventory = Array.Empty<InstalledMod>();
        private readonly Dictionary<string, string> modNames = new Dictionary<string, string>();
        private Vector2 profileScroll;
        private string profileMessage = "";
        private bool onlyPending;
        private readonly HashSet<string> reviewedRows = new HashSet<string>(StringComparer.Ordinal);
        private void OpenModProfile(Campaign campaign)
        {
            if (!IsAtTitle()) throw new InvalidOperationException(T("Revenez au menu principal pour préparer les mods."));
            modNames.Clear();
            reviewedRows.Clear();
            var inventory = InstalledPlugins().Select(plugin =>
            {
                
                modNames[plugin.Metadata.GUID] = plugin.Metadata.Name;
                return new InstalledMod {
                    Id = plugin.Metadata.GUID, Name = plugin.Metadata.Name, Version = plugin.Metadata.Version.ToString(), Sha256 = LabStore.Hash(plugin.Location), Assembly = plugin.Location,
                    Essential = plugin.Metadata.GUID == "local.tcg.savemanager.diagnostics" || plugin.Metadata.GUID == "local.tcg.modprofiles" || plugin.Metadata.GUID == "local.tcg.gamemodes",
                    Dependencies = plugin.Dependencies.Select(d => new ModDependency {
                        Id = d.DependencyGUID, Required = (d.Flags & BepInDependency.DependencyFlags.HardDependency) != 0,
                        MinimumVersion = d.MinimumVersion?.ToString() ?? "" }).ToArray(),
                    Incompatibilities = plugin.Incompatibilities.Select(d => d.IncompatibilityGUID).ToArray()
                };
            }).Concat(ContentInventory()).GroupBy(m=>m.Id).Select(g=>g.First()).OrderBy(m => modNames[m.Id], StringComparer.OrdinalIgnoreCase).ToArray();
            modSettings = TCG.SaveManager.Core.ModProfiles.LoadSettings(campaignsRoot);
            var stored = TCG.SaveManager.Core.ModProfiles.Load(campaignsRoot, campaign.Id);
            var profile = TCG.SaveManager.Core.ModProfiles.Reconcile(stored, inventory, modSettings.NewMods);
            profileInventory = inventory; editingProfile = profile; profileCampaign = campaign; profileScroll = Vector2.zero; profileMessage = ""; onlyPending = false;
            modSearch = ""; pendingDisable = null; copyPicker = false;
            // 0.3.1 : a shop without a profile starts from the last applied profile (or the mods loaded
            // right now) instead of asking for every mod one by one.
            if (stored.Mods.Length == 0) profileMessage = SeedNewProfile();
            BuildGroups();
        }
        private void SetChoice(InstalledMod mod, ModChoice choice)
        {
            profileMessage = "";
            if (profileInventory.Any(m => m.Assembly == mod.Assembly && m.Essential)) choice = ModChoice.Enabled;
            foreach (var sibling in profileInventory.Where(m => string.Equals(m.Assembly, mod.Assembly, StringComparison.OrdinalIgnoreCase)))
            {
                var decision = editingProfile!.Mods.Single(d => d.Id == sibling.Id);
                decision.Choice = sibling.Essential ? ModChoice.Enabled : choice;
                decision.ReviewedVersion = sibling.Version; decision.ReviewedHash = sibling.Sha256; decision.UpdatedFrom = "";
                reviewedRows.Add(sibling.Id);
            }
        }
        private void EnableRequired(InstalledMod mod, HashSet<string> visited)
        {
            if (!visited.Add(mod.Id)) return;
            SetChoice(mod, ModChoice.Enabled);
            foreach (var sibling in profileInventory.Where(m => m.Assembly == mod.Assembly))
                foreach (var dependency in sibling.Dependencies.Where(d => d.Required))
                {
                    var target = profileInventory.FirstOrDefault(m => m.Id == dependency.Id);
                    if (target != null) EnableRequired(target, visited);
                }
        }
        private string DescribeProblems(string[] problems)
            => string.Join("\n", problems.Take(2).Select(message => TCG.ModProfiles.Shared.ProfileMessages.Describe(message, modNames)));
    }
}
