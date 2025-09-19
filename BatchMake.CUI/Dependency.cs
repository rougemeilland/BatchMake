using System.Collections;
using System.Collections.Generic;
using Palmtree;
using Palmtree.IO;

namespace BatchMake.CUI
{
    internal sealed class Dependency
    {
        private sealed class PathCollection
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
            UpdateAlways = false;
        }

        public IReadOnlyArray<FilePath> Targets => _targets;
        public IReadOnlyArray<FilePath> Sources => _sources;
        public bool UpdateAlways { get; set; }

        public void AddTarget(FilePath target) => _targets.Add(target);
        public void AddSource(FilePath source) => _sources.Add(source);
    }
}
