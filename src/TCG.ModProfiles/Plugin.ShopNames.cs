using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using TCG.SaveManager.Core;

using static TCG.Shared.Lang;
namespace TCG.ModProfiles
{
    public sealed partial class Plugin
    {
        private readonly Dictionary<string,string> shopNames=new Dictionary<string,string>();
        private readonly Dictionary<string,string> snapshotNames=new Dictionary<string,string>();
        private readonly Dictionary<string,string> shopModes=new Dictionary<string,string>();
        private bool shopNamesReady;
        [Serializable] private sealed class ShopNameData { public string m_PlayerName=""; }
        private static void RefreshShopNames(object __instance)
        {
            var plugin=integration;if(plugin==null)return;
            plugin.shopNamesReady=true;
            try
            {
                var store=new LabStore(plugin.campaignsRoot);
                foreach(var campaign in (Campaign[]?)plugin.Read(__instance,"campaigns") ?? Array.Empty<Campaign>())
                {
                    var mode=T("Classique");
                    var modeFile=Path.Combine(plugin.campaignsRoot,campaign.Id,"game-mode.xml");
                    if(File.Exists(modeFile))
                    {
                        try
                        {
                            using(var reader=XmlReader.Create(modeFile,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))
                                if(reader.ReadToFollowing("Mode"))
                                {
                                    var value=reader.ReadElementContentAsString();
                                    mode=value=="CardsOnly"?T("Cartes uniquement"):value=="Custom"?T("Personnalisé"):value=="Classic"?T("Classique"):T("Mode inconnu");
                                    if(value=="Custom" && reader.ReadToFollowing("PresetName"))
                                    {
                                        var preset=reader.ReadElementContentAsString().Trim();
                                        if(preset!="")mode+=" ("+preset+")";
                                    }
                                }
                        }
                        catch(Exception ex){mode=T("Mode indisponible");plugin.Logger.LogWarning(ex.Message);}
                    }
                    plugin.shopModes[campaign.Id]=mode;
                    var snapshot=new[]{store.Latest(campaign.Id,"auto"),store.Latest(campaign.Id,"manual")}.Where(p=>p!=null).OrderByDescending(p=>Path.GetFileName(p),StringComparer.Ordinal).FirstOrDefault();
                    if(snapshot==null){plugin.shopNames[campaign.Id]=T("Boutique pas encore nommée");continue;}
                    if(!plugin.snapshotNames.TryGetValue(snapshot,out var caption))
                    {
                        var file=Directory.GetFiles(snapshot,"*Release*.json").FirstOrDefault(p=>p.IndexOf("Backup",StringComparison.OrdinalIgnoreCase)<0);
                        caption=T("Nom en jeu indisponible");
                        if(file!=null)
                        {
                            var name=JsonUtility.FromJson<ShopNameData>(File.ReadAllText(file))?.m_PlayerName;
                            if(!string.IsNullOrWhiteSpace(name))caption=F("Boutique : {0}",name!.Replace('\n',' ').Replace('\r',' '));
                        }
                        plugin.snapshotNames[snapshot]=caption;
                    }
                    plugin.shopNames[campaign.Id]=caption;
                }
            }
            catch(Exception ex){plugin.Logger.LogWarning(F("Lecture des noms de boutiques : {0}",ex.Message));}
        }
    }
}

