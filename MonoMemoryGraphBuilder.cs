﻿using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace MonoGCDump
{
    class MonoMemoryGraphBuilder
    {
        record GCRootRangeData(long Start, long End, string Name);
        record GCRootData(long ObjectId, long AddressId);
        record GCTypeData(long VTableId, string ClassName, long ModuleId);
        record GCObjectReferenceData(long ObjectId, long VTableId, int ObjectSize, long[] Children);

        class GCRootRangeComparer : IComparer<GCRootRangeData>
        {
            public int Compare(GCRootRangeData? x, GCRootRangeData? y)
            {
                if (x == y)
                    return 0;
                if (x == null)
                    return 1;
                if (y == null)
                    return -1;
                return x.Start.CompareTo(y.Start);
            }
        }

        public static async Task<MemoryGraph> Build(EventPipeEventSource source, MonoGCRootRangeTracker? rootRangeTracker = null, Action? stop = null)
        {
            var moduleMap = new Dictionary<long, string>();
            var typeData = new List<GCTypeData>();
            var objectReferenceData = new List<GCObjectReferenceData>();
            var rootData = new List<GCRootData>();

            var vtableIdToTypeIndex = new Dictionary<long, NodeTypeIndex>();
            var objectIdToNodeIndex = new Dictionary<long, NodeIndex>();

            var monoProfiler = new MonoProfilerTraceEventParser(source);
            var clrRundown = new ClrRundownTraceEventParser(source);

            TaskCompletionSource heapDumpStart = new TaskCompletionSource();

            rootRangeTracker ??= new MonoGCRootRangeTracker();

            rootRangeTracker.Attach(monoProfiler);

            clrRundown.LoaderModuleDCStop += delegate (ModuleLoadUnloadTraceData data)
            {
                moduleMap[data.ModuleID] = data.ModuleILPath;
            };

            monoProfiler.MonoProfilerGCHeapDumpStart += data => heapDumpStart.SetResult();

            monoProfiler.MonoProfilerGCHeapDumpStop += data => stop?.Invoke();

            monoProfiler.MonoProfilerGCHeapDumpObjectReferenceData += data =>
            {
                long[] children;
                if (data.Count > 0)
                {
                    children = new long[data.Count];
                    for (int i = 0; i < data.Count; i++)
                    {
                        children[i] = data.GetReferencesObjectId(i);
                    }
                }
                else
                {
                    children = Array.Empty<long>();
                }
                objectReferenceData.Add(new GCObjectReferenceData(data.ObjectID, data.VTableID, (int)data.ObjectSize, children));
            };

            monoProfiler.MonoProfilerGCRoots += delegate (GCRootsData data)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    rootData.Add(new GCRootData(data.GetObjectID(i), data.GetAddressID(i)));
                }
            };

            monoProfiler.MonoProfilerGCHeapDumpVTableClassReference += delegate (GCHeapDumpVTableClassReferenceData data)
            {
                typeData.Add(new GCTypeData(data.VTableID, data.ClassName, data.ModuleID));
            };

            var processTask = Task.Run(() => source.Process());

            if (await Task.WhenAny(heapDumpStart.Task, Task.Delay(5000)) != heapDumpStart.Task)
            {
                source.StopProcessing();
                throw new TimeoutException("Heap dump didn't start within 5 seconds");
            }

            await processTask;

            rootRangeTracker.Detach(monoProfiler);

            // TODO: Better estimate
            var memoryGraph = new MemoryGraph(objectReferenceData.Count + 1024);
            memoryGraph.Is64Bit = source.PointerSize == 8;
            var rootBuilder = new MemoryNodeBuilder(memoryGraph, "[.NET Roots]");

            foreach (var type in typeData)
            {
                if (!vtableIdToTypeIndex.TryGetValue(type.VTableId, out var typeIndex))
                {
                    if (!moduleMap.TryGetValue(type.ModuleId, out string? moduleName))
                        moduleName = $"(Module 0x{type.ModuleId:x})";

                    typeIndex = memoryGraph.CreateType(type.ClassName, moduleName);
                    vtableIdToTypeIndex[type.VTableId] = typeIndex;
                }
            }

            foreach (var objectReference in objectReferenceData)
            {
                if (!objectIdToNodeIndex.TryGetValue(objectReference.ObjectId, out var nodeIndex))
                {
                    nodeIndex = memoryGraph.CreateNode();
                    objectIdToNodeIndex.Add(objectReference.ObjectId, nodeIndex);
                }

                if (!memoryGraph.IsDefined(nodeIndex))
                {
                    var children = new GrowableArray<NodeIndex>(objectReference.Children.Length);
                    for (int i = 0; i < objectReference.Children.Length; i++)
                    {
                        var childObjectId = objectReference.Children[i];
                        if (!objectIdToNodeIndex.TryGetValue(childObjectId, out var childNodeIndex))
                        {
                            childNodeIndex = memoryGraph.CreateNode();
                            objectIdToNodeIndex.Add(childObjectId, childNodeIndex);
                        }
                        children.Add(childNodeIndex);
                    }

                    memoryGraph.SetNode(nodeIndex, vtableIdToTypeIndex[objectReference.VTableId], objectReference.ObjectSize, children);
                }
                else
                {
                    Console.WriteLine($"Duplicate object ID: {objectReference.ObjectId:X}");
                }
            }

            if (rootData.Count == 0)
            {
                Console.WriteLine($"Missing GC Roots data");

                // Fallback to reporting every object as a root if we don't have GCRoots events
                foreach (var objectReference in objectReferenceData)
                {
                    if (objectIdToNodeIndex.TryGetValue(objectReference.ObjectId, out var nodeIndex))
                    {
                        rootBuilder.AddChild(nodeIndex);
                    }
                }
            }
            else
            {
                foreach (var root in rootData)
                {
                    if (objectIdToNodeIndex.TryGetValue(root.ObjectId, out var nodeIndex))
                    {
                        // Find
                        var rootRange = rootRangeTracker.FindRootRange(root.AddressId);
                        rootBuilder.FindOrCreateChild(rootRange?.Name ?? "Other Roots").AddChild(nodeIndex);
                    }
                }
            }

            memoryGraph.RootIndex = rootBuilder.Build();
            memoryGraph.AllowReading();

            return memoryGraph;
        }
    }
}
