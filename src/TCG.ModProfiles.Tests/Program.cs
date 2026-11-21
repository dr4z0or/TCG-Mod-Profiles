using System;
using System.IO;
using TCG.ModProfiles.Shared;
var root = Path.Combine(Path.GetTempPath(), "TCG-profile-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "campaigns"));
File.WriteAllText(Path.Combine(root, "campaigns", ".tcg-save-lab"), "test");
var a = Guid.NewGuid().ToString("N"); var b = Guid.NewGuid().ToString("N");
foreach(var id in new[]{a,b}) { Directory.CreateDirectory(Path.Combine(root,"campaigns",id)); File.WriteAllText(Path.Combine(root,"campaigns",id,"campaign.xml"),"fixture"); }
foreach(var id in new[]{a,b,a}) { StartupSelection.Save(root,id); if(StartupSelection.Read(root)!=id) throw new Exception("Ancien profil conservé."); }
Console.WriteLine("OK : prochain lancement suit les transitions A / B / A.");
try { StartupSelection.Save(root,"../invalid"); throw new Exception("Identifiant accepté."); } catch(IOException) { }
try { StartupSelection.Save(root,Guid.NewGuid().ToString("N")); throw new Exception("Boutique absente acceptée."); } catch(IOException) { }
if(StartupSelection.Read(root)!=a) throw new Exception("Sélection précédente perdue.");
Console.WriteLine("OK : sélection invalide refusée, sélection précédente conservée.");
File.WriteAllText(Path.Combine(root,"mod-profile-selection.xml"),"<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///unknown'>]><CampaignId>&e;</CampaignId>");
try { StartupSelection.Read(root); throw new Exception("DTD acceptée."); } catch(System.Xml.XmlException) { }
Console.WriteLine("OK : document externe refusé.");
// Only files created by this test are removed, without recursive deletion.
File.Delete(Path.Combine(root,"mod-profile-selection.xml"));
foreach(var id in new[]{a,b}) { File.Delete(Path.Combine(root,"campaigns",id,"campaign.xml")); Directory.Delete(Path.Combine(root,"campaigns",id)); }
File.Delete(Path.Combine(root,"campaigns",".tcg-save-lab")); Directory.Delete(Path.Combine(root,"campaigns")); Directory.Delete(root);

var shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
if (!File.Exists(shell)) Console.WriteLine("IGNORÉ : test du relancement (Windows PowerShell absent).");
else {
var check = "$psi=New-Object System.Diagnostics.ProcessStartInfo; " + RelaunchEnvironment.Reset + "$psi.EnvironmentVariables['TCG_KEEP_TEST']; if(@($psi.EnvironmentVariables.Keys | Where-Object { $_ -like 'DOORSTOP_*' }).Count){exit 2}";
var start = new System.Diagnostics.ProcessStartInfo(shell, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(check))) { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true };
start.Environment["DOORSTOP_INITIALIZED"]="1"; start.Environment["DOORSTOP_DISABLE"]="1"; start.Environment["DOORSTOP_INVOKE_DLL_PATH"]="test"; start.Environment["TCG_KEEP_TEST"]="preserved";
using(var process=System.Diagnostics.Process.Start(start)!) { var output=process.StandardOutput.ReadToEnd(); process.WaitForExit(); if(process.ExitCode!=0 || !output.Contains("preserved")) throw new Exception("Environnement de relancement incorrect : " + process.StandardError.ReadToEnd()); }
Console.WriteLine("OK : gardes Doorstop retirées du futur processus, autres variables conservées.");
}

