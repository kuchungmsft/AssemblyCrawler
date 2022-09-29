using System;
using System.Collections.Generic;
using System.IO;

namespace AssemblyCrawler
{
    internal static class FileInfoExtension
    {
        private static readonly HashSet<string> SupportedLanagueSets = new ()
        { "1028", "1029", "1031", "1033", "1036", "1040", "1041", "1042", "1045", "1046", "1049", "1055", "2052", "3082" };

        public static bool IsResource(this FileInfo file)
        {
            return file.Name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase) ||
                SupportedLanagueSets.Contains(Directory.GetParent(file.FullName)?.Name ?? string.Empty);
        }
    }
}