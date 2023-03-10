using System;
using System.Text;
using Microsoft.Diagnostics.Tracing;

namespace GCHeapster
{
    public sealed class MonoProfilerTraceEventParser : TraceEventParser
    {
        public static readonly string ProviderName = "Microsoft-DotNETRuntimeMonoProfiler";
        public static readonly Guid ProviderGuid = new Guid(unchecked((int)0x7F442D82), unchecked((short)0x0F1D), unchecked((short)0x5155), 0x4B, 0x8C, 0x15, 0x29, 0xEB, 0x2E, 0x31, 0xC2);

        public enum Keywords : long
        {
            GC = 0x1,
            GCHeapDump = 0x100000,
            GCHeapCollect = 0x800000,
            GCHeapDumpVTableClassReference = 0x8000000
        };

        public MonoProfilerTraceEventParser(TraceEventSource source) : base(source) { }

        public event Action<GCEventData> MonoProfilerGCEvent
        {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new GCEventData(value,38, 1, "MonoProfiler", MonoProfilerTaskGuid, 55, "GCEventData", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 38, ProviderGuid);
                source.UnregisterEventTemplate(value, 55, MonoProfilerTaskGuid);
            }
        }

        public event Action<EmptyTraceData> MonoProfilerGCHeapDumpStart {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 51, 1, "MonoProfiler", MonoProfilerTaskGuid, 68, "GCHeapDumpStart", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 51, ProviderGuid);
                source.UnregisterEventTemplate(value, 68, MonoProfilerTaskGuid);
            }
        }

        public event Action<EmptyTraceData> MonoProfilerGCHeapDumpStop {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new EmptyTraceData(value, 52, 1, "MonoProfiler", MonoProfilerTaskGuid, 69, "GCHeapDumpStop", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 52, ProviderGuid);
                source.UnregisterEventTemplate(value, 69, MonoProfilerTaskGuid);
            }
        }

        public event Action<GCHeapDumpVTableClassReferenceData> MonoProfilerGCHeapDumpVTableClassReference {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new GCHeapDumpVTableClassReferenceData(value, 63, 1, "MonoProfiler", MonoProfilerTaskGuid, 80, "GCHeapDumpVTableClassReference", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 63, ProviderGuid);
                source.UnregisterEventTemplate(value, 80, MonoProfilerTaskGuid);
            }
        }

        public event Action<GCHeapDumpObjectReferenceData> MonoProfilerGCHeapDumpObjectReferenceData {
            add
            {
                // action, eventid, taskid, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName
                source.RegisterEventTemplate(new GCHeapDumpObjectReferenceData(value, 53, 1, "MonoProfiler", MonoProfilerTaskGuid, 70, "MonoProfilerGCHeapDumpObjectReference", ProviderGuid, ProviderName));
            }
            remove
            {
                source.UnregisterEventTemplate(value, 53, ProviderGuid);
                source.UnregisterEventTemplate(value, 70, MonoProfilerTaskGuid);
            }
        }

        private const TraceEventID GCEventID = (TraceEventID)38;
        private const TraceEventID GCHeapDumpStart = (TraceEventID)51;
        private const TraceEventID GCHeapDumpStop = (TraceEventID)52;
        private const TraceEventID GCHeapDumpObjectReferenceID = (TraceEventID)53;
        private const TraceEventID GCHeapDumpVTableClassReferenceID = (TraceEventID)63;

        protected override string GetProviderName() { return ProviderName; }

        public static Guid GetProviderGuid() { return ProviderGuid; }

        public static ulong GetKeywords() { return (ulong)(Keywords.GC | Keywords.GCHeapDumpVTableClassReference); }

        static private volatile TraceEvent[] s_templates;

        protected override void EnumerateTemplates(Func<string, string, EventFilterResponse> eventsToObserve, Action<TraceEvent> callback)
        {
            if (s_templates == null)
            {
                var templates = new TraceEvent[5];
                templates[0] = new GCEventData(null, 38, 1, "MonoProfiler", MonoProfilerTaskGuid, 55, "GCEventData", ProviderGuid, ProviderName);
                templates[1] = new EmptyTraceData(null, 51, 1, "MonoProfiler", MonoProfilerTaskGuid, 68, "GCHeapDumpStart", ProviderGuid, ProviderName);
                templates[2] = new EmptyTraceData(null, 52, 1, "MonoProfiler", MonoProfilerTaskGuid, 69, "GCHeapDumpStop", ProviderGuid, ProviderName);
                templates[3] = new GCHeapDumpObjectReferenceData(null, 53, 1, "MonoProfiler", MonoProfilerTaskGuid, 70, "MonoProfilerGCHeapDumpObjectReference", ProviderGuid, ProviderName);
                templates[4] = new GCHeapDumpVTableClassReferenceData(null, 63, 1, "MonoProfiler", MonoProfilerTaskGuid, 80, "GCHeapDumpVTableClassReference", ProviderGuid, ProviderName);
                s_templates = templates;
            }
            foreach (var template in s_templates)
                if (eventsToObserve == null || eventsToObserve(template.ProviderName, template.EventName) == EventFilterResponse.AcceptEvent)
                    callback(template);
        }

        private static readonly Guid MonoProfilerTaskGuid = new Guid(unchecked((int)0x7EC39CC6), unchecked((short)0xC9E3), unchecked((short)0x4328), 0x9B, 0x32, 0xCA, 0x6C, 0x5E, 0xC0, 0xEF, 0x31);
    }

    public enum GCTypeMap : byte
    {
        Start = 0,
        End = 5,
        PreStopWorld = 6,
        PostStopWorld = 7,
        PreStartWorld = 8,
        PostStartWorld = 9,
        PreStopWorldLocked = 10,
        PostStartWorldUnLocked = 11
    };

    public sealed class GCEventData : TraceEvent
    {
        internal GCEventData(Action<GCEventData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }

        public GCTypeMap GCType { get { return (GCTypeMap)GetByteAt(0); } }

        public int GCGeneration { get { return GetInt32At(1); } }

        protected override void Dispatch()
        {
            Action(this);
        }
        protected override Delegate Target {
            get { return Action; }
            set { Action = (Action<GCEventData>)value; }
        }
        protected override void Validate()
        {
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttrib(sb, "GCType", GCType);
            XmlAttribHex(sb, "GCGeneration", GCGeneration);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "GCType", "GCGeneration" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return GCType;
                case 1:
                    return GCGeneration;
                default:
                    return null;
            }
        }

        private event Action<GCEventData> Action;
    }

    public sealed class GCHeapDumpObjectReferenceData : TraceEvent
    {
        internal GCHeapDumpObjectReferenceData(Action<GCHeapDumpObjectReferenceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }

        public long ObjectID { get { return GetInt64At(0); } }

        public long VTableID { get { return GetInt64At(8); } }

        public long ObjectSize { get { return GetInt64At(16); } }

        public byte ObjectGeneration { get { return (byte)GetByteAt(24); } }

        public int Count { get { return GetInt32At(25); } }

        // FIXME: 32-bit
        public uint GetReferencesOffset(int index) => (uint)GetInt32At(29 + 8 * index);

        // FIXME: 32-bit
        public long GetReferencesObjectId(int index) => (uint)GetInt64At(29 + 4 + 8 * index);

        protected override void Dispatch()
        {
            Action(this);
        }
        protected override Delegate Target {
            get { return Action; }
            set { Action = (Action<GCHeapDumpObjectReferenceData>)value; }
        }
        protected override void Validate()
        {
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "ObjectID", ObjectID);
            XmlAttribHex(sb, "VTableID", VTableID);
            XmlAttribHex(sb, "ObjectSize", ObjectSize);
            XmlAttribHex(sb, "ObjectGeneration", ObjectGeneration);
            XmlAttribHex(sb, "Count", Count);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "ObjectID", "VTableID", "ObjectSize", "ObjectGeneration", "Count" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return ObjectID;
                case 1:
                    return VTableID;
                case 2:
                    return ObjectSize;
                case 3:
                    return ObjectGeneration;
                case 4:
                    return Count;
                default:
                    return null;
            }
        }

        private event Action<GCHeapDumpObjectReferenceData> Action;
    }

    public sealed class GCHeapDumpVTableClassReferenceData : TraceEvent
    {
        internal GCHeapDumpVTableClassReferenceData(Action<GCHeapDumpVTableClassReferenceData> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
            : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
        {
            Action = action;
        }

        public long VTableID { get { return GetInt64At(0); } }

        public long ClassID { get { return GetInt64At(8); } }

        public long ModuleID { get { return GetInt64At(16); } }

        public string ClassName{ get { return GetUnicodeStringAt(24); } }

        protected override void Dispatch()
        {
            Action(this);
        }
        protected override Delegate Target {
            get { return Action; }
            set { Action = (Action<GCHeapDumpVTableClassReferenceData>)value; }
        }
        protected override void Validate()
        {
        }

        public override StringBuilder ToXml(StringBuilder sb)
        {
            Prefix(sb);
            XmlAttribHex(sb, "VTableID", VTableID);
            XmlAttribHex(sb, "ClassID", ClassID);
            XmlAttribHex(sb, "ModuleID", ModuleID);
            XmlAttrib(sb, "ClassName", ClassName);
            sb.Append("/>");
            return sb;
        }

        public override string[] PayloadNames {
            get
            {
                if (payloadNames == null)
                {
                    payloadNames = new string[] { "VTableID", "ClassID", "ModuleID", "ClassName" };
                }

                return payloadNames;
            }
        }

        public override object PayloadValue(int index)
        {
            switch (index)
            {
                case 0:
                    return VTableID;
                case 1:
                    return ClassID;
                case 2:
                    return ModuleID;
                case 3:
                    return ClassName;
                default:
                    return null;
            }
        }

        private event Action<GCHeapDumpVTableClassReferenceData> Action;
    }
}
