using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;

namespace RelativizeIncludes
{
    internal partial class Program
    {
        private class IncludeMatchEvaluator
        {
            public string CurrentFilePath = string.Empty;

            private readonly IEnumerable<FileInfo> _sourceFiles;

            public IncludeMatchEvaluator(IEnumerable<FileInfo> sourceFiles)
            {
                _sourceFiles = sourceFiles;
            }

            public string Evaluate(Match match)
            {
                Console.WriteLine($"\tMatch: {match.Value}");

                if (match.Groups.Count <= 1)
                {
                    return match.Value;
                }

                var headerPath = match.Groups[1].Value;
                var headerFileName = Path.GetFileName(headerPath);

                var matchingHeaderFiles = _sourceFiles.Where(f => f.Name == headerFileName).ToList();
                if (matchingHeaderFiles.Count == 0)
                {
                    Console.WriteLine("\t\tNo matching headers found...");
                    return match.Value;
                }

                var replacementHeaderFile = matchingHeaderFiles.First();
                if (matchingHeaderFiles.Count > 1)
                {
                    Console.WriteLine("\ttFound multiple matching headers:");
                    var headerCount = 1;
                    foreach (var header in matchingHeaderFiles)
                    {
                        Console.WriteLine($"\t\t\t[{headerCount++}]: {header.FullName}");
                    }

                    Console.WriteLine("\t\tChoose header (enter number 1 or greater):");
                    var response = Console.ReadLine();

                    if (int.TryParse(response, out var headerNumber) && 
                        headerNumber > 0 &&
                        headerNumber <= matchingHeaderFiles.Count)
                    {
                        replacementHeaderFile = matchingHeaderFiles[headerNumber - 1];
                    }
                    else
                    {
                        Console.WriteLine("\t\tChoosing the first result...");
                    }
                }

                var relPath = PathHelper.GetRelativePath(CurrentFilePath, replacementHeaderFile.FullName);

                if (relPath.StartsWith(".\\") || relPath.StartsWith("./"))
                {
                    return $"#include \"{relPath.Substring(2)}\"";
                }

                return $"#include \"{relPath}\"";
            }
        }
    }
}
