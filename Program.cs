using Microsoft.Diagnostics.Tracing;
using System.IO;
using System.CommandLine;
using System;
using Microsoft.Diagnostics.NETCore.Client;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

#nullable enable

namespace MonoGCDump
{
    class Program
    {
        static int Main(string[] args)
        {
            var inputFileNameArgument = new Argument<string>("input-filename", "The path to a nettrace file to be converted.");
            var outputFileNameOption = new Option<string>(new[] { "-o", "--output" }, description: "Output filename.");
            var processIdOption = new Option<int?>(new[] { "-p", "--process-id" }, "The process id to collect the gcdump from.");
            var diagnosticPortOption = new Option<string?>(new[] { "--diagnostic-port" }, "The path to a diagnostic port to be used.");
            var interactiveOption = new Option<bool?>(new[] { "--interactive" }, "Run in interactive mode to collect more GC dumps.");

            var collectCommand = new Command("collect", "Collects a diagnostic trace from a currently running process")
            {
                processIdOption,
                diagnosticPortOption,
                outputFileNameOption,
                interactiveOption
            };
            collectCommand.SetHandler(HandleCollect, processIdOption, diagnosticPortOption, outputFileNameOption, interactiveOption);

            var convertCommand = new Command("convert", "Converts existing nettrace file into gcdump file")
            {
                inputFileNameArgument,
                outputFileNameOption
            };
            convertCommand.SetHandler(HandleConvert, inputFileNameArgument, outputFileNameOption);

            return new RootCommand { convertCommand, collectCommand }.Invoke(args);
        }

        static async Task HandleConvert(string inputFileName, string? outputFileName)
        {
            outputFileName ??= Path.ChangeExtension(inputFileName, "gcdump");
            var source = new EventPipeEventSource(inputFileName);
            var memoryGraph = await MonoMemoryGraphBuilder.Build(source);
            GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileName, "Mono");
            Console.WriteLine($"Converted {inputFileName} to {outputFileName}");
        }

        static async Task HandleCollect(int? processId, string? diagnosticPort, string? outputFileName, bool? interactive)
        {
            if (processId is null && diagnosticPort is null)
            {
                Console.WriteLine("Either a process id or a diagnostic port must be specified.");
                return;
            }

            outputFileName ??= DateTime.UtcNow.ToString("yyyyMMdd'_'HHssmm'.gcdump'");

            DiagnosticsClient diagnosticsClient;

            if (processId is not null)
            {
                diagnosticsClient = new DiagnosticsClient(processId.Value);
            }
            else
            {
                if (!IpcEndpointConfig.TryParse(diagnosticPort, out var config))
                {
                    Console.WriteLine("Invalid diagnostic port.");
                    return;
                }
                diagnosticsClient = new DiagnosticsClient(config);
            }

            if (interactive == true)
            {
                // In interactive mode we run a logging event session to precisely track
                // GC root registations and unregistrations. We also resume the process if
                // it was started in suspended mode.

                int outputFileNameCounter = 1;
                using var eventPipeLogSession = diagnosticsClient.StartEventPipeSession(
                    new EventPipeProvider("Microsoft-DotNETRuntimeMonoProfiler", System.Diagnostics.Tracing.EventLevel.Informational, 0x4000000),
                    requestRundown: false,
                    circularBufferMB: 1024);
                using var source = new EventPipeEventSource(eventPipeLogSession.EventStream);
                var monoProfilerEventParser = new MonoProfilerTraceEventParser(source);
                var rootRangeTracker = new MonoGCRootRangeTracker();
                rootRangeTracker.Attach(monoProfilerEventParser);

                await diagnosticsClient.ResumeRuntimeAsync(default);

                var eventProcessTask = Task.Run(() => source.Process());
                var readKeyTask = Task.Run(() => Console.ReadKey(true));

                Console.WriteLine("Press [Escape] to quit, or [d] to collect GC heap dump");

                while (true)
                {
                    Task completedTask = await Task.WhenAny(eventProcessTask, readKeyTask);
                    if (completedTask == eventProcessTask)
                    {
                        break;
                    }
                    else
                    {
                        if (readKeyTask.Result.Key == ConsoleKey.Escape)
                        {
                            Console.WriteLine("Quitting");
                            await eventPipeLogSession.StopAsync(default);
                        }
                        else if (readKeyTask.Result.Key == ConsoleKey.D)
                        {
                            string outputFileName1 = $"{Path.GetDirectoryName(outputFileName)}{Path.GetFileNameWithoutExtension(outputFileName)}_{outputFileNameCounter}{Path.GetExtension(outputFileName)}";
                            Console.WriteLine($"Dumping GC heap to file {outputFileName1}");
                            try
                            {
                                await CollectDump(diagnosticsClient, outputFileName1, rootRangeTracker);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Collecting dump failed: {e}");
                            }
                        }

                        readKeyTask = Task.Run(() => Console.ReadKey(true));
                    }
                }

                rootRangeTracker.Detach(monoProfilerEventParser);
            }
            else
            {
                // One-shot live GC dump
                await CollectDump(diagnosticsClient, outputFileName);
            }
        }

        private static async Task CollectDump(
            DiagnosticsClient diagnosticsClient,
            string outputFileName,
            MonoGCRootRangeTracker? rootRangeTracker = null)
        {
            using var eventPipeSession = diagnosticsClient.StartEventPipeSession(
                new EventPipeProvider("Microsoft-DotNETRuntimeMonoProfiler", System.Diagnostics.Tracing.EventLevel.Informational, 0xC900003),
                requestRundown: true,
                circularBufferMB: 1024);
            using var source = new EventPipeEventSource(eventPipeSession.EventStream);
            var gcDumpFinished = new TaskCompletionSource();
            var buildTask = MonoMemoryGraphBuilder.Build(source, rootRangeTracker, () => { gcDumpFinished.SetResult(); });
            try
            {
                await Task.WhenAny(gcDumpFinished.Task, buildTask);
            }
            finally
            {
                await eventPipeSession.StopAsync(default);
            }
            var memoryGraph = await buildTask;
            GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileName, "Mono");
        }
    }
}
