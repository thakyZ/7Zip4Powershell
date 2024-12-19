using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security;
using JetBrains.Annotations;
using SharpSevenZip;

namespace SevenZip4PowerShell {
    [Cmdlet(VerbsCommon.Get, "7ZipInformation", DefaultParameterSetName = ParameterSetNames.NoPassword)]
    [OutputType(typeof(ArchiveInformation))]
    [PublicAPI]
    public class Get7ZipInformation : PSCmdlet {
        [Parameter(Position = 0, ValueFromPipeline = true, Mandatory = true, HelpMessage = "The full file name of the archive")]
        [ValidateNotNullOrEmpty()]
        public string[] ArchiveFileName { get; set; } = null!;

        [Parameter(ParameterSetName = ParameterSetNames.PlainPassword)]
        public string? Password { get; set; } = null;

        [Parameter(ParameterSetName = ParameterSetNames.SecurePassword)]
        public SecureString? SecurePassword { get; set; } = null;

        private string? _password = null;

        protected override void BeginProcessing() {
            SharpSevenZipBase.SetLibraryPath(Utils.SevenZipLibraryPath);
    
            _password = ParameterSetName switch {
                ParameterSetNames.NoPassword => null,
                ParameterSetNames.PlainPassword when Password is not null => Password,
                ParameterSetNames.PlainPassword when Password is null => throw new Exception($"Parameter SecurePassword is null"),
                ParameterSetNames.SecurePassword when SecurePassword is not null => Utils.SecureStringToString(SecurePassword),
                ParameterSetNames.SecurePassword when SecurePassword is null => throw new Exception($"Parameter SecurePassword is null"),
                _ => throw new Exception($"Unsupported parameter set {ParameterSetName}"),
            };
        }

        protected override void ProcessRecord() {
            foreach (var archiveFileName in ArchiveFileName.Select(_ => Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, _))) {
                WriteVerbose($"Getting archive data {archiveFileName}");

                SharpSevenZipExtractor extractor;
                if (!string.IsNullOrEmpty(_password)) {
                    extractor = new SharpSevenZipExtractor(archiveFileName, _password);
                } else {
                    extractor = new SharpSevenZipExtractor(archiveFileName);
                }

                using (extractor) {
                    extractor.Check();
                    WriteObject(new ArchiveInformation {
                        FileName = Path.GetFileName(archiveFileName),
                        FullPath = Path.GetFullPath(archiveFileName),
                        PackedSize = extractor.PackedSize,
                        UnpackedSize = extractor.UnpackedSize,
                        FilesCount = extractor.FilesCount,
                        Files = extractor.ArchiveFileNames,
                        FileData = extractor.ArchiveFileData,
                        // TODO: Determine the base directory for any files.
                        BaseDirectory = string.Empty,
                        Format = extractor.Format,
                        Method = extractor.ArchiveProperties.Where(prop => prop.Name == "Method").Cast<ArchiveProperty?>().FirstOrDefault()?.Value
                    });
                }
            }
        }
    }

    [PublicAPI]
    public class ArchiveInformation {
        public required string FileName { get; init; }
        public required long PackedSize { get; init; }
        public required long UnpackedSize { get; init; }
        public required uint FilesCount { get; init; }
        public required ReadOnlyCollection<string> Files { get; init; }
        public required ReadOnlyCollection<ArchiveFileInfo> FileData { get; init; }
        public required string BaseDirectory { get; init; }
        public required string FullPath { get; init; }
        public required InArchiveFormat Format { get; init; }
        public required object? Method { get; init; }
    }
}