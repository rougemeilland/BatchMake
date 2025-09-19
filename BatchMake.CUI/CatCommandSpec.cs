using System;
using System.Linq;
using Palmtree.IO;

namespace BatchMake.CUI
{
    internal sealed class CatCommandSpec
        : BuiltinCommandSpec
    {
        public CatCommandSpec(string commandLine, string commandName, string commandParameters, Func<ISequentialInputByteStream>? standardInputOpener, Func<ISequentialOutputByteStream>? standardOutputOpener, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput, bool appendOnRedirecting)
            : base(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting)
        {
        }

        protected override void Execute(ISequentialInputByteStream standardInput, bool redirectStandardInput, ISequentialOutputByteStream standardOutput, bool redirectStandardOutput)
        {
            var inFilePaths = Arguments.Select(arg => arg.CreateFilePath()).ToArray();
            if (inFilePaths.Length > 0)
            {
                foreach (var inFilePath in inFilePaths)
                {
                    using var inStream = inFilePath.OpenRead();
                    CopyByteStream(inStream, standardOutput);
                }
            }
            else
            {
                CopyByteStream(standardInput, standardOutput);
            }

            static void CopyByteStream(ISequentialInputByteStream inStream, ISequentialOutputByteStream outStream)
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var length = inStream.Read(buffer);
                    if (length <= 0)
                        break;
                    outStream.WriteBytes(buffer, 0, length);
                }
            }
        }
    }
}
