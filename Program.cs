using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CommandLine;
using CommandLine.Text;

namespace RelativizeIncludes
{
    internal class Program
    {
        private static readonly string[] SourceExtensions = {".h", ".hpp", ".cpp", ".c++", ".c"};
        private const int MaxPathLength = 260;

        class Options
        {
            public static IEnumerable<Example> Examples
            {
                get
                {
                    var examplePath = @"C:\Source\Project";
                    return new List<Example>()
                    {
                        new Example($"Read and modify all source files from {examplePath} to use relative #include paths", new Options() {RootDirectoryPath = examplePath})
                    };
                }
            }

            [Option('p', "path", Required = false, Default = null, HelpText = "The root path of the directory containing all source files to process. If no path is specified, the current directory is used.")]
            public string RootDirectoryPath { get; set; }

            [Option('d', "dry-run", Required = false, HelpText = "Perform a dry run and print out potential replacements.")]
            public bool DryRun { get; set; }

            [Option('s', "staging", Required = false, Default = null, HelpText = "Copy modified source files to the specified path. This should be an empty directory.")]
            public string StagingDirectoryPath { get; set; }
        }

        static void Main(string[] args)
        {
            // TODO: Validate args
            try
            {
                var parseResult = Parser.Default.ParseArguments<Options>(args);

                if (parseResult.Errors.Any())
                {
                    return;
                }

                Run(parseResult.Value);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERROR:");
                Console.Error.WriteLine(e.ToString());
                throw;
            }
        }

        private static void Run(Options options)
        {
            string rootDirectoryPath = options.RootDirectoryPath;
            if (string.IsNullOrWhiteSpace(rootDirectoryPath))
            {
                rootDirectoryPath = Directory.GetCurrentDirectory();
            }

            Console.WriteLine($"Root directory: {rootDirectoryPath}");

            string destinationDirectoryPath = rootDirectoryPath;
            if (!string.IsNullOrEmpty(options.StagingDirectoryPath))
            {
                if (Directory.EnumerateFileSystemEntries(options.StagingDirectoryPath).Any())
                {
                    Console.Error.WriteLine("ERROR: Staging directory is not empty.");
                    return;
                }

                destinationDirectoryPath = options.StagingDirectoryPath;
            }

            var rootDirInfo = new DirectoryInfo(rootDirectoryPath);
            var sourceFiles = rootDirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                .Where(f => SourceExtensions.Contains(f.Extension)).ToList();

            Console.WriteLine($"{sourceFiles.Count} source files found...");

            if (options.DryRun)
            {
                Console.WriteLine("Performing dry run...");
            }

            var pattern = @"#include\s+""(.*)""";
            var rgx = new Regex(pattern);

            foreach (var file in sourceFiles)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Including file");
                Console.ResetColor();
                Console.WriteLine($": {file.FullName}");

                var fileText = string.Empty;
                using (var reader = new StreamReader(file.OpenRead()))
                {
                    fileText = reader.ReadToEnd();
                }

                var replacedText = rgx.Replace(fileText, m => EvaluateMatch(m, file, sourceFiles));

                if (!options.DryRun && !ReferenceEquals(replacedText, fileText))
                {
                    var destinationPath = Path.Combine(destinationDirectoryPath, GetRelativePath(rootDirectoryPath, file.FullName));
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.WriteAllText(destinationPath, replacedText);
                }
            }
        }

        private static string EvaluateMatch(Match match, FileInfo includingFile, IEnumerable<FileInfo> sourceFiles)
        {
            Console.Write('\t');
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("Match");
            Console.ResetColor();
            Console.WriteLine($": {match.Value}");

            if (match.Groups.Count <= 1)
            {
                return match.Value;
            }

            if (includingFile == null)
            {
                Console.WriteLine($"\t\tCurrent file is not set. Skipping...");
                return match.Value;
            }

            var headerPath = match.Groups[1].Value;
            var headerFileName = Path.GetFileName(headerPath);

            var matchingHeaderFiles = sourceFiles.Where(f => f.Name == headerFileName).ToList();
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

            string replacement;
            if (replacementHeaderFile.DirectoryName == includingFile.DirectoryName)
            {
                // If the replacement header is in the same directory, we don't
                // need to find the relative path
                replacement = $"#include \"{replacementHeaderFile.Name}\"";
            }
            else
            {
                var relPath = GetRelativePath(includingFile.FullName, replacementHeaderFile.FullName);

                // Remove any leading ".\" if the replacement header shares a path with the including file.     
                if (relPath.StartsWith($".{Path.DirectorySeparatorChar}"))
                {
                    relPath = relPath.Substring(2);
                }

                replacement = $"#include \"{relPath.Replace(Path.DirectorySeparatorChar, '/')}\"";
            }

            if (replacement == match.Value)
            {
                Console.WriteLine("\t\tNo replacement required.");

                // Return the same reference since no replacement is needed
                return match.Value;
            }

            Console.Write($"\t\t");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(match.Value);
            Console.ResetColor();
            Console.Write(" -> ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(replacement);
            Console.ResetColor();
            Console.WriteLine();

            return replacement;
        }


        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        private static extern bool PathRelativePathTo([Out] StringBuilder pszPath, [In] string pszFrom, [In] FileAttributes dwAttrFrom, [In] string pszTo, [In] FileAttributes dwAttrTo);
        private static string GetRelativePath(string fromPath, string toPath)
        {
            var sb = new StringBuilder(MaxPathLength);

            if (PathRelativePathTo(sb, fromPath, FileAttributes.Normal, toPath, FileAttributes.Normal))
            {
                return sb.ToString();
            }

            throw new InvalidOperationException($"Unable to get relative path from \"{fromPath}\" to \"{toPath}\"))");
        }
    }
}