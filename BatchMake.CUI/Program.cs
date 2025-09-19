using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Console;

namespace BatchMake.CUI
{
    internal static partial class Program
    {
        private enum StateOfParsingDependencyLines
        {
            ParsingTargets = 0,
            ParsingSources,
        }

        private static readonly char[] _pathDelimiters = ['/', '\\'];

        private static int Main(string[] args)
        {
            if (TinyConsole.InputEncoding.CodePage != Encoding.UTF8.CodePage || TinyConsole.OutputEncoding.CodePage != Encoding.UTF8.CodePage)
            {
                if (OperatingSystem.IsWindows())
                    TinyConsole.WriteLog(LogCategory.Warning, "The encoding of standard input or output is not UTF8. Consider running the command \"chcp 65001\".");
                else
                    TinyConsole.WriteLog(LogCategory.Warning, "The encoding of standard input or standard output is not UTF8.");
            }

            TinyConsole.DefaultTextWriter = ConsoleTextWriterType.StandardError;

            var scriptFilePath = (FilePath?)null;
            var verbose = false;
            var logFile = (FilePath?)null;
            var logWriter = (ISequentialOutputByteStream?)null;
            var originalStandardOutput = TinyConsole.StandardOutput;
            var originalStandardError = TinyConsole.StandardError;
            var newStandardOutput = (ISequentialOutputByteStream?)null;
            var newStandardError = (ISequentialOutputByteStream?)null;
            var success = false;
            try
            {
                for (var index = 0; index < args.Length; ++index)
                {
                    var arg = args[index];
                    if (arg is "-v" or "--verbose")
                    {
                        if (verbose)
                        {
                            TinyConsole.WriteLog(LogCategory.Error, "The '-v' option (or the '--verbose' option) is specified multiple times.");
                            PrintUsage();
                            return 1;
                        }

                        verbose = true;
                    }
                    else if (arg is "-l" or "--log")
                    {
                        if (logFile is not null)
                            TinyConsole.WriteLog(LogCategory.Error, "The '-l' option (or the '--log' option) is specified multiple times.");
                        var logDir = DirectoryPath.UserHomeDirectory?.GetSubDirectory(".palmtree")?.GetSubDirectory("bmake")?.GetSubDirectory("log")?.Create();
                        if (logDir is not null)
                        {
                            try
                            {
                                (logFile, logWriter) = CreateLogWriter(logDir);
                                newStandardOutput = originalStandardOutput.WithBranch(logWriter, true);
                                newStandardError = originalStandardError.WithBranch(logWriter, true);
                                TinyConsole.StandardOutput = newStandardOutput;
                                TinyConsole.StandardError = newStandardError;
                            }
                            catch (Exception ex)
                            {
                                TinyConsole.WriteLog(ex);
                                PrintUsage();
                                return 1;
                            }
                        }
                    }
                    else if (arg.StartsWith('-'))
                    {
                        TinyConsole.WriteLog(LogCategory.Error, $"A unknown argument is specified.: \"{arg}\"");
                        PrintUsage();
                        return 1;
                    }
                    else if (scriptFilePath is not null)
                    {
                        TinyConsole.WriteLog(LogCategory.Error, $"Multiple script files are specified.: \"{scriptFilePath.FullName}\", \"{arg}\"");
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
                                TinyConsole.WriteLog(LogCategory.Error, $"The specified script file does not exist.: \"{scriptFilePath.FullName}\"");
                                PrintUsage();
                                return 1;
                            }
                        }
                        catch (Exception)
                        {
                            TinyConsole.WriteLog(LogCategory.Error, $"The path name of the script file is invalid.: \"{arg}\"");
                            PrintUsage();
                            return 1;
                        }
                    }
                }

                if (scriptFilePath is null)
                {
                    TinyConsole.WriteLog(LogCategory.Error, "The path name of the script file is not specified.");
                    PrintUsage();
                    return 1;
                }

