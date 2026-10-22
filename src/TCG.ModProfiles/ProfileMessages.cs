using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace TCG.ModProfiles.Shared
{
    public static class ProfileMessages
    {
        public static string Describe(string message, IReadOnlyDictionary<string, string> names)
        {
            if (names.Count == 0) return message;
            var pattern = "(?<![\\w.])(?:" + string.Join("|", names.Keys.OrderByDescending(id => id.Length).Select(Regex.Escape)) + ")(?![\\w.])";
            return Regex.Replace(message, pattern, match => names[match.Value]);
        }
    }
}
