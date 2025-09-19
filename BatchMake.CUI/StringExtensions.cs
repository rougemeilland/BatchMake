using System;
using Palmtree.IO;

namespace BatchMake.CUI
{
    internal static class StringExtensions
    {
        public static FilePath CreateFilePath(this string path)
        {
            try
            {
                return new FilePath(path);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Invalid path name.: \"{path}\"", ex);
            }
        }

        public static DirectoryPath CreateDirectoryPath(this string path)
        {
            try
            {
                return new DirectoryPath(path);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Invalid path name.: \"{path}\"", ex);
            }
        }
    }
}
