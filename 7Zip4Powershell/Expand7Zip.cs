using System;
using System.IO;
using System.Management.Automation;
using System.Security;
using System.Threading;

using JetBrains.Annotations;
using SharpSevenZip;

namespace SevenZip4PowerShell {
    [Cmdlet(VerbsData.Expand, "7Zip", DefaultParameterSetName = ParameterSetNames.NoPassword)]
    [PublicAPI]
    public class Expand7Zip : ThreadedCmdlet {
        [Parameter(Position = 0, Mandatory = true, HelpMessage = "The full file name of the archive")]
        [ValidateNotNullOrEmpty()]
        public string ArchiveFileName { get; set; } = null!;

        [Parameter(Position = 1, Mandatory = true, HelpMessage = "The target folder")]
        [ValidateNotNullOrEmpty()]
        public string TargetPath { get; set; } = null!;

        [Parameter(ParameterSetName = ParameterSetNames.PlainPassword)]
        public string? Password { get; set; } = null;

        [Parameter(ParameterSetName = ParameterSetNames.SecurePassword)]
        public SecureString? SecurePassword { get; set; } = null;

        [Parameter(HelpMessage = "Allows setting additional parameters on SevenZipExtractor")]
        [Obsolete("The parameter CustomInitialization is obsolete, as it never worked as intended.")]
        public ScriptBlock? CustomInitialization { get; set; } = null;

        private string? _password = null;

        protected override void BeginProcessing() {
            base.BeginProcessing();
      
            _password = ParameterSetName switch {
                ParameterSetNames.NoPassword => null,
                ParameterSetNames.PlainPassword when Password is not null => Password,
                ParameterSetNames.PlainPassword when Password is null => throw new Exception($"Parameter SecurePassword is null"),
                ParameterSetNames.SecurePassword when SecurePassword is not null => Utils.SecureStringToString(SecurePassword),
                ParameterSetNames.SecurePassword when SecurePassword is null => throw new Exception($"Parameter SecurePassword is null"),
                _ => throw new Exception($"Unsupported parameter set {ParameterSetName}"),
            };
        }

        protected override CmdletWorker CreateWorker() {
            return new ExpandWorker(this);
        }

        private class ExpandWorker(Expand7Zip cmdlet) : CmdletWorker {
            private readonly Expand7Zip _cmdlet = cmdlet;

            public override void Execute(CancellationToken token) {
                token.ThrowIfCancellationRequested();
                var targetPath = new FileInfo(Path.Combine(_cmdlet.SessionState.Path.CurrentFileSystemLocation.Path, _cmdlet.TargetPath)).FullName;
                var archiveFileName = new FileInfo(Path.Combine(_cmdlet.SessionState.Path.CurrentFileSystemLocation.Path, _cmdlet.ArchiveFileName)).FullName;

                var activity = $"Extracting \"{Path.GetFileName(archiveFileName)}\" to \"{targetPath}\"";
                var statusDescription = "Extracting";

                Write($"Extracting archive \"{archiveFileName}\"");

                // Reuse ProgressRecord instance instead of creating new one on each progress update
                Progress = new ProgressRecord(Environment.CurrentManagedThreadId, activity, statusDescription) { PercentComplete = 0 };

                using (var extractor = CreateExtractor(archiveFileName)) {
                    extractor.Extracting += (sender, args) => {
                        Progress.PercentComplete = args.PercentDone;
                        WriteProgress(Progress);
                    };

                    extractor.FileExtractionStarted += (sender, args) => {
                        statusDescription = $"Extracting file \"{args.FileInfo.FileName}\"";
                        Write(statusDescription);
                    };

                    extractor.ExtractArchive(targetPath);
                }

                Write("Extraction finished");
            }

            private SharpSevenZipExtractor CreateExtractor(string archiveFileName) {
                if (!string.IsNullOrEmpty(_cmdlet._password)) {
                    return new SharpSevenZipExtractor(archiveFileName, _cmdlet._password);
                } else {
                    return new SharpSevenZipExtractor(archiveFileName);
                }
            }
        }
    }
}