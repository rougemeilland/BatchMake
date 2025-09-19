using Palmtree.IO;

namespace BatchMake.CUI
{
    internal static class PathExtensions
    {
        public static string ShortenFilePathNames(this FilePath path)
        {
            var relativePath = path.GetRelativePath(DirectoryPath.CurrentDirectory);
            return
                relativePath is not null && relativePath.Length < path.FullName.Length
                ? relativePath
                : path.FullName;
        }

        public static string ShortenFilePathNames(this DirectoryPath path)
        {
            var relativePath = path.GetRelativePath(DirectoryPath.CurrentDirectory);
            return
                relativePath is not null && relativePath.Length < path.FullName.Length
                ? relativePath
                : path.FullName;
        }
    }
}
