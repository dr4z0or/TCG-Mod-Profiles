using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace TCG.ModProfiles.Shared
{
    // 1.0.1 : SHA-256 of mod files, remembered by path, size and modification date. Before, every
    // inventory (game start, each Continue, each Mods screen) re-read every plugin DLL in full: with
    // Collection Tracker (750+ MB) that froze the game for several seconds. A file whose size or date
    // changes is hashed again; the cache lives next to the shops (mod-hash-cache.txt).
    public sealed class FileHashCache
    {
        private sealed class Entry { public long Length, Ticks; public string Sha256 = ""; }
        private readonly string path;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private bool dirty;
        public int Computed { get; private set; }

        public FileHashCache(string path)
        {
            this.path = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    // length|ticks|sha256|full path (the path may contain '|' on no supported system, but stays last).
                    var parts = line.Split(new[] { '|' }, 4);
                    if (parts.Length != 4 || !long.TryParse(parts[0], out var length) || !long.TryParse(parts[1], out var ticks)
                        || parts[2].Length != 64 || !parts[2].All(Uri.IsHexDigit)) continue;
                    entries[parts[3]] = new Entry { Length = length, Ticks = ticks, Sha256 = parts[2].ToUpperInvariant() };
                }
            }
            catch (IOException) { entries.Clear(); }
            catch (UnauthorizedAccessException) { entries.Clear(); }
        }

        public string Hash(string file)
        {
            var info = new FileInfo(file);
            var key = info.FullName;
            if (entries.TryGetValue(key, out var known) && known.Length == info.Length && known.Ticks == info.LastWriteTimeUtc.Ticks)
                return known.Sha256;
            long length = info.Length, ticks = info.LastWriteTimeUtc.Ticks;
            string sha;
            using (var algorithm = SHA256.Create())
            using (var stream = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                sha = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
            Computed++;
            // A file written while it was read keeps no cache entry: it is hashed again next time.
            info.Refresh();
            if (info.Exists && info.Length == length && info.LastWriteTimeUtc.Ticks == ticks)
            {
                entries[key] = new Entry { Length = length, Ticks = ticks, Sha256 = sha };
                dirty = true;
            }
            else entries.Remove(key);
            return sha;
        }

        // Forgets the files that no longer exist, then writes the cache if it changed. Never throws:
        // a cache that cannot be written only means hashing again next time.
        public void Save()
        {
            foreach (var key in entries.Keys.Where(k => !File.Exists(k)).ToList()) { entries.Remove(key); dirty = true; }
            if (!dirty) return;
            var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(pending, entries.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(p => p.Value.Length + "|" + p.Value.Ticks + "|" + p.Value.Sha256 + "|" + p.Key).ToArray());
                if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
                dirty = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { try { if (File.Exists(pending)) File.Delete(pending); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
