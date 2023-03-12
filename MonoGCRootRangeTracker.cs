using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoGCDump
{
    internal class MonoGCRootRangeTracker
    {
        public record GCRootRangeData(long Start, long End, string Name);

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

        private List<GCRootRangeData> rootRangeData = new();
        private static GCRootRangeComparer rootRangeComparer = new();

        public void Attach(MonoProfilerTraceEventParser traceEventParser)
        {
            traceEventParser.MonoProfilerGCRootRegister += TraceEventParser_MonoProfilerGCRootRegister;
            traceEventParser.MonoProfilerGCRootUnregister += TraceEventParser_MonoProfilerGCRootUnregister;
        }

        public void Detach(MonoProfilerTraceEventParser traceEventParser)
        {
            traceEventParser.MonoProfilerGCRootRegister -= TraceEventParser_MonoProfilerGCRootRegister;
            traceEventParser.MonoProfilerGCRootUnregister -= TraceEventParser_MonoProfilerGCRootUnregister;
        }

        private void TraceEventParser_MonoProfilerGCRootRegister(GCRootRegisterData data)
        {
            // FIXME: Unique root names?
            var rootRange = new GCRootRangeData(data.RootID, data.RootID + data.RootSize, data.RootKeyName);
            int newIndex = rootRangeData.BinarySearch(rootRange, rootRangeComparer);
            if (newIndex < 0)
            {
                rootRangeData.Insert(~newIndex, rootRange);
            }
        }

        private void TraceEventParser_MonoProfilerGCRootUnregister(GCRootUnregisterData data)
        {
            // TODO: Binary search
            var rootRangeIndex = rootRangeData.FindIndex(rootRange => data.RootID == rootRange.Start);
            if (rootRangeIndex >= 0)
            {
                rootRangeData.RemoveAt(rootRangeIndex);
            }
        }

        public GCRootRangeData? FindRootRange(long address)
        {
            return rootRangeData.Find(rootRange => address >= rootRange.Start && address < rootRange.End);
        }
    }
}
