# mono-gcdump

## Introduction

The mono-gcdump tool is a close cousin to the [dotnet-gcdump tool](https://github.com/dotnet/diagnostics/blob/main/documentation/dotnet-gcdump-instructions.md). It produces .gcdump files from MonoVM processes that capture the managed heap state for analysis. The gcdump files can be viewed in Visual Studio, or [PerfView](https://github.com/microsoft/PerfView).

## Usage

There are two ways to use the tool. The first one is to capture a .nettrace file using the [dotnet-trace tool](https://github.com/dotnet/diagnostics/blob/main/documentation/dotnet-trace-instructions.md) with the `collect --providers Microsoft-DotNETRuntimeMonoProfiler:0xC900003:4` option. The second one is using `mono-gcdump collect -p <process id>` on a running process, or `mono-gcdump collect --diagnostic-port <diagnostic port>` if using [dotnet-dsrouter](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter) with a mobile application.

## Example: Dumping heap of Android application

- Install the `dotnet-dsrouter` tool using `dotnet tool install --global dotnet-dsrouter`.
- Connect to your Android device using `adb` and run `adb shell setprop debug.mono.profile '127.0.0.1:9000,suspend'`.
- Run your application on the device.
- Make sure the `ANDROID_SDK_ROOT` environment variable points to the Android SDK location.
- Run `dotnet-dsrouter server-server -ipcs ~/mylocalport -tcps 127.0.0.1:9000 --forward-port Android` (on Windows use `mylocalport` instead of `~/mylocalport`)
- Run `mono-gcdump collect --diagnostic-port ~/mylocalport,connect -o memory.gcdump`.
- Open the generated `memory.gcdump` in your tool of choice.

Follow the [dotnet-dsrouter documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dsrouter) for more details on how to setup the diagnostic ports, and for troubleshooting common issues.

## License

The tool is licensed under the [MIT license](LICENSE.TXT). It uses portions of the tools and libraries from [dotnet/diagnostics](https://github.com/dotnet/diagnostics) and [PerfView](https://github.com/microsoft/PerfView).
