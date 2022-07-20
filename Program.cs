using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace RelativizeIncludes
{
    internal partial class Program
    {
        private static readonly string[] SourceExtensions = { ".h", ".hpp", ".cpp" };
        private const bool UseTestDirectory = true;

        static void Main(string[] args)
        {
            ParseAndRun(args);
            //TestRelativePaths();
        }

        private static void ParseAndRun(string[] args)
        {
            string rootDirectoryPath = Directory.GetCurrentDirectory();
            if (args.Length > 0)
            {
                rootDirectoryPath = args[0];
            }
            Console.WriteLine($"Root directory: {rootDirectoryPath}");

            // Optionally write the output to a test path
            string testDirectoryPath = rootDirectoryPath;
            if (UseTestDirectory)
            {
                testDirectoryPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Test");
                Directory.CreateDirectory(testDirectoryPath);
                foreach (var testFilePath in Directory.GetFiles(testDirectoryPath, "*.*", SearchOption.AllDirectories))
                {
                    File.Delete(testFilePath);
                }
            }

            var rootDirInfo = new DirectoryInfo(rootDirectoryPath);
            var sourceFiles = rootDirInfo.GetFiles("*.*", SearchOption.AllDirectories).Where(f => SourceExtensions.Contains(f.Extension)).ToList();

            Console.WriteLine($"{sourceFiles.Count} source files found...");

            var pattern = @"#include\s+""(.*)""";
            var rgx = new Regex(pattern);

            var evaluator = new IncludeMatchEvaluator(sourceFiles);

            foreach (var file in sourceFiles)
            {
                Console.WriteLine($"File: {file}");

                var fileText = string.Empty;
                using (var reader = new StreamReader(file.OpenRead()))
                {
                    fileText = reader.ReadToEnd();
                }

                evaluator.CurrentFilePath = file.FullName;
                var replacedText = rgx.Replace(fileText, evaluator.Evaluate);

                var destinationPath = Path.Combine(testDirectoryPath, PathHelper.GetRelativePath(rootDirectoryPath, file.FullName));
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.WriteAllText(destinationPath, replacedText);
            }
        }

        private static void TestRelativePaths()
        {
            var from = @"C:\Folder1\Folder2\Test.txt";
            var to = @"C:\Folder1\Test2.txt";
            Console.WriteLine($"\"{from}\" -> \"{to}\": {PathHelper.GetRelativePath(from, to)}");

            from = @"C:\Folder1\Test2.txt";
            to = @"C:\Folder1\Folder2\Test.txt";
            Console.WriteLine($"\"{from}\" -> \"{to}\": {PathHelper.GetRelativePath(from, to)}");

            from = @"C:\Folder1\Folder2\";
            to = @"C:\FolderA\FolderB\";
            Console.WriteLine($"\"{from}\" -> \"{to}\": {PathHelper.GetRelativePath(from, to)}");

            from = @"C:\FolderC\FolderD\Test1.txt";
            to = @"C:\Folder1\";
            Console.WriteLine($"\"{from}\" -> \"{to}\": {PathHelper.GetRelativePath(from, to)}");
        }
    }
}
