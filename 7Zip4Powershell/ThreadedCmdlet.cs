// Ignore Spelling: Cmdlet

using System;
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Threading;
using SharpSevenZip;

namespace SevenZip4PowerShell {
    public abstract class ThreadedCmdlet : PSCmdlet {
        protected abstract CmdletWorker CreateWorker();
        private Thread? _thread;
        private readonly CancellationTokenSource tokenSource = new();

        protected override void EndProcessing() {
            SharpSevenZipBase.SetLibraryPath(Utils.SevenZipLibraryPath);

            var worker = CreateWorker();

            _thread ??= StartBackgroundThread(worker, tokenSource.Token);

            foreach (var o in worker.Queue.GetConsumingEnumerable()) {
                if (o is ProgressRecord record) {
                    WriteProgress(record);
                } else if (o is ErrorRecord errorRecord) {
                    WriteError(errorRecord);
                } else if (o is string @string) {
                    WriteVerbose(@string);
                } else {
                    WriteObject(o);
                }
            }

            _thread.Join();

            try {
                worker.Progress.StatusDescription = "Finished";
                worker.Progress.RecordType = ProgressRecordType.Completed;
                WriteProgress(worker.Progress);
            } catch (NullReferenceException) {
                // Possible bug in PowerShell 7.4.0 leading to a null reference exception being thrown on ProgressPane completion
                // This is not happening on PowerShell 5.1
            }
        }

        private static Thread StartBackgroundThread(CmdletWorker worker, CancellationToken token) {
            var thread = new Thread(() => {
                try {
                    worker.Execute(token);
                } catch (Exception ex) {
                    worker.Queue.Add(new ErrorRecord(ex, "7Zip4PowerShellException", ErrorCategory.NotSpecified, worker));
                }
                finally {
                    worker.Queue.CompleteAdding();
                }
            }) { IsBackground = true };
            thread.Start();
            return thread;
        }

        protected override void StopProcessing() {
            if (tokenSource.Token.CanBeCanceled && !tokenSource.IsCancellationRequested) {
                tokenSource.Cancel();
            }
        }
    }

    public abstract class CmdletWorker {
        public BlockingCollection<object> Queue { get; } = [];

        public ProgressRecord Progress { get; set; }

        public CmdletWorker() {
            // NOTE: In the method of ThreadedCmdlet.EndProcessing() of this type, there is a note for a possible bug in PowerShell 7.4.0
            //       leading to a null reference exception. This is because the CmdletWorker.Progress property has not been assigned and
            //       is attempted to be called anyway, which is why that exception has been thrown. To alleviate this issue, add a
            //       temporary default progress record for startup of the worker.
            Progress = new ProgressRecord(Environment.CurrentManagedThreadId, "Starting Thread", "Starting") { PercentComplete = 0 };
        }

        protected void Write(string text) {
            Queue.Add(text);
        }

        protected void WriteProgress(ProgressRecord progressRecord) {
            Queue.Add(progressRecord);
        }

        public abstract void Execute(CancellationToken token);
    }
}