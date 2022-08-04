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
                        new Example(
                            $"Read and modify all source files from {examplePath} to use relative #include paths",
                            new Options() {RootDirectoryPath = examplePath})
                    };
                }
            }

            [Option('p', "path", Required = false, Default = null,
                HelpText =
                    "The root path of the directory containing all source files to process. If no path is specified, the current directory is used.")]
            public string RootDirectoryPath { get; set; }

            [Option('a', "additional-include-dir-file", Required = false, Default = null,
                HelpText = "Path to a text file containing additional include directory paths")]
            public string AdditionalIncludeDirectoriesFilePath { get; set; }

            [Option('d', "dry-run", Required = false, Default = false,
                HelpText = "Perform a dry run and print out potential replacements.")]
            public bool DryRun { get; set; }

            [Option('s', "staging", Required = false, Default = null,
                HelpText = "Copy modified source files to the specified path. This should be an empty directory.")]
            public string StagingDirectoryPath { get; set; }

            [Option('i', "ignore-case", Required = false, Default = false,
                HelpText = "Ignore case when searching for matching header files by name.")]
            public bool IgnoreCase { get; set; }

            [Option('b', "use-brackets", Required = false, Default = false,
                HelpText = "Use angle brackets (<>) in addition to apostrophes for matching include directives.")]
            public bool UseBrackets { get; set; }
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

            var additionalIncludeDirectoryPaths = new List<string>();
            var additionalIncludeFiles = new List<FileInfo>();
            if (!string.IsNullOrWhiteSpace(options.AdditionalIncludeDirectoriesFilePath))
            {
                if (!File.Exists(options.AdditionalIncludeDirectoriesFilePath) ||
                    options.AdditionalIncludeDirectoriesFilePath.Any(c => Path.GetInvalidPathChars().Contains(c)))
                {
                    Console.Error.WriteLine("ERROR: Invalid path to additional include directories file.");
                    return;
                }

                using (var reader = new StreamReader(options.AdditionalIncludeDirectoriesFilePath))
                {
                    string fileLine;
                    while ((fileLine = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(fileLine) || fileLine.Trim().StartsWith("#"))
                        {
                            continue;
                        }

                        additionalIncludeDirectoryPaths.Add(Path.GetFullPath(fileLine) + Path.DirectorySeparatorChar);
                    }
                }

                foreach (var path in additionalIncludeDirectoryPaths)
                {
                    if (!Directory.Exists(path) || path.Any(c => Path.GetInvalidPathChars().Contains(c)))
                    {
                        continue;
                    }

                    var includeDirInfo = new DirectoryInfo(path);
                    additionalIncludeFiles.AddRange(includeDirInfo.GetFiles("*.*", SearchOption.AllDirectories));
                }
            }

            Console.WriteLine("Include paths:");
            foreach (var path in additionalIncludeDirectoryPaths)
            {
                Console.WriteLine($"\t{path}");
            }

            Console.WriteLine($"Total include files: {additionalIncludeFiles.Count}");

            if (!string.IsNullOrEmpty(options.StagingDirectoryPath))
            {
                if (Directory.EnumerateFileSystemEntries(options.StagingDirectoryPath).Any())
                {
                    Console.Error.WriteLine("ERROR: Staging directory is not empty.");
                    return;
                }
            }

            var rootDirInfo = new DirectoryInfo(rootDirectoryPath);
            var sourceFiles = rootDirInfo.GetFiles("*.*", SearchOption.AllDirectories)
                .Where(f => SourceExtensions.Contains(f.Extension)).ToList();

            Console.WriteLine($"{sourceFiles.Count} source files found...");

            if (options.DryRun)
            {
                Console.WriteLine("Performing dry run...");
            }

            // NOTE: Bracket support will match things like "#include <string>",
            // so make sure that the include paths (using the -a flag) contain
            // the C++ standard library. Otherwise, there is a risk of the
            // header resolving incorrectly (e.g. "SomeLib/string.h" vs C++ STL
            // <string>)
            var rgx = new Regex(options.UseBrackets ? @"#\s*include\s*([""<])(.*)([>""])" : @"#\s*include\s*("")(.*)("")");
            var replacementCount = 0;
            var replacementFileCount = 0;
            foreach (var file in sourceFiles)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("Including file");
                Console.ResetColor();
                Console.WriteLine($": {file.FullName}");

                string fileText;
                using (var reader = new StreamReader(file.OpenRead()))
                {
                    fileText = reader.ReadToEnd();
                }

                var replacedText = rgx.Replace(fileText,
                    m => EvaluateMatch(m, file, sourceFiles, additionalIncludeFiles,
                        additionalIncludeDirectoryPaths, options.IgnoreCase, ref replacementCount));

                if (ReferenceEquals(replacedText, fileText))
                {
                    // No replacements have been made if there is reference
                    // equality between the replaced and original strings.

                    continue;
                }

                if (!options.DryRun)
                {
                    string destinationFilePath;
                    if (!string.IsNullOrEmpty(options.StagingDirectoryPath))
                    {
                        destinationFilePath = Path.Combine(options.StagingDirectoryPath,
                            GetRelativePath(rootDirectoryPath, file.FullName));
                    }
                    else
                    {
                        destinationFilePath = file.FullName;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath));
                    File.WriteAllText(destinationFilePath, replacedText);
                }

                replacementFileCount++;
            }

            Console.WriteLine();
            Console.WriteLine($"Replaced {replacementCount} include directives in {replacementFileCount} files.");
        }

        private static string EvaluateMatch(Match match, FileInfo includingFile,
            IEnumerable<FileInfo> sourceFiles, IEnumerable<FileInfo> includeFiles,
            IEnumerable<string> includePaths, bool ignoreCase, ref int replacementCount)
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

            var includeTokenLeft = match.Groups[1].Value;
            var headerPath = match.Groups[2].Value;
            var includeTokenRight = match.Groups[3].Value;

            var headerFileName = Path.GetFileName(headerPath);

            bool Selector(FileInfo f) => string.Equals(f.Name, headerFileName,
                ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture);

            var matchingHeaderFiles = sourceFiles.Where(Selector).ToList();
            matchingHeaderFiles.AddRange(includeFiles.Where(Selector));

            if (matchingHeaderFiles.Count == 0)
            {
                Console.WriteLine("\t\tNo matching headers found...");
                return match.Value;
            }

            var replacementHeaderFile = matchingHeaderFiles.First();
            if (matchingHeaderFiles.Count > 1)
            {
                Console.WriteLine("\t\tFound multiple matching headers:");
                var headerCount = 1;
                foreach (var header in matchingHeaderFiles)
                {
                    Console.WriteLine($"\t\t\t[{headerCount++}]: {header.FullName}");
                }

                string response = null;
                int headerNumber;
                while (!int.TryParse(response, out headerNumber) || headerNumber < 0 ||
                       headerNumber > matchingHeaderFiles.Count)
                {
                    Console.Write("\t\tChoose header (enter number 1 or greater, 0 to skip): ");
                    response = Console.ReadLine();
                }

                if (headerNumber == 0)
                {
                    Console.WriteLine("\t\tSkipping...");

                    // Return the same reference since no replacement is needed

                    return match.Value;
                }

                replacementHeaderFile = matchingHeaderFiles[headerNumber - 1];
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
                // Find the relative path to the replacement header from all
                // include directories first. If multiple include paths match,
                // pick the more specific one. If an include path is not found,
                // find the relative path from the including file.
                var relPath = includePaths.Where(p => replacementHeaderFile.FullName.StartsWith(p)).Max();

                if (string.IsNullOrEmpty(relPath))
                {
                    relPath = GetRelativePath(includingFile.FullName, replacementHeaderFile.FullName);
                }

                // Remove any leading ".\" if the replacement header shares a
                // path with the including file.
                if (relPath.StartsWith($".{Path.DirectorySeparatorChar}"))
                {
                    relPath = relPath.Substring(2);
                }

                replacement =
                    $"#include {includeTokenLeft}{relPath.Replace(Path.DirectorySeparatorChar, '/')}{includeTokenRight}";
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

            replacementCount++;

            return replacement;
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        private static extern bool PathRelativePathTo([Out] StringBuilder pszPath, [In] string pszFrom,
            [In] FileAttributes dwAttrFrom, [In] string pszTo, [In] FileAttributes dwAttrTo);

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