using System;
using System.Text;
using Palmtree.IO;

namespace BatchMake.CUI
{
    internal sealed class EchoCommandSpec
        : BuiltinCommandSpec
    {
        public EchoCommandSpec(string commandLine, string commandName, string commandParameters, Func<ISequentialInputByteStream>? standardInputOpener, Func<ISequentialOutputByteStream>? standardOutputOpener, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput, bool appendOnRedirecting)
            : base(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting)
        {
        }

        protected override void Execute(ISequentialInputByteStream standardInput, bool redirectStandardInput, ISequentialOutputByteStream standardOutput, bool redirectStandardOutput)
        {
            var concatinatedArguments = $"{string.Join(" ", Arguments)}{Environment.NewLine}";
            standardOutput.WriteBytes(Encoding.UTF8.GetBytes(concatinatedArguments));
        }
    }
}
