using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using BepInEx.Bootstrap;
using HarmonyLib;
using TCG.SaveManager.Core;
using UnityEngine;
using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    public sealed partial class Plugin
    {
        public sealed class RelaunchRequest
        {
            public string CampaignId { get; set; } = "";
            public string Kind { get; set; } = "";
            public string SnapshotId { get; set; } = "";
            public DateTime CreatedUtc { get; set; }
        }
        private bool restartPending, resumeChecked;
        private float resumeAfter;
        private string ResumePath => Path.Combine(Directory.GetParent(campaignsRoot)!.FullName, "mod-profile-resume.xml");
        private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";
        private static string WindowsArgument(string value)
        {
            var result = new StringBuilder("\""); var slashes = 0;
            foreach (var c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '\"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
                else { result.Append('\\', slashes); result.Append(c); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('\"'); return result.ToString();
        }
        private void ApplyAndRelaunch()
        {
            if (restartPending) return;
            if (!IsAtTitle()) throw new InvalidOperationException(T("Revenez au menu principal pour appliquer les mods."));
            if (HasPendingChoices()) throw new InvalidOperationException(T("Choisissez les mods encore à décider."));
            var errors = TCG.SaveManager.Core.ModProfiles.Validate(editingProfile!, profileInventory);
            if (errors.Length != 0) throw new InvalidOperationException(DescribeProblems(errors));
            var store = new LabStore(campaignsRoot);
            var id = profileCampaign!.Id;
            var kind = loadTarget != null ? loadKind : store.Latest(id, "auto") != null ? "auto" : store.Latest(id, "manual") != null ? "manual" : "new";
            var snapshot = loadTarget != null ? loadSnapshot : "";
            if (saveManager != null && Read(saveManager, "workspace") is CampaignWorkspace workspace)
                workspace.CheckRequiredMods(id, kind, snapshot, editingProfile!.Mods.Where(d => d.Choice == ModChoice.Enabled).Select(d => d.Id));
            TCG.SaveManager.Core.ModProfiles.Save(campaignsRoot, editingProfile!);
            SelectStartupProfile();
            using (var writer = XmlWriter.Create(ResumePath + ".pending"))
                new XmlSerializer(typeof(RelaunchRequest)).Serialize(writer, new RelaunchRequest { CampaignId = id, Kind = kind, SnapshotId = snapshot, CreatedUtc = DateTime.UtcNow });
            if (File.Exists(ResumePath)) File.Replace(ResumePath + ".pending", ResumePath, null); else File.Move(ResumePath + ".pending", ResumePath);
            using (var process = Process.GetCurrentProcess())
            {
                var exe = process.MainModule!.FileName;
                if (!string.Equals(Path.GetFullPath(exe), Path.Combine(BepInEx.Paths.GameRootPath, "Card Shop Simulator.exe"), StringComparison.OrdinalIgnoreCase))
                    throw new IOException(T("Exécutable de relancement inconnu."));
                var arguments = string.Join(" ", Environment.GetCommandLineArgs().Skip(1).Select(WindowsArgument));
                var script = "$ErrorActionPreference='Stop'; $p=Get-Process -Id " + process.Id + " -ErrorAction SilentlyContinue; if($p -and !$p.WaitForExit(60000)){exit 1}; " +
                    "$psi=New-Object System.Diagnostics.ProcessStartInfo; $psi.FileName=" + PsQuote(exe) + "; $psi.WorkingDirectory=" + PsQuote(Path.GetDirectoryName(exe)!) + "; " +
                    "$psi.UseShellExecute=$false; $psi.Arguments=" + PsQuote(arguments) + "; " + TCG.ModProfiles.Shared.RelaunchEnvironment.Reset + "[System.Diagnostics.Process]::Start($psi) | Out-Null";
                // Windows PowerShell waits for the old process before starting its replacement.
                var pwsh = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(pwsh)) { File.Delete(ResumePath); throw new IOException(T("PowerShell Windows indisponible pour le relancement automatique.")); }
                Process? helper;
                try { helper = Process.Start(new ProcessStartInfo(pwsh, "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) { UseShellExecute = false, CreateNoWindow = true }); }
                catch { File.Delete(ResumePath); throw; }
                if (helper == null) throw new IOException(T("Relancement impossible."));
                helper.Dispose();
            }
            restartPending = true;
            profileMessage = T("Relancement de la boutique…");
            Application.Quit();
        }
        private void Update()
        {
            if (resumeChecked || restartPending || Time.realtimeSinceStartup < resumeAfter) return;
            if (!File.Exists(ResumePath)) { resumeChecked = true; return; }
            if (!Chainloader.PluginInfos.TryGetValue("local.tcg.savemanager.diagnostics", out var info)) return;
            saveManager = info.Instance;
            if (!(bool)AccessTools.Method(saveManager.GetType(), "IsAtTitle").Invoke(saveManager, null)) return;
            resumeChecked = true;
            try
            {
                RelaunchRequest request;
                using (var reader = XmlReader.Create(ResumePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    request = (RelaunchRequest)new XmlSerializer(typeof(RelaunchRequest)).Deserialize(reader);
                // Consume before loading: failures cannot create an automatic relaunch loop.
                File.Delete(ResumePath);
                if (DateTime.UtcNow - request.CreatedUtc > TimeSpan.FromMinutes(10) || request.CreatedUtc > DateTime.UtcNow.AddMinutes(1)) throw new IOException(T("Reprise expirée : choisissez votre boutique dans le menu."));
                if (!Guid.TryParseExact(request.CampaignId, "N", out _) || !new[] { "auto", "manual", "new" }.Contains(request.Kind)) throw new IOException(T("Reprise invalide."));
                Logger.LogInfo("Reprise après relancement : " + request.CampaignId + " / " + request.Kind);
                AccessTools.Method(saveManager.GetType(), "ChooseCampaign").Invoke(saveManager, new object[] { request.CampaignId, request.Kind, request.SnapshotId });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                // Return to the normal shop menu with the failure visible.
                AccessTools.Field(saveManager.GetType(), "status").SetValue(saveManager, F("Reprise automatique impossible : {0}", ex.Message));
                AccessTools.Field(saveManager.GetType(), "visible").SetValue(saveManager, true);
                if (File.Exists(ResumePath)) File.Move(ResumePath, ResumePath + ".failed-" + Guid.NewGuid().ToString("N"));
            }
        }
    }
}
