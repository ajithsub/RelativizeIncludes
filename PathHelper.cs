using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace RelativizeIncludes
{
    internal static class PathHelper
    {
        private const int MAX_PATH = 260;

        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        static extern bool PathRelativePathTo([Out] StringBuilder pszPath, [In] string pszFrom, [In] FileAttributes dwAttrFrom, [In] string pszTo, [In] FileAttributes dwAttrTo);

        public static string GetRelativePath(string fromPath, string toPath)
        {
            var sb = new StringBuilder(MAX_PATH);
            
            if (PathRelativePathTo(sb, fromPath, FileAttributes.Normal, toPath, FileAttributes.Normal))
            {
                return sb.ToString();
            }
            else
            {
                throw new InvalidOperationException($"Unable to get relative path from \"{fromPath}\" to \"{toPath}\"))");
            }
        }
    }
}
