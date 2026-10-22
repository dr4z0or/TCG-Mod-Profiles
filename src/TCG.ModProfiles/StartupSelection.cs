using System;
using System.IO;
using System.Xml;
using static TCG.Shared.Lang;
namespace TCG.ModProfiles.Shared
{
    public static class StartupSelection
    {
        public static void Save(string root, string campaignId)
        {
            Validate(root, campaignId);
            var path = Path.Combine(root, "mod-profile-selection.xml");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var writer = XmlWriter.Create(stream)) { writer.WriteElementString("CampaignId", campaignId); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static string Read(string root)
        {
            using (var reader = XmlReader.Create(Path.Combine(root, "mod-profile-selection.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                var doc = new XmlDocument { XmlResolver = null }; doc.Load(reader);
                if (doc.DocumentElement?.Name != "CampaignId") throw new IOException(T("Sélection inconnue."));
                var id = doc.DocumentElement.InnerText; Validate(root, id); return id;
            }
        }
        private static void Validate(string root, string id)
        {
            if (!Path.IsPathRooted(root) || !Guid.TryParseExact(id, "N", out _)) throw new IOException(T("Sélection invalide."));
            var folder = Path.Combine(root, "campaigns", id);
            for (var directory = new DirectoryInfo(folder); directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(T("Lien refusé."));
            if (!File.Exists(Path.Combine(root, "campaigns", ".tcg-save-lab")) || !File.Exists(Path.Combine(folder, "campaign.xml"))) throw new IOException(T("Boutique inconnue."));
        }
    }
}
