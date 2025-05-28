using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Console;

namespace BatchMake.CUI2
{
    internal partial class Program
    {
        private class Dependency
        {
            private class PathCollection
                : IReadOnlyArray<FilePath>
            {
                private readonly IList<FilePath> _files;

                public PathCollection()
                {
                    _files = [];
                }

                public FilePath this[int index] => _files[index];
                public int Length => _files.Count;
                public void Add(FilePath file) => _files.Add(file);
                public IEnumerator<FilePath> GetEnumerator() => _files.GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }

            private readonly PathCollection _targets;
            private readonly PathCollection _sources;

            public Dependency()
            {
                _targets = new PathCollection();
                _sources = new PathCollection();
            }

            public IReadOnlyArray<FilePath> Targets => _targets;
            public IReadOnlyArray<FilePath> Sources => _sources;
            public void AddTarget(FilePath target) => _targets.Add(target);
            public void AddSource(FilePath source) => _sources.Add(source);
        }

        private class CommandSpec
        {
            public CommandSpec(string commandLine, ISequentialInputByteStream? standardInput, ISequentialOutputByteStream? standardOutput, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput)
            {
                CommandLine = commandLine;
                StandardInput = standardInput;
                StandardOutput = standardOutput;
                IsPipedFromPreviousCommand = isPipedFromPreviousCommand;
                IsPipedToNextCommand = isPipedToNextCommand;
                FileRedirectedToStandardInput = fileRedirectedToStandardInput;
                FileRedirectedToStandardOutput = fileRedirectedToStandardOutput;
            }

            public string CommandLine { get; }
            public ISequentialInputByteStream? StandardInput { get; }
            public ISequentialOutputByteStream? StandardOutput { get; }
            public bool IsPipedFromPreviousCommand { get; }
            public bool IsPipedToNextCommand { get; }
            public FilePath? FileRedirectedToStandardInput { get; }
            public FilePath? FileRedirectedToStandardOutput { get; }

            public async Task Start()
            {
                var firstToken = CommandLine.SplitCommandLineArguments().Take(1).ToArray();
                if (firstToken.Length <= 0)
                    throw new Exception("An empty command line is specified.");
                var commandName = firstToken[0].element;
                var commandPath =
                    ProcessUtility.WhereIs(commandName)
                    ?? throw new Exception($"Executable file not found.: \"{commandName}\"");
                var commandParameter = CommandLine[firstToken[0].end..].TrimStart();
                var startInfo = new ProcessStartInfo
                {
                    FileName = ShortenFilePathNames(new FilePath(commandPath)),
                    Arguments = commandParameter,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardInput = StandardInput is not null,
                    RedirectStandardOutput = StandardOutput is not null,
                    RedirectStandardError = true,
                };
                var process = StartProcess(startInfo, CommandLine);
                try
                {
                    var task1 =
                        Task.Run(
                            () =>
                            {
                                if (StandardInput is not null)
                                {
                                    try
                                    {
                                        using var outStream = process.StandardInput.BaseStream.AsOutputByteStream();
                                        CopyByteStream(StandardInput, outStream);
                                    }
                                    finally
                                    {
                                        StandardInput.Dispose();
                                    }
                                }
                            });
                    var task2 =
                        Task.Run(
                            () =>
                            {
                                if (StandardOutput is not null)
                                {
                                    try
                                    {
                                        using var inStream = process.StandardOutput.BaseStream.AsInputByteStream();
                                        CopyByteStream(inStream, StandardOutput);
                                    }
                                    finally
                                    {
                                        StandardOutput.Dispose();
                                    }
                                }
                            });
                    var task3 =
                        Task.Run(
                            () =>
                            {
                                using var inStream = process.StandardError.BaseStream.AsInputByteStream();
                                using var outStream = TinyConsole.OpenStandardError();
                                CopyByteStream(inStream, outStream);
                            });

                    await Task.WhenAll(task1, task2, task3);
                    await process.WaitForExitAsync();
                    var exitCode = process.ExitCode;
                    if (exitCode != 0)
                        throw new Exception($"The process terminated abnormally.: exit-code={exitCode}, command-line=\"{CommandLine}\"");
                }
                finally
                {
                    process.Dispose();
                }

                static Process StartProcess(ProcessStartInfo startInfo, string commandLine)
                {
                    try
                    {
                        return
                            Process.Start(startInfo)
                            ?? throw new Exception($"Failed to create process.: command-line=\"{commandLine}\"");
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to start process.: command-line=\"{commandLine}\"", ex);
                    }
                }

