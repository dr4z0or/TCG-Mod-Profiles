using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using TCG.SaveManager.Core;
using UnityEngine;

using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    // 0.3.0 : grouped, searchable mod list with cascading deactivation and copy from another shop.
    public sealed partial class Plugin
    {
        private sealed class ModGroup
        {
            public string Key = "";
            public string Title = "";
            public bool Packs;
            public bool Missing;
            public readonly List<InstalledMod> Mods = new List<InstalledMod>();
            public readonly List<ModDecision> Absent = new List<ModDecision>();
        }
        private readonly List<ModGroup> groups = new List<ModGroup>();
        private readonly Dictionary<string, bool> expandedGroups = new Dictionary<string, bool>(StringComparer.Ordinal);
        private ModSettings modSettings = new ModSettings();
        private string modSearch = "";
        private InstalledMod? pendingDisable;
        private string[] pendingDependents = Array.Empty<string>();
        private bool copyPicker;
        private Vector2 copyScroll;
        private Campaign[] copyCandidates = Array.Empty<Campaign>();

        private string NameOf(string id)
        {
            if (modNames.TryGetValue(id, out var name)) return name;
            var decision = editingProfile?.Mods.FirstOrDefault(d => d.Id == id);
            return decision != null && decision.Name != "" ? decision.Name : id;
        }
        private static string PackLabel(string name) => name.StartsWith("Pack · ", StringComparison.Ordinal) ? name.Substring(7) : name;
        // Packs are grouped by the folder that holds their *_prefabloader folders.
        private static string PackFamily(InstalledMod mod)
        {
            try
            {
                var folder = Path.GetDirectoryName(Path.GetDirectoryName(mod.Assembly) ?? "") ?? "";
                var root = Paths.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return T("Autres packs");
                var parts = folder.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return T("Autres packs");
                return string.Join(" › ", (parts.Length > 1 ? parts.Skip(1) : parts).ToArray());
            }
            catch (ArgumentException) { return T("Autres packs"); }
        }
        private void BuildGroups()
        {
            groups.Clear();
            var required = new HashSet<string>(profileInventory.SelectMany(m => m.Dependencies.Where(d => d.Required).Select(d => d.Id)), StringComparer.Ordinal);
            ModGroup Group(string key, string title, bool packs = false, bool expanded = true)
            {
                var group = groups.FirstOrDefault(g => g.Key == key);
                if (group != null) return group;
                group = new ModGroup { Key = key, Title = title, Packs = packs };
                groups.Add(group);
                if (!expandedGroups.ContainsKey(key)) expandedGroups[key] = expanded;
                return group;
            }
            Group("mods", T("Mods"));
            Group("libraries", T("Bibliothèques (requises par d’autres mods)"));
            foreach (var mod in profileInventory)
            {
                if (mod.Essential) Group("essential", T("Indispensables (gestionnaire de boutiques)"), false, false).Mods.Add(mod);
                else if (mod.Id.StartsWith("content:", StringComparison.Ordinal)) { var family = PackFamily(mod); Group("pack:" + family, "Packs · " + family, true, false).Mods.Add(mod); }
                else if (required.Contains(mod.Id)) Group("libraries", "").Mods.Add(mod);
                else Group("mods", "").Mods.Add(mod);
            }
            var absent = editingProfile!.Mods.Where(d => !profileInventory.Any(m => m.Id == d.Id)).ToArray();
            if (absent.Length > 0) { var group = Group("missing", T("Mods absents du dossier plugins")); group.Missing = true; group.Absent.AddRange(absent); }
            groups.RemoveAll(g => g.Mods.Count == 0 && g.Absent.Count == 0);
            foreach (var group in groups) group.Mods.Sort((a, b) => string.Compare(NameOf(a.Id), NameOf(b.Id), StringComparison.OrdinalIgnoreCase));
            groups.Sort((a, b) =>
            {
                int Rank(ModGroup g) => g.Key == "mods" ? 0 : g.Key == "libraries" ? 1 : g.Packs ? 2 : g.Missing ? 3 : 4;
                var rank = Rank(a).CompareTo(Rank(b));
                return rank != 0 ? rank : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });
        }
        private ModDecision Decision(string id) => editingProfile!.Mods.Single(d => d.Id == id);
        private bool Visible(InstalledMod mod)
        {
            if (onlyPending && Decision(mod.Id).Choice != ModChoice.Pending && !reviewedRows.Contains(mod.Id) && Decision(mod.Id).UpdatedFrom == "") return false;
            return modSearch.Trim() == "" || NameOf(mod.Id).IndexOf(modSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private bool Expanded(ModGroup group) => modSearch.Trim() != "" || onlyPending || (expandedGroups.TryGetValue(group.Key, out var open) && open);
        private float RowHeight(ModGroup group) => group.Packs ? 40 : 78;

        // Disabling a mod first lists every enabled mod that requires it.
        private void RequestDisable(InstalledMod mod)
        {
            var dependents = TCG.SaveManager.Core.ModProfiles.Dependents(mod.Id, profileInventory)
                .Where(id => editingProfile!.Mods.Any(d => d.Id == id && d.Choice != ModChoice.Disabled) && profileInventory.Any(m => m.Id == id && !m.Essential)).ToArray();
            if (dependents.Length == 0) { SetChoice(mod, ModChoice.Disabled); return; }
            pendingDisable = mod; pendingDependents = dependents;
        }
        private void ConfirmDisable()
        {
            if (pendingDisable == null) return;
            SetChoice(pendingDisable, ModChoice.Disabled);
            foreach (var id in pendingDependents) { var mod = profileInventory.FirstOrDefault(m => m.Id == id); if (mod != null) SetChoice(mod, ModChoice.Disabled); }
            profileMessage = F("{0} et {1} mod(s) dépendant(s) désactivés.", NameOf(pendingDisable.Id), pendingDependents.Length);
            pendingDisable = null; pendingDependents = Array.Empty<string>();
        }
        private void CopyFrom(Campaign source)
        {
            var other = TCG.SaveManager.Core.ModProfiles.Load(campaignsRoot, source.Id);
            var copied = 0;
            foreach (var mod in profileInventory.Where(m => !m.Essential))
            {
                var decision = other.Mods.FirstOrDefault(d => d.Id == mod.Id);
                if (decision == null || decision.Choice == ModChoice.Pending) continue;
                SetChoice(mod, decision.Choice);
                copied++;
            }
            copyPicker = false;
            profileMessage = F("{0} choix copiés depuis « {1} ». Vérifiez puis enregistrez.", copied, source.Name);
        }
        private string SeedNewProfile()
        {
            ModProfile? source = null; var sourceName = "";
            try
            {
                var root = Directory.GetParent(campaignsRoot)!.FullName;
                if (File.Exists(Path.Combine(root, "mod-profile-selection.xml")))
                {
                    var id = TCG.ModProfiles.Shared.StartupSelection.Read(root);
                    if (id != profileCampaign!.Id)
                    {
                        source = TCG.SaveManager.Core.ModProfiles.Load(campaignsRoot, id);
                        sourceName = new LabStore(campaignsRoot).List().FirstOrDefault(c => c.Id == id)?.Name ?? "";
                    }
                }
            }
            catch (Exception ex) { Logger.LogWarning(F("Profil de départ indisponible : {0}", ex.Message)); source = null; }
            var copied = 0;
            foreach (var mod in profileInventory.Where(m => !m.Essential))
            {
                var decision = Decision(mod.Id);
                if (decision.Choice != ModChoice.Pending) continue;
                var previous = source?.Mods.FirstOrDefault(d => d.Id == mod.Id && d.Choice != ModChoice.Pending);
                decision.Choice = previous?.Choice ?? (SessionEnabled(mod.Id) ? ModChoice.Enabled : ModChoice.Disabled);
                decision.ReviewedVersion = mod.Version; decision.ReviewedHash = mod.Sha256;
                if (previous != null) copied++;
            }
            return copied > 0 && sourceName != ""
                ? F("Nouvelle boutique : choix repris de « {0} ». Ajustez si besoin puis enregistrez.", sourceName)
                : T("Nouvelle boutique : les mods chargés actuellement sont actifs. Ajustez si besoin puis enregistrez.");
        }
        private void DrawDisableConfirm()
        {
            Fill(new Rect(0, 0, 1000, 700), new Color(0, 0, 0, 0.6f));
            GUI.Box(new Rect(200, 220, 600, 260), GUIContent.none, cardStyle);
            GUI.Label(new Rect(225, 238, 550, 36), F("Désactiver {0} ?", NameOf(pendingDisable!.Id)), nameStyle);
            var names = string.Join(", ", pendingDependents.Take(5).Select(n => PackLabel(NameOf(n))).ToArray()) + (pendingDependents.Length > 5 ? F(" et {0} autre(s)", pendingDependents.Length - 5) : "");
            GUI.Label(new Rect(225, 280, 550, 110), F("{0} mod(s) en dépendent et seront aussi désactivés : {1}.", pendingDependents.Length, names), bodyStyle);
            if (GUI.Button(new Rect(225, 410, 260, 46), T("Tout désactiver"), goldButton)) ConfirmDisable();
            if (GUI.Button(new Rect(500, 410, 275, 46), T("Annuler"), quietButton)) { pendingDisable = null; pendingDependents = Array.Empty<string>(); }
        }
        private static string PolicyLabel(NewModPolicy policy) => policy == NewModPolicy.Enable ? T("Nouveaux : actifs") : policy == NewModPolicy.Disable ? T("Nouveaux : désactivés") : T("Nouveaux : à décider");

        private void DrawModProfile()
        {
            Fill(new Rect(0, 0, 1000, 700), new Color(0, 0, 0, 0.72f));
            GUI.Box(new Rect(80, 45, 840, 610), GUIContent.none, cardStyle);
            GUI.Label(new Rect(105, 62, 780, 40), F("Mods · {0}", DisplayName(profileCampaign!)), nameStyle);
            GUI.Label(new Rect(105, 102, 780, 44), loadTarget != null ? T("Vérifiez les choix de cette boutique avant son chargement.") : T("Choisissez les mods de cette boutique. Activer un mod active aussi ce dont il a besoin ; désactiver un mod propose de désactiver ce qui en dépend."), mutedStyle);
            var small = new GUIStyle(quietButton) { fontSize = 12, padding = new RectOffset(6, 6, 4, 4) };
            var smallOn = new GUIStyle(blueButton) { fontSize = 12, padding = new RectOffset(6, 6, 4, 4) };
            var interactive = pendingDisable == null && !copyPicker;
            var enabled = GUI.enabled;
            GUI.enabled = interactive;
            GUI.Label(new Rect(105, 154, 80, 26), T("Chercher"), mutedStyle);
            modSearch = GUI.TextField(new Rect(180, 150, 250, 30), modSearch, 60);
            var undecided = profileInventory.Count(m => Decision(m.Id).Choice == ModChoice.Pending);
            var updated = profileInventory.Count(m => Decision(m.Id).UpdatedFrom != "");
            if (GUI.Button(new Rect(440, 150, 160, 30), new GUIContent(onlyPending ? T("Voir tous les mods") : F("À revoir ({0})", undecided + updated), T("Nouveaux mods à décider et mods mis à jour dont le choix a été conservé.")), onlyPending ? smallOn : small)) { onlyPending = !onlyPending; profileScroll = Vector2.zero; }
            if (GUI.Button(new Rect(606, 150, 140, 30), new GUIContent(T("Copier depuis…"), T("Reprendre les choix d’une autre boutique.")), small))
                Run(() => { copyCandidates = new LabStore(campaignsRoot).List().Where(c => c.Id != profileCampaign!.Id).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(); copyPicker = true; copyScroll = Vector2.zero; });
            if (GUI.Button(new Rect(752, 150, 143, 30), new GUIContent(PolicyLabel(modSettings.NewMods), T("Choix appliqué aux mods qu’une boutique découvre pour la première fois (toutes boutiques).")), small))
                Run(() =>
                {
                    modSettings.NewMods = modSettings.NewMods == NewModPolicy.Ask ? NewModPolicy.Enable : modSettings.NewMods == NewModPolicy.Enable ? NewModPolicy.Disable : NewModPolicy.Ask;
                    TCG.SaveManager.Core.ModProfiles.SaveSettings(campaignsRoot, modSettings);
                    profileMessage = modSettings.NewMods == NewModPolicy.Ask ? T("Les nouveaux mods seront à décider dans chaque boutique.") : modSettings.NewMods == NewModPolicy.Enable ? T("Les nouveaux mods seront activés automatiquement (dépendances à vérifier).") : T("Les nouveaux mods seront désactivés automatiquement.");
                });
            var height = 0f;
            foreach (var group in groups)
            {
                var visible = group.Missing ? group.Absent.Count : group.Mods.Count(Visible);
                if (visible == 0 && (modSearch.Trim() != "" || onlyPending)) continue;
                height += 38;
                if (Expanded(group)) height += group.Missing ? group.Absent.Count * 48 : group.Mods.Count(Visible) * RowHeight(group);
            }
            profileScroll = GUI.BeginScrollView(new Rect(105, 188, 790, 317), profileScroll, new Rect(0, 0, 765, Math.Max(317, height)));
            try
            {
                var y = 0f;
                if (height == 0) GUI.Label(new Rect(8, 8, 740, 30), onlyPending ? T("Aucun mod à revoir.") : T("Aucun mod ne correspond à cette recherche."), bodyStyle);
                foreach (var group in groups)
                {
                    var visible = group.Missing ? group.Absent.Count : group.Mods.Count(Visible);
                    if (visible == 0 && (modSearch.Trim() != "" || onlyPending)) continue;
                    var open = Expanded(group);
                    var active = group.Mods.Count(m => Decision(m.Id).Choice == ModChoice.Enabled);
                    var pending = group.Mods.Count(m => Decision(m.Id).Choice == ModChoice.Pending);
                    var header = (open ? "▾ " : "▸ ") + group.Title + (group.Missing ? " (" + group.Absent.Count + ")" : F(" · {0}/{1} actifs", active, group.Mods.Count) + (pending > 0 ? F(" · {0} à décider", pending) : ""));
                    if (GUI.Button(new Rect(0, y, group.Packs || group.Key == "mods" || group.Key == "libraries" ? 470 : 755, 32), header, new GUIStyle(quietButton) { fontSize = 14, alignment = TextAnchor.MiddleLeft }))
                        expandedGroups[group.Key] = !open;
                    if (!group.Missing && group.Key != "essential")
                    {
                        if (GUI.Button(new Rect(478, y, 136, 32), T("Tout activer"), small))
                            foreach (var mod in group.Mods.Where(Visible).ToArray()) EnableRequired(mod, new HashSet<string>(StringComparer.Ordinal));
                        if (GUI.Button(new Rect(620, y, 135, 32), T("Tout désactiver"), small))
                        {
                            var members = group.Mods.Where(Visible).ToArray();
                            var outside = members.SelectMany(m => TCG.SaveManager.Core.ModProfiles.Dependents(m.Id, profileInventory)).Distinct()
                                .Where(id => !members.Any(m => m.Id == id) && editingProfile!.Mods.Any(d => d.Id == id && d.Choice != ModChoice.Disabled) && profileInventory.Any(m => m.Id == id && !m.Essential)).ToArray();
                            foreach (var mod in members) SetChoice(mod, ModChoice.Disabled);
                            if (outside.Length > 0) profileMessage = F("Attention : {0} dépend de ce groupe. Désactivez-le aussi ou réactivez une dépendance.", string.Join(", ", outside.Take(3).Select(NameOf).ToArray()) + (outside.Length > 3 ? "…" : ""));
                        }
                    }
                    y += 38;
                    if (!open) continue;
                    if (group.Missing)
                    {
                        foreach (var decision in group.Absent)
                        {
                            GUI.Box(new Rect(0, y, 755, 42), GUIContent.none, cardStyle);
                            GUI.Label(new Rect(12, y + 9, 540, 26), (decision.Name != "" ? decision.Name : decision.Id) + " · " + (decision.Choice == ModChoice.Enabled ? T("était actif") : decision.Choice == ModChoice.Disabled ? T("désactivé") : T("à décider")), bodyStyle);
                            var rowEnabled = GUI.enabled;
                            GUI.enabled = rowEnabled && decision.Choice != ModChoice.Disabled;
                            if (GUI.Button(new Rect(575, y + 5, 168, 32), decision.Choice == ModChoice.Disabled ? T("Désactivé") : T("Désactiver"), small))
                            { decision.Choice = ModChoice.Disabled; profileMessage = T("Choix corrigé pour ce mod absent. Enregistrez le profil."); }
                            GUI.enabled = rowEnabled;
                            y += 48;
                        }
                        continue;
                    }
                    foreach (var mod in group.Mods.Where(Visible))
                    {
                        var decision = Decision(mod.Id);
                        var name = group.Packs ? PackLabel(NameOf(mod.Id)) : NameOf(mod.Id) + " · " + mod.Version;
                        if (group.Packs)
                        {
                            GUI.Box(new Rect(0, y, 755, 34), GUIContent.none, decision.Choice == ModChoice.Pending ? cardStyle : activeCardStyle);
                            GUI.Label(new Rect(12, y + 5, 480, 26), new GUIContent(name + (decision.Choice == ModChoice.Pending ? T(" · à décider") : decision.UpdatedFrom != "" ? T(" · modifié, choix conservé") : ""), NameOf(mod.Id)), new GUIStyle(bodyStyle) { fontSize = 14, wordWrap = false, clipping = TextClipping.Clip });
                        }
                        else
                        {
                            GUI.Box(new Rect(0, y, 755, 70), GUIContent.none, decision.Choice == ModChoice.Pending ? cardStyle : activeCardStyle);
                            GUI.Label(new Rect(12, y + 5, 495, 26), new GUIContent(name, mod.Id), new GUIStyle(bodyStyle) { wordWrap = false, clipping = TextClipping.Clip });
                            var deps = mod.Dependencies.Where(d => d.Required).Select(d => NameOf(d.Id) + (string.IsNullOrEmpty(d.MinimumVersion) ? "" : " ≥ " + d.MinimumVersion)).ToArray();
                            var optional = mod.Dependencies.Where(d => !d.Required && profileInventory.Any(m => m.Id == d.Id)).Select(d => NameOf(d.Id)).ToArray();
                            var users = TCG.SaveManager.Core.ModProfiles.Dependents(mod.Id, profileInventory).Length;
                            var details = mod.Essential ? T("Indispensable") :
                                decision.UpdatedFrom != "" ? F("Mis à jour depuis {0} · choix conservé", decision.UpdatedFrom.Replace(" (fichier modifié)", T(" (fichier modifié)"))) :
                                decision.Choice == ModChoice.Pending ? T("Nouveau : à décider") :
                                deps.Length > 0 ? F("Requiert : {0}", string.Join(", ", deps)) :
                                users > 0 ? F("Utilisé par {0} mod(s)", users) :
                                optional.Length > 0 ? F("Fonctionne aussi avec : {0}", string.Join(", ", optional)) :
                                SessionEnabled(mod.Id) ? T("Chargé dans cette session") : T("Non chargé dans cette session");
                            GUI.Label(new Rect(12, y + 35, 495, 25), new GUIContent(details, details), new GUIStyle(mutedStyle) { wordWrap = false, clipping = TextClipping.Clip });
                        }
                        var buttonY = group.Packs ? y + 2 : y + 13; var buttonH = group.Packs ? 30 : 42;
                        if (mod.Essential) { GUI.Label(new Rect(575, y + 20, 168, 30), T("Toujours actif"), bodyStyle); y += RowHeight(group); continue; }
                        var style = group.Packs ? small : quietButton; var styleOn = group.Packs ? smallOn : blueButton;
                        if (GUI.Button(new Rect(515, buttonY, 106, buttonH), decision.Choice == ModChoice.Enabled ? T("Actif") : T("Activer"), decision.Choice == ModChoice.Enabled ? styleOn : style) && (decision.Choice != ModChoice.Enabled || decision.UpdatedFrom != ""))
                            EnableRequired(mod, new HashSet<string>(StringComparer.Ordinal));
                        if (GUI.Button(new Rect(629, buttonY, 126, buttonH), decision.Choice == ModChoice.Disabled ? T("Désactivé") : T("Désactiver"), decision.Choice == ModChoice.Disabled ? styleOn : style) && (decision.Choice != ModChoice.Disabled || decision.UpdatedFrom != ""))
                            RequestDisable(mod);
                        y += RowHeight(group);
                    }
                }
            }
            finally { GUI.EndScrollView(); }
            GUI.enabled = enabled;
            var errors = TCG.SaveManager.Core.ModProfiles.Validate(editingProfile!, profileInventory);
            var pendingCount = profileInventory.Count(m => Decision(m.Id).Choice == ModChoice.Pending);
            GUI.Label(new Rect(105, 512, 790, 66), profileMessage != "" ? profileMessage : errors.Length > 0 ? DescribeProblems(errors) : pendingCount > 0 ? F("{0} mod(s) à décider (bouton « À revoir »). Le profil peut être enregistré comme brouillon.", pendingCount) : T("Profil prêt à être enregistré. Les mods chargés restent actifs dans cette session."), profileMessage != "" ? new GUIStyle(bodyStyle) { fontStyle = FontStyle.Bold } : mutedStyle);
            GUI.enabled = interactive;
            if (GUI.Button(new Rect(105, 590, 120, 42), T("Fermer"), quietButton)) { managerVisible = false; profileCampaign = null; loadTarget = null; }
            if (GUI.Button(new Rect(240, 590, 310, 42), onlyPending ? T("Activer les mods affichés") : modSearch.Trim() != "" ? T("Activer les résultats") : T("Activer tous les mods"), blueButton))
                foreach (var mod in profileInventory.Where(Visible).ToArray()) EnableRequired(mod, new HashSet<string>(StringComparer.Ordinal));
            var canRelaunch = errors.Length == 0 && pendingCount == 0 && SessionDiffers();
            if (GUI.Button(new Rect(565, 590, 330, 42), canRelaunch ? T("Appliquer et relancer") : pendingCount > 0 ? F("Enregistrer ({0} à décider)", pendingCount) : T("Enregistrer le profil"), goldButton)) Run(() =>
            {
                // Saving confirms the updated mods the player has seen in this list.
                foreach (var decision in editingProfile!.Mods) decision.UpdatedFrom = "";
                if (canRelaunch) { ApplyAndRelaunch(); return; }
                TCG.SaveManager.Core.ModProfiles.Save(campaignsRoot, editingProfile!);
                profileMessage = errors.Length == 0 ? T("Profil enregistré.") : T("Brouillon enregistré : résoudre les dépendances avant application.");
                if (loadTarget != null) ResumeLoad();
                else if (errors.Length == 0 && pendingCount == 0) { RememberStartupProfile(); profileMessage = SessionDiffers() ? T("Profil enregistré pour le prochain lancement. Quittez puis relancez le jeu.") : T("Profil enregistré et compatible avec cette session."); }
            });
            GUI.enabled = enabled;
            if (copyPicker) DrawCopyPicker();
            if (pendingDisable != null) DrawDisableConfirm();
        }
        private void DrawCopyPicker()
        {
            var others = copyCandidates;
            Fill(new Rect(0, 0, 1000, 700), new Color(0, 0, 0, 0.6f));
            GUI.Box(new Rect(250, 140, 500, 420), GUIContent.none, cardStyle);
            GUI.Label(new Rect(275, 158, 450, 36), T("Copier les choix de…"), nameStyle);
            GUI.Label(new Rect(275, 196, 450, 44), T("Les mods inconnus de l’autre boutique gardent leur choix actuel."), mutedStyle);
            copyScroll = GUI.BeginScrollView(new Rect(275, 245, 450, 245), copyScroll, new Rect(0, 0, 426, Math.Max(245, others.Length * 50)));
            try
            {
                if (others.Length == 0) GUI.Label(new Rect(6, 6, 420, 30), T("Aucune autre boutique."), bodyStyle);
                for (var i = 0; i < others.Length; i++)
                {
                    var campaign = others[i];
                    if (GUI.Button(new Rect(0, i * 50, 420, 42), campaign.Name, quietButton)) Run(() => CopyFrom(campaign));
                }
            }
            finally { GUI.EndScrollView(); }
            if (GUI.Button(new Rect(275, 502, 150, 40), T("Annuler"), blueButton)) copyPicker = false;
        }
    }
}
