using Graphs;
using Microsoft.Diagnostics.Tracing;
using System.Collections.Generic;
using System.Diagnostics;

namespace GCHeapster
{
    class Program
    {
        static void Main(string[] args)
        {
            string traceFile = args[0];
            string outputFile = args[1];

            List<GCHeapDumpObjectReferenceData> referenceDatas = new List<GCHeapDumpObjectReferenceData>();
            MemoryGraph memoryGraph = new MemoryGraph(10000);
            var vtableIdToTypeIndex = new Dictionary<long, NodeTypeIndex>();
            var objectIdToNodeIndex = new Dictionary<long, NodeIndex>();
            var rootBuilder = new MemoryNodeBuilder(memoryGraph, "[.NET Roots]");

            var source = new EventPipeEventSource(traceFile);
            var monoProfiler = new MonoProfilerTraceEventParser(source);

            monoProfiler.MonoProfilerGCEvent += delegate (GCEventData data) { };
            monoProfiler.MonoProfilerGCHeapDumpStart += delegate (EmptyTraceData data) { };
            monoProfiler.MonoProfilerGCHeapDumpStop += delegate (EmptyTraceData data) { };
            monoProfiler.MonoProfilerGCHeapDumpObjectReferenceData += delegate (GCHeapDumpObjectReferenceData data)
            {
                referenceDatas.Add((GCHeapDumpObjectReferenceData)data.Clone());
            };

            monoProfiler.MonoProfilerGCHeapDumpVTableClassReference += delegate (GCHeapDumpVTableClassReferenceData data)
            {
                if (!vtableIdToTypeIndex.TryGetValue(data.VTableID, out var typeIndex))
                {
                    typeIndex = memoryGraph.CreateType(data.ClassName);
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
                        nodeIndex = memoryGraph.CreateNode();
                        objectIdToNodeIndex.Add(childObjectId, childNodeIndex);
                    }
                    children[i] = childNodeIndex;
                }

                Debug.WriteLine($"SetNode({referenceData.ObjectID})");
                memoryGraph.SetNode(nodeIndex, vtableIdToTypeIndex[referenceData.VTableID], (int)referenceData.ObjectSize, children);

                // FIXME: Find a way to report some more meaningful roots
                rootBuilder.AddChild(nodeIndex);
            }

            memoryGraph.RootIndex = rootBuilder.Build();
            memoryGraph.AllowReading();

            GCHeapDump.WriteMemoryGraph(memoryGraph, "test.gcdump", "Mono");
        }
    }
}
