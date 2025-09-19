using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Palmtree;
using Palmtree.IO;

namespace BatchMake.CUI
{
    internal abstract class BuiltinCommandSpec
        : CommandSpec
    {
        private sealed class ReadOnlyArrayOfString
            : IReadOnlyArray<string>
        {
            private readonly IList<string> _arrayOfString;

            public ReadOnlyArrayOfString(IEnumerable<string> arrayOfString)
            {
                _arrayOfString = arrayOfString.ToList();
            }

            string IReadOnlyIndexer<int, string>.this[int index]
            {
                get
                {
                    if (index < 0 || index >= _arrayOfString.Count)
                        throw new ArgumentOutOfRangeException(nameof(index));
                    return _arrayOfString[index];
                }
            }

            int IReadOnlyArray<string>.Length => _arrayOfString.Count;

            IEnumerator<string> IEnumerable<string>.GetEnumerator() => _arrayOfString.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _arrayOfString.GetEnumerator();
        }

        protected BuiltinCommandSpec(string commandLine, string commandName, string commandParameters, Func<ISequentialInputByteStream>? standardInputOpener, Func<ISequentialOutputByteStream>? standardOutputOpener, bool isPipedFromPreviousCommand, bool isPipedToNextCommand, FilePath? fileRedirectedToStandardInput, FilePath? fileRedirectedToStandardOutput, bool appendOnRedirecting)
            : base(commandLine, commandName, commandParameters, standardInputOpener, standardOutputOpener, isPipedFromPreviousCommand, isPipedToNextCommand, fileRedirectedToStandardInput, fileRedirectedToStandardOutput, appendOnRedirecting)
        {
            Arguments = new ReadOnlyArrayOfString(CommandParameters.SplitCommandLineArguments().Select(item => item.element));
        }

        public IReadOnlyArray<string> Arguments { get; }
    }
}