                static void CopyByteStream(ISequentialInputByteStream inStream, ISequentialOutputByteStream outStream)
                {
                    Span<byte> buffer = stackalloc byte[1024];
                    while (true)
                    {
                        var length = inStream.Read(buffer);
                        if (length <= 0)
                            break;
                        outStream.WriteBytes(buffer[..length]);
                    }
                }
            }
        }

        private static readonly string _thisProgramName = typeof(Program).Assembly.GetAssemblyFileNameWithoutExtension();
        private static readonly char[] _pathDelimiters = new char[] { '/', '\\' };

        static int Main(string[] args)
        {
            var scriptFilePath = (FilePath?)null;
            var verbose = false;
            for (var index = 0; index < args.Length; ++index)
            {
                var arg = args[index];
                if (arg is "-v" or "--verbose")
                {
                    verbose = true;
                }
                else if (arg.StartsWith('-'))
                {
                    PrintErrorMessage($"A unknown argument is specified.: \"{arg}\"");
                    PrintUsage();
                    return 1;
                }
                else if (scriptFilePath is not null)
                {
                    PrintErrorMessage($"Multiple script files are specified.: \"{scriptFilePath.FullName}\", \"{arg}\"");
                    PrintUsage();
                    return 1;
                }
                else
                {
                    try
                    {
                        scriptFilePath = new FilePath(arg);
                        if (!scriptFilePath.Exists)
                        {
                            PrintErrorMessage($"The specified script file does not exist.: \"{scriptFilePath.FullName}\"");
                            PrintUsage();
                            return 1;
                        }
                    }
                    catch (Exception)
                    {
                        PrintErrorMessage($"The path name of the script file is invalid.: \"{arg}\"");
                        PrintUsage();
                        return 1;
                    }
                }
            }

            if (scriptFilePath is null)
            {
                PrintErrorMessage("The path name of the script file is not specified.");
                PrintUsage();
                return 1;
            }

            try
            {
                Environment.CurrentDirectory = scriptFilePath.Directory.FullName;
                DoProcess(scriptFilePath, verbose);
                return 0;
            }
            catch (Exception ex)
            {
                PrintException(ex);
                return 1;
            }
        }

