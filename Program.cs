using Microsoft.Diagnostics.Tracing;
using System.IO;
using System.CommandLine;
using System;
using Microsoft.Diagnostics.NETCore.Client;
using System.Threading.Tasks;

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
            var collectCommand = new Command("collect", "Collects a diagnostic trace from a currently running process") { processIdOption, diagnosticPortOption, outputFileNameOption };
            var convertCommand = new Command("convert", "Convers existing nettrace file into gcdump file") { inputFileNameArgument, outputFileNameOption };

            convertCommand.SetHandler(HandleConvert, inputFileNameArgument, outputFileNameOption);
            collectCommand.SetHandler(HandleCollect, processIdOption, diagnosticPortOption, outputFileNameOption);

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

        static async Task HandleCollect(int? processId, string? diagnosticPort, string? outputFileName)
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

            var eventPipeSession = diagnosticsClient.StartEventPipeSession(
                new EventPipeProvider("Microsoft-DotNETRuntimeMonoProfiler", System.Diagnostics.Tracing.EventLevel.Informational, 0xC900003),
                requestRundown: true,
                circularBufferMB: 1024);
            var source = new EventPipeEventSource(eventPipeSession.EventStream);
            var gcDumpFinished = new TaskCompletionSource();
            var buildTask = MonoMemoryGraphBuilder.Build(source, () => { gcDumpFinished.SetResult(); });
            await gcDumpFinished.Task;
            await eventPipeSession.StopAsync(default);
            var memoryGraph = await buildTask;
            GCHeapDump.WriteMemoryGraph(memoryGraph, outputFileName, "Mono");
        }
    }
}