var absentProfile = new TCG.SaveManager.Core.ModProfile { CampaignId=a, Mods=new[]{ new TCG.SaveManager.Core.ModDecision { Id="removed.mod", Choice=TCG.SaveManager.Core.ModChoice.Enabled, ReviewedVersion="1.0", ReviewedHash="old" } } };
absentProfile=TCG.SaveManager.Core.ModProfiles.Reconcile(absentProfile,Array.Empty<TCG.SaveManager.Core.InstalledMod>());
if(TCG.SaveManager.Core.ModProfiles.Validate(absentProfile,Array.Empty<TCG.SaveManager.Core.InstalledMod>()).Length==0) throw new Exception("Mod absent actif ignoré.");
absentProfile.Mods[0].Choice=TCG.SaveManager.Core.ModChoice.Disabled;
if(TCG.SaveManager.Core.ModProfiles.Validate(absentProfile,Array.Empty<TCG.SaveManager.Core.InstalledMod>()).Length!=0) throw new Exception("Correction du mod absent refusée.");
var returned = new TCG.SaveManager.Core.InstalledMod { Id="removed.mod", Version="1.0", Sha256="old" };
if(TCG.SaveManager.Core.ModProfiles.Reconcile(absentProfile,new[]{returned}).Mods[0].Choice!=TCG.SaveManager.Core.ModChoice.Disabled) throw new Exception("Retour du mod perd la décision.");
returned.Sha256="changed";
var returnedChanged=TCG.SaveManager.Core.ModProfiles.Reconcile(absentProfile,new[]{returned}).Mods[0];
if(returnedChanged.Choice!=TCG.SaveManager.Core.ModChoice.Disabled || returnedChanged.UpdatedFrom=="") throw new Exception("Mod revenu modifié : choix perdu ou non signalé.");
Console.WriteLine("OK : mod retiré corrigeable, retour identique mémorisé, retour modifié signalé avec son choix.");

var displayNames=new System.Collections.Generic.Dictionary<string,string>{["mod.a"]="Shop C",["mod.a.extra"]="Upstairs",["other"]="mod.a"};
if(ProfileMessages.Describe("Dépendance requise : mod.a.extra → mod.a",displayNames)!="Dépendance requise : Upstairs → Shop C") throw new Exception("Noms de mods incorrects.");
if(ProfileMessages.Describe("Mod actif absent : unknown.mod",displayNames)!="Mod actif absent : unknown.mod") throw new Exception("Identifiant inconnu altéré.");
if(ProfileMessages.Describe("other",displayNames)!="mod.a") throw new Exception("Remplacement de nom en cascade.");
Console.WriteLine("OK : noms lisibles, identifiants proches distincts, noms inconnus conservés.");
// 1.0.1 : mod file hashes are remembered by size and date, and stay correct when a file changes.
var cacheRoot = Path.Combine(Path.GetTempPath(), "TCG-hash-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(cacheRoot);
var cacheFile = Path.Combine(cacheRoot, "mod-hash-cache.txt");
var dll = Path.Combine(cacheRoot, "Mod.dll");
File.WriteAllBytes(dll, System.Text.Encoding.UTF8.GetBytes("version 1"));
string Sha(string f) { using (var s = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(f))).Replace("-", ""); }
var cache = new FileHashCache(cacheFile);
if (cache.Hash(dll) != Sha(dll) || cache.Computed != 1) throw new Exception("Empreinte incorrecte.");
if (cache.Hash(dll) != Sha(dll) || cache.Computed != 1) throw new Exception("Fichier inchangé relu.");
cache.Save();
var reloaded = new FileHashCache(cacheFile);
if (reloaded.Hash(dll) != Sha(dll) || reloaded.Computed != 0) throw new Exception("Cache non conservé entre deux lancements.");
File.WriteAllBytes(dll, System.Text.Encoding.UTF8.GetBytes("version 2, longer"));
if (reloaded.Hash(dll) != Sha(dll) || reloaded.Computed != 1) throw new Exception("Mod mis à jour : ancienne empreinte.");
var stamp = File.GetLastWriteTimeUtc(dll);
File.WriteAllBytes(dll, System.Text.Encoding.UTF8.GetBytes("version 3, longer"));
File.SetLastWriteTimeUtc(dll, stamp.AddSeconds(5));
if (reloaded.Hash(dll) != Sha(dll) || reloaded.Computed != 2) throw new Exception("Même taille, autre date : ancienne empreinte.");
reloaded.Save();
File.WriteAllText(cacheFile, "garbage\n1|2|zz|" + dll + "\n");
var damaged = new FileHashCache(cacheFile);
if (damaged.Hash(dll) != Sha(dll) || damaged.Computed != 1) throw new Exception("Cache abîmé utilisé.");
File.Delete(dll); damaged.Save();
if (File.ReadAllText(cacheFile).Contains("Mod.dll")) throw new Exception("Mod supprimé gardé dans le cache.");
File.Delete(cacheFile); Directory.Delete(cacheRoot);
Console.WriteLine("OK : empreintes des mods mémorisées, recalculées si taille ou date changent, cache abîmé ignoré.");