                try
                {
                    Environment.CurrentDirectory = scriptFilePath.Directory.FullName;
                    DoProcess(scriptFilePath, verbose);
                    success = true;
                    return 0;
                }
                catch (Exception ex)
                {
                    TinyConsole.WriteLog(ex);
                    return 1;
                }
            }
            finally
            {
                if (logFile is not null || logWriter is not null)
                {
                    TinyConsole.Out.Flush();
                    TinyConsole.Error.Flush();
                    TinyConsole.StandardOutput = originalStandardOutput;
                    TinyConsole.StandardError = originalStandardError;
                    newStandardOutput?.Dispose();
                    newStandardError?.Dispose();
                    logWriter?.Dispose();

                    // 失敗時のログのみ残す
                    if (success)
                        logFile?.SafetyDelete();
                }
            }
        }

        private static (FilePath logFile, ISequentialOutputByteStream logWriter) CreateLogWriter(DirectoryPath logDir)
        {
            var now = DateTime.Now;
            try
            {
                var logFile = logDir.CreateUniqueFile(prefix: now.ToString("O").Replace(":", "-"), suffix: ".log");
                var logWriter = logFile.Create();
                return (logFile, logWriter);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("The log file could not be created.", ex);
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
                throw new ApplicationException($"Header marker (e.g. \"# !bmake\") not found.: path=\"{scriptFile.FullName}\"");
            lines = lines[(headerMarkerPos + 1)..];

            var nextIndex = ParseDependency(lines, out var dependency);
            if (dependency.Targets.Length <= 0)
                throw new ApplicationException($"No target specified.: path=\"{scriptFile.FullName}\"");
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
                    TinyConsole.WriteLog(LogCategory.Information, fileDescription);
            }

            var allTargetFilesExist = dependency.Targets.All(file => file.Exists);
            foreach (var sourceFile in dependency.Sources)
            {
                if (!sourceFile.Exists)
                    throw new ApplicationException($"The source file does not exist.: \"{sourceFile.FullName}\"");
            }

            var oldestTargetFile = dependency.Targets.Where(file => file.Exists).OrderBy(file => file.LastWriteTimeUtc).FirstOrDefault();
            var newestSourceFile = dependency.Sources.Append(scriptFile).OrderByDescending(file => file.LastWriteTimeUtc).First();

            if (allTargetFilesExist && oldestTargetFile is not null && oldestTargetFile.LastWriteTimeUtc >= newestSourceFile.LastWriteTimeUtc && !dependency.UpdateAlways)
            {
                if (verbose)
                    TinyConsole.WriteLog(LogCategory.Information, "Because the source file(s) is not newer than the target file(s), the command(s) to update the target file(s) is not executed.");
            }
            else
            {
                if (verbose)
                {
                    if (dependency.UpdateAlways)
                        TinyConsole.WriteLog(LogCategory.Information, "Because the target file(s) is always expected to be updated, the command(s) to update the target file(s) is executed.");
                    else
                        TinyConsole.WriteLog(LogCategory.Information, "Because the source file(s) is newer than the target file(s), the command(s) to update the target file(s) is executed.");
                }

                var temporaryFiles = new Dictionary<int, FilePath>();
                var temporaryDirectories = new Dictionary<int, DirectoryPath>();
                var success = false;
                try
                {
                    var commandSpecs = ParseCommandLines(lines[nextIndex..], dependency, temporaryFiles, temporaryDirectories, verbose).ToArray();
                    var pipedProcesses = new List<Task>();
                    foreach (var commandSpec in commandSpecs)
                    {
                        if (verbose)
                        {
                            if (!commandSpec.IsPipedFromPreviousCommand)
                                TinyConsole.WriteLog(LogCategory.Information, $"Execute command:");
                            TinyConsole.WriteLog(LogCategory.Information, $"{(commandSpec.IsPipedFromPreviousCommand ? "| " : "")}{commandSpec.CommandLine}{(commandSpec.FileRedirectedToStandardInput is not null ? $" < {commandSpec.FileRedirectedToStandardInput.FullName}" : "")}{(commandSpec.FileRedirectedToStandardOutput is null ? "" : $" {(commandSpec.AppendOnRedirecting ? ">>" : ">")} {commandSpec.FileRedirectedToStandardOutput.FullName}")}");
                        }

                        pipedProcesses.Add(commandSpec.Start());
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
                    if (!success)
                    {
                        // 更新処理が失敗した場合にはターゲットファイルをすべて削除する

                        foreach (var targetFile in dependency.Targets)
                            targetFile.SafetyDelete();

                    }

                    foreach (var temporaryFile in temporaryFiles.Values)
                    {
                        temporaryFile.SafetyDelete();
                        if (verbose)
                            TinyConsole.WriteLog(LogCategory.Information, $"Deleted tempprary file \"{temporaryFile.FullName}\"");
                    }

                    foreach (var temporaryDirectory in temporaryDirectories.Values)
                    {
                        temporaryDirectory.SafetyDelete(true);
                        if (verbose)
                            TinyConsole.WriteLog(LogCategory.Information, $"Deleted tempprary directory \"{temporaryDirectory.FullName}\"");
                    }
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
            var state = StateOfParsingDependencyLines.ParsingTargets;
            for (var index = 0; index < lines.Length; ++index)
            {
                var line = lines.Span[index].Trim();
                if (line.StartsWith(':'))
                {
                    switch (state)
                    {
                        case StateOfParsingDependencyLines.ParsingTargets:
                        {
                            state = StateOfParsingDependencyLines.ParsingSources;
                            var token = line[1..].TrimStart();
                            if (token.Length > 0)
                            {
                                if (token.Equals("!", StringComparison.Ordinal))
                                    dependency.UpdateAlways = true;
                                else
                                    dependency.AddSource(token.CreateFilePath());
                            }

                            break;
                        }
                        case StateOfParsingDependencyLines.ParsingSources:
                            throw new ApplicationException("Syntax error: The dependency definition contains multiple lines beginning with ':'.");
                        default:
                            throw Validation.GetFailErrorException();
                    }
                }
                else if (line.Length <= 0)
                {
                    return index;
                }
                else
                {
                    switch (state)
                    {
                        case StateOfParsingDependencyLines.ParsingTargets:
                            dependency.AddTarget(line.CreateFilePath());
                            break;
                        case StateOfParsingDependencyLines.ParsingSources:
                        {
                            if (line.Equals("!", StringComparison.Ordinal))
                                dependency.UpdateAlways = true;
                            else
                                dependency.AddSource(line.CreateFilePath());
                            break;
                        }
                        default:
                            throw Validation.GetFailErrorException();
                    }
                }
            }

            return lines.Length;
        }

        private static IEnumerable<CommandSpec> ParseCommandLines(ReadOnlyMemory<string> lines, Dependency dependency, IDictionary<int, FilePath> temporaryFiles, IDictionary<int, DirectoryPath> temporaryDirectories, bool verbose)
        {
            var commandTexts = new List<string>();
            var redirectFromStdin = (FilePath?)null;
            var redirectToStdout = (FilePath?)null;
            var appendMode = false;
            var pipeFromPreviousCommand = (InProcessPipe?)null;

            foreach (var line in Filter(lines))
            {
                var trimmedLine = EvalueVariable(line.Trim(), dependency, temporaryFiles, temporaryDirectories, verbose);
                if (trimmedLine.Length <= 0)
                {
                    var commandSpec = Flush(false);
                    if (commandSpec is not null)
                        yield return commandSpec;
                }
                else if (trimmedLine.StartsWith('<'))
                {
                    trimmedLine = trimmedLine[1..].TrimStart();
                    if (redirectFromStdin is not null)
                        throw new ApplicationException("Duplicate redirection from standard input.");
                    if (pipeFromPreviousCommand is not null)
                        throw new ApplicationException("Pipe and redirection conflict.");
                    if (trimmedLine.Length <= 0)
                        throw new ApplicationException("No redirection source for standard input is specified.");
                    redirectFromStdin = trimmedLine.DecodeCommandLineArgument().CreateFilePath();
                }
                else if (trimmedLine.StartsWith(">>", StringComparison.Ordinal))
                {
                    trimmedLine = trimmedLine[2..].TrimStart();
                    if (redirectToStdout is not null)
                        throw new ApplicationException("Duplicate redirection to standard output.");
                    if (trimmedLine.Length <= 0)
                        throw new ApplicationException("No redirection destination for standard output is specified.");
                    redirectToStdout = trimmedLine.DecodeCommandLineArgument().CreateFilePath();
                    appendMode = true;
                }
                else if (trimmedLine.StartsWith('>'))
                {
                    trimmedLine = trimmedLine[1..].TrimStart();
                    if (redirectToStdout is not null)
                        throw new ApplicationException("Duplicate redirection to standard output.");
                    if (trimmedLine.Length <= 0)
                        throw new ApplicationException("No redirection destination for standard output is specified.");
                    redirectToStdout = trimmedLine.DecodeCommandLineArgument().CreateFilePath();
                    appendMode = false;
                }
                else if (trimmedLine.StartsWith('|'))
                {
                    var commandSpec = Flush(true);
                    if (commandSpec is not null)
                        yield return commandSpec;
                }
                else
                {
                    if (redirectFromStdin is not null)
                        throw new ApplicationException("The command is specified after the redirect specification.");
                    if (redirectToStdout is not null)
                        throw new ApplicationException("The command is specified after the redirect specification.");
                    commandTexts.Add(trimmedLine);
                }
            }

            {
                var commandSpec = Flush(false);
                if (commandSpec is not null)
                    yield return commandSpec;
            }

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

            static string EvalueVariable(string text, Dependency dependency, IDictionary<int, FilePath> temporaryFiles, IDictionary<int, DirectoryPath> temporaryDirectories, bool verbose)
            {
                return
                    GetVariablePattern().Replace(
                        text,
                        m =>
                        {
                            var variable = m.Groups["variable"].Value;
                            var m2 = GetVariableSpecPattern().Match(variable);
                            if (!m2.Success)
                                throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                            var symbol = m2.Groups["symbol"].Value;
                            var indexGroup = m2.Groups["index"];
                            var optionsGGroup = m2.Groups["options"];
                            var index = indexGroup.Success ? int.Parse(indexGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture.NumberFormat) : (int?)null;
                            var options = optionsGGroup.Success ? optionsGGroup.Value.Split(':').AsReadOnlySpan(1) : [];
                            switch (symbol)
                            {
                                case "target":
                                {
                                    if (index is not null)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (dependency.Targets.Length != 1)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    return dependency.Targets[0].ShortenFilePathNames().EncodeCommandLineArgument();
                                }
                                case "targets":
                                {
                                    if (index is null)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (index.Value > dependency.Targets.Length)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    return dependency.Targets[index.Value - 1].ShortenFilePathNames().EncodeCommandLineArgument();
                                }
                                case "source":
                                {
                                    if (index is not null)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (dependency.Sources.Length != 1)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    return dependency.Sources[0].ShortenFilePathNames().EncodeCommandLineArgument();
                                }
                                case "sources":
                                {
                                    if (index is null)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (index.Value > dependency.Sources.Length)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    return dependency.Sources[index.Value - 1].ShortenFilePathNames().EncodeCommandLineArgument();
                                }
                                case "temp-file":
                                {
                                    if (index is null)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 1)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0 && (options[0].Length < 2 || !options[0].StartsWith('.') || options[0].IndexOf('.', 1) > 0 || options[0].IndexOfAny(_pathDelimiters) > 0))
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (temporaryFiles.TryGetValue(index.Value, out var temporaryFile))
                                    {
                                        if (options[0].Length > 0 && !temporaryFile.Extension.Equals(options[0], StringComparison.Ordinal))
                                            throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    }
                                    else
                                    {
                                        temporaryFile =
                                            options[0].Length > 0
                                            ? FilePath.CreateTemporaryFile(suffix: options[0])
                                            : FilePath.CreateTemporaryFile();
                                        temporaryFiles.Add(index.Value, temporaryFile);
                                        if (verbose)
                                            TinyConsole.WriteLog(LogCategory.Information, $"Created temprary file \"{temporaryFile.FullName}\".");
                                    }

                                    return temporaryFile.ShortenFilePathNames().EncodeCommandLineArgument();
                                }
                                case "temp-dir":
                                {
                                    if (index is null)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 1)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (options.Length > 0 && options[0].IndexOfAny(_pathDelimiters) > 0)
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                                    if (!temporaryDirectories.TryGetValue(index.Value, out var temporaryDirectory))
                                    {
                                        temporaryDirectory = DirectoryPath.CreateTemporaryDirectory();
                                        temporaryDirectories.Add(index.Value, temporaryDirectory);
                                        if (verbose)
                                            TinyConsole.WriteLog(LogCategory.Information, $"Created temprary directory \"{temporaryDirectory.FullName}\".");
                                    }

                                    try
                                    {
                                        return
                                            options.Length > 0
                                            ? temporaryDirectory.GetFile(options[0]).ShortenFilePathNames().EncodeCommandLineArgument()
                                            : temporaryDirectory.ShortenFilePathNames().EncodeCommandLineArgument();
                                    }
                                    catch (Exception ex)
                                    {
                                        throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"", ex);
                                    }
                                }
                                default:
                                    throw new ApplicationException($"An invalid variable is specified.: \"{m.Value}\"");
                            }
                        });
            }

            CommandSpec? Flush(bool isPipedToNextCommand)
            {
                if (commandTexts.Count <= 0)
                {
                    if (redirectFromStdin is not null || redirectToStdout is not null)
                        throw new ApplicationException("No command precedes the redirection.");
                    if (pipeFromPreviousCommand is not null)
                        throw new ApplicationException("The command to which the pipe is connected is not specified.");
                    if (isPipedToNextCommand)
                        throw new ApplicationException("The command to which the pipe is connected is not specified.");
                    return null;
                }
                else
                {
                    var isPipedFromPreviousCommand = pipeFromPreviousCommand is not null;
                    if (redirectFromStdin is not null && isPipedFromPreviousCommand)
                        throw new ApplicationException("Pipe and redirection conflict.");
                    if (redirectToStdout is not null && isPipedToNextCommand)
                        throw new ApplicationException("Pipe and redirection conflict.");
                    var commandSpec =
                        CommandSpec.CreateInstance(
                            string.Join(" ", commandTexts),
                            GetStandardInputOpener(),
                            GetStandardOutputOpener(isPipedToNextCommand, appendMode),
                            isPipedFromPreviousCommand,
                            isPipedToNextCommand,
                            redirectFromStdin,
                            redirectToStdout,
                            appendMode);
                    commandTexts.Clear();
                    redirectFromStdin = null;
                    redirectToStdout = null;
                    appendMode = false;
                    return commandSpec;
                }
            }

            Func<ISequentialInputByteStream>? GetStandardInputOpener()
            {
                if (pipeFromPreviousCommand is not null)
                {
                    var pipe = pipeFromPreviousCommand;
                    return pipe.OpenInputStream;
                }
                else if (redirectFromStdin is not null)
                {
                    var sourceFile = redirectFromStdin;
                    return GetDestinationFileOpener(sourceFile);
                }
                else
                {
                    return null;
                }

                static Func<ISequentialInputByteStream> GetDestinationFileOpener(FilePath sourceFile)
                {
                    return () => sourceFile.OpenRead();
                }
            }

            Func<ISequentialOutputByteStream>? GetStandardOutputOpener(bool isPipedToNextCommand, bool appendMode)
            {
                if (isPipedToNextCommand)
                {
                    pipeFromPreviousCommand = new InProcessPipe();
                    var pipe = pipeFromPreviousCommand;
                    return pipe.OpenOutputStream;
                }
                else if (redirectToStdout is not null)
                {
                    pipeFromPreviousCommand = null;
                    return GetDestinationFileOpener(redirectToStdout, appendMode);
                }
                else
                {
                    pipeFromPreviousCommand = null;
                    return null;
                }

                static Func<ISequentialOutputByteStream> GetDestinationFileOpener(FilePath destinationFile, bool doAppend)
                {
                    return () => doAppend ? destinationFile.Append() : destinationFile.Create();
                }
            }
        }

        private static void PrintUsage() => TinyConsole.WriteLog(LogCategory.Information, $"Usage: {Validation.DefaultApplicationName} [-v | --verbose] <script file path name>");

        [GeneratedRegex(@"^# +!(/bin|/usr/bin|/usr/local/bin)?bmake *$", RegexOptions.Compiled | RegexOptions.ExplicitCapture)]
        private static partial Regex GetHeaderMarkerPattern();

        [GeneratedRegex(@"^(?<symbol>[a-z\-]+)(-(?<index>\d+))?(?<options>(:[^:}]+)*)$", RegexOptions.Compiled | RegexOptions.ExplicitCapture)]
        private static partial Regex GetVariableSpecPattern();

        [GeneratedRegex(@"\${(?<variable>[^}]+)}", RegexOptions.Compiled | RegexOptions.ExplicitCapture)]
        private static partial Regex GetVariablePattern();
    }
}
