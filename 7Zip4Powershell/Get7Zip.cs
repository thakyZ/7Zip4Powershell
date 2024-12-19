using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Security;
using JetBrains.Annotations;
using SharpSevenZip;

namespace SevenZip4PowerShell {
    [Cmdlet(VerbsCommon.Get, "7Zip", DefaultParameterSetName = ParameterSetNames.NoPassword)]
    [PublicAPI]
    public class Get7Zip : PSCmdlet {
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
            foreach (var archiveFileName in ArchiveFileName.Select(_ => Path.GetFullPath(Path.Combine(SessionState.Path.CurrentFileSystemLocation.Path, _)))) {
                WriteVerbose($"Getting archive data {archiveFileName}");

                SharpSevenZipExtractor extractor;

                if (!string.IsNullOrEmpty(_password)) {
                    extractor = new SharpSevenZipExtractor(archiveFileName, _password);
                } else {
                    extractor = new SharpSevenZipExtractor(archiveFileName);
                }

                using (extractor) {
                    foreach (var file in extractor.ArchiveFileData) {
                        WriteObject(new PSObject(file));
                    }
                }
            }
        }
    }
}