using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Console;

namespace BatchMake.CUI
{
    internal sealed class ExternalCommandSpec
        : CommandSpec
    {
        public ExternalCommandSpec(string commandLine, string commandName, string commandParameters, Func<ISequentialInputByteStream>? standardInputOpener, Func<ISequentialOutputByteStream>? standardOutputOpener, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput, bool appendOnRedirecting)
            : base(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting)
        {
        }

        protected override void Execute(ISequentialInputByteStream standardInput, bool redirectStandardInput, ISequentialOutputByteStream standardOutput, bool redirectStandardOutput)
        {
            var commandPath =
                ProcessUtility.WhereIs(CommandName)
                ?? throw new ApplicationException($"Executable file not found.: \"{CommandName}\"");
            var startInfo = new ProcessStartInfo
            {
                FileName = new FilePath(commandPath).ShortenFilePathNames(),
                Arguments = CommandParameters,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = redirectStandardInput,
                RedirectStandardOutput = redirectStandardOutput,
                RedirectStandardError = true,
            };
            var process = StartProcess(startInfo, CommandLine);
            try
            {
                var task1 =
                    Task.Run(
                        () =>
                        {
                            if (redirectStandardInput)
                            {
                                using var outStream = process.StandardInput.BaseStream.AsOutputByteStream();
                                CopyByteStream(standardInput, outStream);
                            }
                        });
                var task2 =
                    Task.Run(
                        () =>
                        {
                            if (redirectStandardOutput)
                            {
                                using var inStream = process.StandardOutput.BaseStream.AsInputByteStream();
                                CopyByteStream(inStream, standardOutput);
                            }
                        });
                var task3 =
                    Task.Run(
                        () =>
                        {
                            using var inStream = process.StandardError;
                            CopyTextStream(inStream, TinyConsole.Error);
                        });

                Task.WaitAll(task1, task2, task3);
                process.WaitForExit();
                var exitCode = process.ExitCode;
                if (exitCode != 0)
                    throw new ApplicationException($"The process terminated abnormally.: exit-code={exitCode}, command-line=\"{CommandLine}\"");
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
                        ?? throw new ApplicationException($"Failed to create process.: command-line=\"{commandLine}\"");
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"Failed to start process.: command-line=\"{commandLine}\"", ex);
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

            static void CopyTextStream(TextReader inStream, TextWriter outStream)
            {
                Span<char> buffer = stackalloc char[1024];
                while (true)
                {
                    var length = inStream.Read(buffer);
                    if (length <= 0)
                        break;
                    outStream.Write(buffer[..length]);
                }
            }
        }
    }
}
