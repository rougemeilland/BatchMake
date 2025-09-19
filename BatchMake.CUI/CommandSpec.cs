using System;
using System.Linq;
using System.Threading.Tasks;
using Palmtree;
using Palmtree.IO;
using Palmtree.IO.Console;

namespace BatchMake.CUI
{
    internal abstract class CommandSpec
    {
        private readonly Func<ISequentialInputByteStream>? _standardInputOpener;
        private readonly Func<ISequentialOutputByteStream>? _standardOutputOpener;

        protected CommandSpec(string commandLine, string commandName, string commandParameters, Func<ISequentialInputByteStream>? standardInputOpener, Func<ISequentialOutputByteStream>? standardOutputOpener, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput, bool appendOnRedirecting)
        {
            CommandLine = commandLine;
            CommandName = commandName;
            CommandParameters = commandParameters;
            _standardInputOpener = standardInputOpener;
            _standardOutputOpener = standardOutputOpener;
            IsPipedFromPreviousCommand = isPipedFromPreviousCommand;
            IsPipedToNextCommand = isPipedToNextCommand;
            FileRedirectedToStandardInput = fileRedirectedToStandardInput;
            FileRedirectedToStandardOutput = fileRedirectedToStandardOutput;
            AppendOnRedirecting = appendOnRedirecting;
        }

        public string CommandLine { get; }
        public string CommandName { get; }
        public string CommandParameters { get; }
        public bool IsPipedFromPreviousCommand { get; }
        public bool IsPipedToNextCommand { get; }
        public FilePath? FileRedirectedToStandardInput { get; }
        public FilePath? FileRedirectedToStandardOutput { get; }
        public bool AppendOnRedirecting { get; }

        public static CommandSpec CreateInstance(string commandLine, Func<ISequentialInputByteStream>? standardInputOpener, Func<ISequentialOutputByteStream>? standardOutputOpener, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput, bool appendOnRedirecting)
        {
            var args0 = commandLine.SplitCommandLineArguments().Take(1).ToArray();
            if (args0.Length <= 0)
                throw new ApplicationException("An empty command line is specified.");
            var commandName = args0[0].element.DecodeCommandLineArgument();
            var commandParameters = commandLine[args0[0].end..].TrimStart();
            return
                commandName switch
                {
                    "cat" => new CatCommandSpec(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting),
                    "echo" => new EchoCommandSpec(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting),
                    _ => new ExternalCommandSpec(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting),
                };
        }

        public Task Start()
            => Task.Run(
                () =>
                {

                    using var standardInput = OpenStandardInput();
                    using var standardOutput = OpenStandardOutput();
                    Execute(standardInput, _standardInputOpener is not null, standardOutput, _standardOutputOpener is not null);
                });

        protected abstract void Execute(ISequentialInputByteStream standardInput, bool redirectStandardInput, ISequentialOutputByteStream standardOutput, bool redirectStandardOutput);

        private ISequentialInputByteStream OpenStandardInput()
            => _standardInputOpener is not null
                ? _standardInputOpener()
                : TinyConsole.OpenStandardInput();

        private ISequentialOutputByteStream OpenStandardOutput()
            => _standardOutputOpener is not null
                ? _standardOutputOpener()
                : TinyConsole.Out..OpenStandardOutput(); // TODO: 新たにオープンはせず既存の出力オブジェクトを参照する
    }
}
