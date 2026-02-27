using System;
using System.IO;

namespace BilliardPhysics.Editor
{
    /// <summary>
    /// Pure-logic helpers for the Fixed Binary export flow used by
    /// <see cref="TableAndPocketAuthoringEditor"/>.  Kept in a separate file so
    /// that file-name validation and extension-normalisation rules can be
    /// exercised by unit tests without needing a running Unity Editor session.
    /// </summary>
    public static class ExportFixedBinaryHelper
    {
        /// <summary>The canonical file extension for exported fixed-point binary data.</summary>
        public const string Extension = ".bytes";

        // Characters that are illegal in a file name on Windows or macOS/Linux.
        // '\0' is included as a defensive measure: the null byte is invalid in
        // file names on every major OS and should never appear in user input.
        private static readonly char[] k_illegalNameChars =
            { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0' };

        /// <summary>
        /// Ensures <paramref name="fileName"/> ends with <c>.bytes</c>.
        /// <list type="bullet">
        ///   <item>No extension → appends <c>.bytes</c>.</item>
        ///   <item>Already ends with <c>.bytes</c> (case-insensitive) → unchanged.</item>
        ///   <item>Any other extension → replaced with <c>.bytes</c>.</item>
        /// </list>
        /// <c>null</c> or empty input is returned unchanged.
        /// </summary>
        public static string NormalizeExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
                return fileName + Extension;
            if (string.Equals(ext, Extension, StringComparison.OrdinalIgnoreCase))
                return fileName;

            // Replace the existing extension with .bytes.
            return Path.GetFileNameWithoutExtension(fileName) + Extension;
        }

        /// <summary>
        /// Validates that <paramref name="fileName"/> is a legal file-name token
        /// (not empty/whitespace-only and containing no illegal characters).
        /// </summary>
        /// <param name="fileName">The file name to validate (may include the extension).</param>
        /// <param name="errorMessage">
        /// Human-readable reason when the method returns <see langword="false"/>.
        /// </param>
        /// <returns><see langword="true"/> when the name is valid.</returns>
        public static bool ValidateFileName(string fileName, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                errorMessage = "File name must not be empty.";
                return false;
            }

            if (fileName.IndexOfAny(k_illegalNameChars) >= 0)
            {
                errorMessage = "File name contains illegal characters.  "
                             + "The following characters are not allowed:  "
                             + "/ \\ : * ? \" < > |";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