        private static void DoProcess(FilePath scriptFile, bool verbose)
        {
            var lines =
                scriptFile.ReadAllLines()
                .ToArray()
                .AsReadOnlyMemory();
            var headerMarkerPos = FindHeaderMarker(lines);
            if (headerMarkerPos < 0)
                throw new Exception($"Header marker (e.g. \"# !bmake\") not found.: path=\"{scriptFile.FullName}\"");
            lines = lines[(headerMarkerPos + 1)..];

            var nextIndex = ParseDependency(lines, out var dependency);
            if (dependency.Targets.Length <= 0)
                throw new Exception($"No target specified.: path=\"{scriptFile.FullName}\"");
            if (dependency.Sources.Length <= 0)
                throw new Exception($"No source specified.: path=\"{scriptFile.FullName}\"");
            if (verbose)
            {
                var fileDescriptions = Array.Empty<string>().AsEnumerable();
                fileDescriptions =
                    fileDescriptions
                    .Append($"script: \"{scriptFile.FullName}\" ({scriptFile.LastWriteTimeUtc:o})");
                if (dependency.Targets.Length == 1)
                {
                    var file = dependency.Targets[0];
                    fileDescriptions =
                        fileDescriptions
                        .Append($"target: \"{file.FullName}\" ({(file.Exists ? file.LastWriteTimeUtc.ToString("o") : "Not Exists")})");
                }
                else
                {
                    fileDescriptions =
                        fileDescriptions
                        .Concat(
                            Enumerable.Range(0, dependency.Targets.Length)
                            .Select(index => (index, file: dependency.Targets[index]))
                            .Select(item => $"targets[{item.index}]: \"{item.file.FullName}\" ({(item.file.Exists ? item.file.LastWriteTimeUtc.ToString("o") : "Not Exists")})"));
                }

                if (dependency.Sources.Length == 1)
                {
                    var file = dependency.Sources[0];
                    fileDescriptions =
                        fileDescriptions
                        .Append($"source: \"{file.FullName}\" ({(file.Exists ? file.LastWriteTimeUtc.ToString("o") : "Not Exists")})");
                }
                else
                {
                    fileDescriptions =
                        fileDescriptions
                        .Concat(
                            Enumerable.Range(0, dependency.Sources.Length)
                            .Select(index => (index, file: dependency.Sources[index]))
                            .Select(item => $"sources[{item.index}]: \"{item.file.FullName}\" ({(item.file.Exists ? item.file.LastWriteTimeUtc.ToString("o") : "Not Exists")})"));
                }

                foreach (var fileDescription in fileDescriptions)
                    PrintInformationMessage(fileDescription);
            }

            var allTargetFilesExist = dependency.Targets.All(file => file.Exists);
            foreach (var sourceFile in dependency.Sources)
            {
                if (!sourceFile.Exists)
                    throw new Exception($"The source file does not exist.: \"{sourceFile.FullName}\"");
            }

            var oldestTargetFile = dependency.Targets.Where(file => file.Exists).OrderBy(file => file.LastWriteTimeUtc).FirstOrDefault();
            var newestSourceFile = dependency.Sources.Append(scriptFile).OrderByDescending(file => file.LastWriteTimeUtc).First();

            if (allTargetFilesExist && oldestTargetFile is not null && oldestTargetFile.LastWriteTimeUtc >= newestSourceFile.LastWriteTimeUtc)
            {
                if (verbose)
                    PrintInformationMessage("Because the source file(s) is not newer than the target file(s), the command(s) to update the target file(s) is not executed.");
            }
            else
            {
                if (verbose)
                    PrintInformationMessage("Because the source file(s) is newer than the target file(s), the command(s) to update the target file(s) is executed.");
                var temporaryFiles = new Dictionary<int, FilePath>();
                var temporaryDirectories = new Dictionary<int, DirectoryPath>();
                try
                {
                    var commandSpecs = ParseCommandLines(lines[nextIndex..], dependency, temporaryFiles, temporaryDirectories);
                    if (verbose)
                    {
                        foreach (var commandSpec in commandSpecs)
                        {
                            if (!commandSpec.IsPipedFromPreviousCommand)
                                PrintInformationMessage($"Execute command:");
                            PrintInformationMessage($"{(commandSpec.IsPipedFromPreviousCommand ? "| " : "")}{commandSpec.CommandLine}{(commandSpec.FileRedirectedToStandardInput is not null ? $" < {commandSpec.FileRedirectedToStandardInput.FullName}" : "")}{(commandSpec.FileRedirectedToStandardOutput is not null ? $" > {commandSpec.FileRedirectedToStandardOutput.FullName}" : "")}");
                        }
                    }

                    var pipedProcesses = new List<Task>();

                    foreach (var commandSpec in commandSpecs)
                    {
                        var processTask = commandSpec.Start();
                        pipedProcesses.Add(processTask);
                        if (!commandSpec.IsPipedToNextCommand)
                        {
                            foreach (var pipedProcess in pipedProcesses)
                                pipedProcess.Wait();
                            pipedProcesses.Clear();
                        }
                    }
                }
                finally
                {
                    foreach (var temporaryFile in temporaryFiles.Values)
                        temporaryFile.SafetyDelete();
                    foreach (var temporaryDirectory in temporaryDirectories.Values)
                        temporaryDirectory.SafetyDelete(true);
                }
            }
        }

