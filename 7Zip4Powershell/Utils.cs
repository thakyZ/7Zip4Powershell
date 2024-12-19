using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security;

namespace SevenZip4PowerShell {
    internal static class Utils {
        public static string SevenZipLibraryPath => Path.Combine(AssemblyPath, SevenZipLibraryName);

        private static string AssemblyPath => Path.GetDirectoryName(typeof(Utils).Assembly.Location) ?? throw new InvalidOperationException("Failed to find assembly path.");

        private static string SevenZipLibraryName => Environment.Is64BitProcess ? "7z64.dll" : "7z.dll";

        public static string SecureStringToString(SecureString value) {
            var valuePtr = IntPtr.Zero;
            try {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(value);
                if (Marshal.PtrToStringUni(valuePtr) is not string @string) {
                    return new NetworkCredential(string.Empty, value).Password;
                }
                return @string ?? throw new InvalidOperationException("Failed to marshal a SecureString to it's plain text string");
            }
            finally {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }
    }
}
