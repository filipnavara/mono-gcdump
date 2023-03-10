using Graphs;
using Microsoft.Diagnostics.Tracing;
using System;
using System.Collections.Generic;
using System.IO;

namespace GCHeapster
{
    class Program
    {
        static void Main(string[] args)
        {
            string traceFile = args[0];
            string outputFile = args.Length == 1 ? Path.ChangeExtension(traceFile, "gcdump") : args[1];

            List<GCHeapDumpObjectReferenceData> referenceDatas = new List<GCHeapDumpObjectReferenceData>();
            MemoryGraph memoryGraph = new MemoryGraph(10000);
            var vtableIdToTypeIndex = new Dictionary<long, NodeTypeIndex>();
            var objectIdToNodeIndex = new Dictionary<long, NodeIndex>();
            var rootBuilder = new MemoryNodeBuilder(memoryGraph, "[.NET Roots]");

            var source = new EventPipeEventSource(traceFile);
            var monoProfiler = new MonoProfilerTraceEventParser(source);

            memoryGraph.Is64Bit = source.PointerSize == 8;

            monoProfiler.MonoProfilerGCEvent += delegate (GCEventData data) { };
            monoProfiler.MonoProfilerGCHeapDumpStart += delegate (EmptyTraceData data) { };
            monoProfiler.MonoProfilerGCHeapDumpStop += delegate (EmptyTraceData data) { };
            monoProfiler.MonoProfilerGCHeapDumpObjectReferenceData += delegate (GCHeapDumpObjectReferenceData data)
            {
                referenceDatas.Add((GCHeapDumpObjectReferenceData)data.Clone());
            };

            monoProfiler.MonoProfilerGCRootRegister += delegate (GCRootRegisterData data)
            {

            };

            monoProfiler.MonoProfilerGCRootUnregister += delegate (GCRootUnregisterData data)
            {

            };

            monoProfiler.MonoProfilerGCRoots += delegate (GCRootsData data)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    var objectId = data.GetObjectID(i);
                    var addressId = data.GetAddressID(i);

                    if (!objectIdToNodeIndex.TryGetValue(objectId, out var nodeIndex))
                    {
                        nodeIndex = memoryGraph.CreateNode();
                        objectIdToNodeIndex.Add(objectId, nodeIndex);
                    }

                    rootBuilder.AddChild(nodeIndex);
                }
            };

            monoProfiler.MonoProfilerGCHeapDumpVTableClassReference += delegate (GCHeapDumpVTableClassReferenceData data)
            {
                if (!vtableIdToTypeIndex.TryGetValue(data.VTableID, out var typeIndex))
                {
                    typeIndex = memoryGraph.CreateType(data.ClassName, "FakeModule");
                    vtableIdToTypeIndex[data.VTableID] = typeIndex;
                }
            };

            source.Process();

            foreach (var referenceData in referenceDatas)
            {
                if (!objectIdToNodeIndex.TryGetValue(referenceData.ObjectID, out var nodeIndex))
                {
                    nodeIndex = memoryGraph.CreateNode();
                    objectIdToNodeIndex.Add(referenceData.ObjectID, nodeIndex);
                }

                var children = new GrowableArray<NodeIndex>(referenceData.Count);
                for (int i = 0; i < referenceData.Count; i++)
                {
                    var childObjectId = referenceData.GetReferencesObjectId(i);
                    if (!objectIdToNodeIndex.TryGetValue(childObjectId, out var childNodeIndex))
                    {
                        childNodeIndex = memoryGraph.CreateNode();
                        objectIdToNodeIndex.Add(childObjectId, childNodeIndex);
                    }
                    children.Add(childNodeIndex);
                }

                if (!memoryGraph.IsDefined(nodeIndex))
                {
                    memoryGraph.SetNode(nodeIndex, vtableIdToTypeIndex[referenceData.VTableID], (int)referenceData.ObjectSize, children);

                    // FIXME: Find a way to report some more meaningful roots
                    //rootBuilder.AddChild(nodeIndex);
                }
                else
                {
                    Console.WriteLine($"Duplicate object ID: {referenceData.ObjectID:X}");
                }
            }

            memoryGraph.RootIndex = rootBuilder.Build();
            memoryGraph.AllowReading();

            GCHeapDump.WriteMemoryGraph(memoryGraph, outputFile, "Mono");
        }
    }
}