        private static int FindHeaderMarker(ReadOnlyMemory<string> lines)
        {
            for (var index = 0; index < lines.Length; ++index)
            {
                if (GetHeaderMarkerPattern().IsMatch(lines.Span[index].TrimEnd()))
                    return index;
            }

            return -1;
        }

        private static int ParseDependency(ReadOnlyMemory<string> lines, out Dependency dependency)
        {
            dependency = new Dependency();
            for (var index = 0; index < lines.Length; ++index)
            {
                var line = lines.Span[index].Trim();
                if (line.StartsWith(':'))
                {
                    var token = line[1..].TrimStart();
                    if (token.Length <= 0)
                        throw new Exception("Syntax error: No source file pathname after ':'.");
                    dependency.AddSource(CreateFilePath(token));
                }
                else if (line.Length <= 0)
                {
                    return index;
                }
                else
                {
                    dependency.AddTarget(CreateFilePath(line.Trim()));
                }
            }

            return lines.Length;
        }

        private static IEnumerable<CommandSpec> ParseCommandLines(ReadOnlyMemory<string> lines, Dependency dependency, IDictionary<int, FilePath> temporaryFiles, IDictionary<int, DirectoryPath> temporaryDirectories)
        {
            var commandTexts = new List<string>();
            var redirecttFromStdin = (FilePath?)null;
            var redirectToStdout = (FilePath?)null;
            var pipeFromPreviousCommand = (InProcessPipe?)null;

            foreach (var line in Filter(lines))
            {
                var trimmedLine = EvalueVariable(line.Trim(), dependency, temporaryFiles, temporaryDirectories);
                if (trimmedLine.Length <= 0)
                {
                    foreach (var commandSpec in Flush(false))
                        yield return commandSpec;
                }
                else if (trimmedLine.StartsWith('<'))
                {
                    if (redirecttFromStdin is not null)
                        throw new Exception("Duplicate redirection from standard input.");
                    if (pipeFromPreviousCommand is not null)
                        throw new Exception("Pipe and redirection conflict.");
                    if (trimmedLine[1..].TrimStart().Length <= 0)
                        throw new Exception("No redirection source for standard input is specified.");
                    redirecttFromStdin = CreateFilePath(trimmedLine.DecodeCommandLineArgument());
                }
                else if (trimmedLine.StartsWith('>'))
                {
                    if (redirectToStdout is not null)
                        throw new Exception("Duplicate redirection to standard output.");
                    if (trimmedLine[1..].TrimStart().Length <= 0)
                        throw new Exception("No redirection destination for standard output is specified.");
                    redirectToStdout = CreateFilePath(trimmedLine.DecodeCommandLineArgument());
                }
                else if (trimmedLine.StartsWith('|'))
                {
                    foreach (var commandSpec in Flush(true))
                        yield return commandSpec;
                }
                else
                {
                    if (redirecttFromStdin is not null)
                        throw new Exception("The command is specified after the redirect specification.");
                    if (redirectToStdout is not null)
                        throw new Exception("The command is specified after the redirect specification.");
                    commandTexts.Add(trimmedLine);
                }
            }

            foreach (var commandSpec in Flush(false))
                yield return commandSpec;

            static IEnumerable<string> Filter(ReadOnlyMemory<string> lines)
            {
                for (var index = 0; index < lines.Length; ++index)
                {
                    var line = lines.Span[index].Trim();
                    if (line.StartsWith('#'))
                    {
                    }
                    else if (line.StartsWith('|'))
                    {
                        yield return "|";
                        var line2 = line[1..].TrimStart();
                        if (line2.Length > 0)
                            yield return line2;
                    }
                    else
                    {
                        yield return line;
                    }
                }
            }

            static string EvalueVariable(string text, Dependency dependency, IDictionary<int, FilePath> temporaryFiles, IDictionary<int,DirectoryPath> temporaryDirectories)
            {
                return
                    GetVariableSpecPattern().Replace(
                        text,
                        m =>
                        {
                            var symbol = m.Groups["symbol"].Value;
                            var indexGroup = m.Groups["index"];
                            var optionsGGroup = m.Groups["options"];
                            var index = indexGroup.Success ? int.Parse(indexGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture.NumberFormat) : (int?)null;
                            var options = optionsGGroup.Success ? optionsGGroup.Value.Split(':').AsReadOnlySpan(1) : [];
                            switch (symbol)
                            {
                                case "target":
                                    if (index is not null)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (dependency.Targets.Length != 1)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    return ShortenFilePathNames(dependency.Targets[0]).EncodeCommandLineArgument();
                                case "targets":
                                    if (index is null)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (index.Value > dependency.Targets.Length)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    return ShortenFilePathNames(dependency.Targets[index.Value - 1]).EncodeCommandLineArgument();
                                case "source":
                                    if (index is not null)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (dependency.Sources.Length != 1)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    return ShortenFilePathNames(dependency.Sources[0]).EncodeCommandLineArgument();
                                case "sources":
                                    if (index is null)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (index.Value > dependency.Sources.Length)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    return ShortenFilePathNames(dependency.Sources[index.Value - 1]).EncodeCommandLineArgument();
                                case "temp-file":
                                    if (index is null)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (!temporaryFiles.TryGetValue(index.Value, out var temporaryFile))
                                    {
                                        temporaryFile = FilePath.CreateTemporaryFile();
                                        temporaryFiles.Add(index.Value, temporaryFile);
                                    }

                                    return ShortenFilePathNames(temporaryFile).EncodeCommandLineArgument();
                                case "temp-dir":
                                    if (index is null)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 1)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0 && options[0].IndexOfAny(_pathDelimiters) > 0)
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (!temporaryDirectories.TryGetValue(index.Value, out var temporaryDirectory))
                                    {
                                        temporaryDirectory = DirectoryPath.CreateTemporaryDirectory();
                                        temporaryDirectories.Add(index.Value, temporaryDirectory);
                                    }

                                    try
                                    {
                                        return
                                            options.Length > 0
                                            ? ShortenFilePathNames(temporaryDirectory.GetFile(options[0])).EncodeCommandLineArgument()
                                            : ShortenFilePathNames(temporaryDirectory).EncodeCommandLineArgument();
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new Exception($"An invalid variable is specified.: \"{m.Value}\"", ex);
                                    }

                                default:
                                    throw new Exception($"An invalid variable is specified.: \"{m.Value}\"");
                            }
                        });
            }

            IEnumerable<CommandSpec> Flush(bool isPipedToNextCommand)
            {
                if (commandTexts.Count <= 0)
                {
                    if (redirecttFromStdin is not null || redirectToStdout is not null)
                        throw new Exception("No command precedes the redirection.");
                    if (pipeFromPreviousCommand is not null)
                        throw new Exception("The command to which the pipe is connected is not specified.");
                    if (isPipedToNextCommand)
                        throw new Exception("The command to which the pipe is connected is not specified.");
                }
                else
                {
                    var isPipedFromPreviousCommand = pipeFromPreviousCommand is not null;
                    if (redirecttFromStdin is not null && isPipedFromPreviousCommand)
                        throw new Exception("Pipe and redirection conflict.");
                    if (redirectToStdout is not null && isPipedToNextCommand)
                        throw new Exception("Pipe and redirection conflict.");

                    yield return
                        new CommandSpec(
                            string.Join(" ", commandTexts),
                            OpenStdin(),
                            OpenStdout(isPipedToNextCommand),
                            isPipedFromPreviousCommand,
                            isPipedToNextCommand,
                            redirecttFromStdin,
                            redirectToStdout);

                    commandTexts.Clear();
                    redirecttFromStdin = null;
                    redirectToStdout = null;
                }
            }

            ISequentialInputByteStream? OpenStdin()
                => pipeFromPreviousCommand?.OpenInputStream() ?? redirecttFromStdin?.OpenRead();

            ISequentialOutputByteStream? OpenStdout(bool isPipedToNextCommand)
            {
                if (isPipedToNextCommand)
                {
                    pipeFromPreviousCommand = new InProcessPipe();
                    return pipeFromPreviousCommand.OpenOutputStream();
                }
                else if (redirectToStdout is not null)
                {
                    pipeFromPreviousCommand = null;
                    return redirectToStdout.Create();
                }
                else
                {
                    pipeFromPreviousCommand = null;
                    return null;
                }
            }
        }

        private static string ShortenFilePathNames(FilePath path)
        {
            var relativePath = path.GetRelativePath(DirectoryPath.CurrentDirectory);
            return
                relativePath is not null && relativePath.Length < path.FullName.Length
                ? relativePath
                : path.FullName;
        }

        private static string ShortenFilePathNames(DirectoryPath path)
        {
            var relativePath = path.GetRelativePath(DirectoryPath.CurrentDirectory);
            return
                relativePath is not null && relativePath.Length < path.FullName.Length
                ? relativePath
                : path.FullName;
        }

        private static FilePath CreateFilePath(string path)
        {
            try
            {
                return new FilePath(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Invalid path name.: \"{path}\"", ex);
            }
        }

        private static DirectoryPath CreateDirectoryPath(string path)
        {
            try
            {
                return new DirectoryPath(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"Invalid path name.: \"{path}\"", ex);
            }
        }

        private static void PrintUsage() => PrintInformationMessage("Usage: bmake [-v | --verbose] <script file path name>");

        private static void PrintException(Exception ex, int indent = 0)
        {
            lock (_thisProgramName)
            {
                PrintErrorMessage(ex.Message);
                if (ex.InnerException is not null)
                    PrintException(ex.InnerException, indent + 1);
                if (ex is AggregateException aggregateException)
                {
                    foreach (var innerException in aggregateException.InnerExceptions)
                        PrintException(innerException, indent + 1);
                }
            }
        }

        private static void PrintInformationMessage(string message, int indent = 0) => PrintMessage("INFO", ConsoleColor.Cyan, message, indent);

        private static void PrintWarningMessage(string message, int indent = 0) => PrintMessage("WARNING", ConsoleColor.Yellow, message, indent);

        private static void PrintErrorMessage(string message, int indent = 0) => PrintMessage("ERROR", ConsoleColor.Red, message, indent);

        private static void PrintMessage(string category, ConsoleColor foregroundColor, string message, int indent = 0)
        {
            lock (_thisProgramName)
            {
                var currentForeGroundColor = TinyConsole.ForegroundColor;
                TinyConsole.Write($"{new string(' ', indent << 1)}{_thisProgramName}:");
                TinyConsole.ForegroundColor = foregroundColor;
                TinyConsole.Write(category);
                TinyConsole.ForegroundColor = currentForeGroundColor;
                TinyConsole.WriteLine($":{message}");
            }
        }

        [GeneratedRegex(@"^# +!(/bin|/usr/bin|/usr/local/bin)?bmake *$", RegexOptions.Compiled | RegexOptions.ExplicitCapture)]
        private static partial Regex GetHeaderMarkerPattern();

        [GeneratedRegex(@"\${(?<symbol>[a-z\-]+)(-(?<index>\d+))?(?<options>(:[^:}]+)*)}", RegexOptions.Compiled | RegexOptions.ExplicitCapture)]
        private static partial Regex GetVariableSpecPattern();
    }
}
