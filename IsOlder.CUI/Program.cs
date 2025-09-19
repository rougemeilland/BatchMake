using System;
using System.Collections.Generic;
using System.Linq;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Console;

namespace IsOlder.CUI
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var argsWithoutOptions = new List<string>();
            var verbose = false;
            foreach (var arg in args)
            {
                if (arg is "-v" or "--verbose")
                    verbose = true;
                else
                    argsWithoutOptions.Add(arg);
            }

            try
            {
                if (argsWithoutOptions.Count < 2)
                    throw new ApplicationException("Too few arguments.");

                var targetFile = GetFilePath(argsWithoutOptions.First());
                if (!targetFile.Exists)
                {
                    if (verbose)
                        TinyConsole.WriteLog(LogCategory.Information, $"Target file does not exist.: \"{targetFile}\"");
                    return 1;
                }

                var sourceFiles =
                    argsWithoutOptions
                    .Skip(1)
                    .Select(arg =>
                    {
                        try
                        {
                            return GetFilePath(arg);
                        }
                        catch (Exception ex)
                        {
                            throw new ApplicationException($"Invalid path name.: \"{arg}\"", ex);
                        }
                    })
                    .ToList();
                foreach (var sourceFile in sourceFiles)
                {
                    if (!sourceFile.Exists)
                        throw new ApplicationException($"Source file does not exist.: \"{sourceFile}\"");
                    var isOldTarget = targetFile.LastWriteTimeUtc <= sourceFile.LastWriteTimeUtc;
                    if (verbose)
                    {
                        if (isOldTarget)
                            TinyConsole.WriteLog(LogCategory.Information, $"Source file is newer than target file.: target=\"{targetFile}\", source=\"{sourceFile}\"");
                        else
                            TinyConsole.WriteLog(LogCategory.Information, $"Target file is newer than source file.: target=\"{targetFile}\", source=\"{sourceFile}\"");
                    }

                    if (isOldTarget)
                        return 1;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static FilePath GetFilePath(string path)
        {
            try
            {
                return new FilePath(path);

            }
            catch (Exception ex)
            {
                throw new Exception($"\"{path}\" is not a valid path name.", ex);
            }
        }
    }
}
