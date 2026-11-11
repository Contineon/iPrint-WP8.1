// Minimal SNMPv1 client (BER encode/decode) over WinRT DatagramSocket.
// Logic follows epson-net-snmp.c from Epson ESC/P-R driver 1.8.10 (GPL-2.0+):
// community "public", UDP 161, GetRequest / GetNextRequest, one varbind per request.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace iprint.Escpr
{
    public enum SnmpValueType : byte
    {
        Integer = 0x02,
        OctetString = 0x04,
        Null = 0x05,
        ObjectId = 0x06,
        IpAddress = 0x40,
        Counter = 0x41,
        Gauge = 0x42,
        TimeTicks = 0x43,
        NoSuchObject = 0x80,
        NoSuchInstance = 0x81,
        EndOfMibView = 0x82
    }

    public sealed class SnmpResponse
    {
        public string SourceAddress { get; set; }
        public int RequestId { get; set; }
        public int ErrorStatus { get; set; }
        public string Oid { get; set; }
        public SnmpValueType ValueType { get; set; }
        public byte[] Value { get; set; }

        public bool IsOk
        {
            get
            {
                return ErrorStatus == 0 && ValueType != SnmpValueType.NoSuchObject &&
                       ValueType != SnmpValueType.NoSuchInstance && ValueType != SnmpValueType.EndOfMibView;
            }
        }

        public string AsString()
        {
            if (Value == null) return string.Empty;
            return Encoding.UTF8.GetString(Value, 0, Value.Length);
        }

        public long AsInteger()
        {
            if (Value == null || Value.Length == 0) return 0;
            long v = (Value[0] & 0x80) != 0 ? -1 : 0;
            foreach (var b in Value) v = (v << 8) | b;
            return v;
        }
    }

    public static class SnmpCodec
    {
        public const byte PduGet = 0xA0;
        public const byte PduGetNext = 0xA1;
        public const byte PduResponse = 0xA2;
        public const string Community = "public";

        static int _requestId = new Random().Next(1, 0x3FFFFFFF);

        public static int NextRequestId()
        {
            return Interlocked.Increment(ref _requestId) & 0x7FFFFFFF;
        }

        // ---------------- encoding ----------------

        static void WriteLength(List<byte> o, int len)
        {
            if (len < 0x80) { o.Add((byte)len); return; }
            var tmp = new List<byte>();
            while (len > 0) { tmp.Insert(0, (byte)(len & 0xFF)); len >>= 8; }
            o.Add((byte)(0x80 | tmp.Count));
            o.AddRange(tmp);
        }

        static byte[] Tlv(byte tag, byte[] content)
        {
            var o = new List<byte>(content.Length + 6);
            o.Add(tag);
            WriteLength(o, content.Length);
            o.AddRange(content);
            return o.ToArray();
        }

        static byte[] EncodeInteger(long v)
        {
            var bytes = new List<byte>();
            do
            {
                bytes.Insert(0, (byte)(v & 0xFF));
                v >>= 8;
            } while (!((v == 0 && (bytes[0] & 0x80) == 0) || (v == -1 && (bytes[0] & 0x80) != 0)));
            return Tlv(0x02, bytes.ToArray());
        }

        public static byte[] EncodeOid(string oid)
        {
            var arcs = oid.Trim('.').Split('.').Select(s => uint.Parse(s)).ToArray();
            if (arcs.Length < 2) throw new ArgumentException("bad oid " + oid);
            var o = new List<byte>();
            o.Add((byte)(arcs[0] * 40 + arcs[1]));
            for (int i = 2; i < arcs.Length; i++)
            {
                uint a = arcs[i];
                var tmp = new List<byte>();
                tmp.Insert(0, (byte)(a & 0x7F));
                a >>= 7;
                while (a > 0) { tmp.Insert(0, (byte)(0x80 | (a & 0x7F))); a >>= 7; }
                o.AddRange(tmp);
            }
            return Tlv(0x06, o.ToArray());
        }

        static byte[] Concat(params byte[][] parts)
        {
            var o = new List<byte>();
            foreach (var p in parts) o.AddRange(p);
            return o.ToArray();
        }

        public static byte[] BuildRequest(byte pduType, int requestId, string oid)
        {
            var varbind = Tlv(0x30, Concat(EncodeOid(oid), new byte[] { 0x05, 0x00 }));
            var varbindList = Tlv(0x30, varbind);
            var pdu = Tlv(pduType, Concat(EncodeInteger(requestId), EncodeInteger(0), EncodeInteger(0), varbindList));
            var community = Tlv(0x04, Encoding.UTF8.GetBytes(Community));
            return Tlv(0x30, Concat(EncodeInteger(0), community, pdu));
        }

        // ---------------- decoding ----------------

        static bool ReadTlv(byte[] buf, ref int pos, int end, out byte tag, out int start, out int len)
        {
            tag = 0; start = 0; len = 0;
            if (pos + 2 > end) return false;
            tag = buf[pos++];
            int l = buf[pos++];
            if ((l & 0x80) != 0)
            {
                int n = l & 0x7F;
                if (n == 0 || n > 4 || pos + n > end) return false;
                l = 0;
                for (int i = 0; i < n; i++) l = (l << 8) | buf[pos++];
            }
            if (l < 0 || pos + l > end) return false;
            start = pos; len = l; pos += l;
            return true;
        }

        static long DecodeInt(byte[] b, int start, int len)
        {
            if (len == 0) return 0;
            long v = (b[start] & 0x80) != 0 ? -1 : 0;
            for (int i = 0; i < len; i++) v = (v << 8) | b[start + i];
            return v;
        }

        public static string DecodeOid(byte[] b, int start, int len)
        {
            if (len == 0) return string.Empty;
            var sb = new StringBuilder();
            int first = b[start];
            sb.Append(first / 40).Append('.').Append(first % 40);
            uint v = 0;
            for (int i = 1; i < len; i++)
            {
                v = (v << 7) | (uint)(b[start + i] & 0x7F);
                if ((b[start + i] & 0x80) == 0) { sb.Append('.').Append(v); v = 0; }
            }
            return sb.ToString();
        }

        public static SnmpResponse Parse(byte[] buf)
        {
            int pos = 0; byte tag; int s, l;
            if (!ReadTlv(buf, ref pos, buf.Length, out tag, out s, out l) || tag != 0x30) return null;
            int end = s + l; pos = s;
            if (!ReadTlv(buf, ref pos, end, out tag, out s, out l) || tag != 0x02) return null; // version
            if (!ReadTlv(buf, ref pos, end, out tag, out s, out l) || tag != 0x04) return null; // community
            if (!ReadTlv(buf, ref pos, end, out tag, out s, out l) || tag != PduResponse) return null;
            int pduEnd = s + l; pos = s;
            var r = new SnmpResponse();
            if (!ReadTlv(buf, ref pos, pduEnd, out tag, out s, out l)) return null;
            r.RequestId = (int)DecodeInt(buf, s, l);
            if (!ReadTlv(buf, ref pos, pduEnd, out tag, out s, out l)) return null;
            r.ErrorStatus = (int)DecodeInt(buf, s, l);
            if (!ReadTlv(buf, ref pos, pduEnd, out tag, out s, out l)) return null; // error index
            if (!ReadTlv(buf, ref pos, pduEnd, out tag, out s, out l) || tag != 0x30) return null; // varbind list
            int vlEnd = s + l; pos = s;
            if (!ReadTlv(buf, ref pos, vlEnd, out tag, out s, out l) || tag != 0x30) return null; // varbind
            int vbEnd = s + l; pos = s;
            if (!ReadTlv(buf, ref pos, vbEnd, out tag, out s, out l) || tag != 0x06) return null;
            r.Oid = DecodeOid(buf, s, l);
            if (!ReadTlv(buf, ref pos, vbEnd, out tag, out s, out l)) return null;
            r.ValueType = (SnmpValueType)tag;
            r.Value = new byte[l];
            Array.Copy(buf, s, r.Value, 0, l);
            return r;
        }
    }

    /// <summary>
    /// UDP SNMP session. One instance can send many requests and collects all replies
    /// (needed for broadcast discovery).
    /// </summary>
    public sealed class SnmpSession : IDisposable
    {
        public const string Port = "161";

        readonly DatagramSocket _socket = new DatagramSocket();
        readonly object _lock = new object();
        readonly Dictionary<int, TaskCompletionSource<SnmpResponse>> _pending =
            new Dictionary<int, TaskCompletionSource<SnmpResponse>>();
        bool _bound;

        /// <summary>Raised for every parsed reply (including broadcast replies).</summary>
        public event Action<SnmpResponse> ResponseReceived;

        public SnmpSession()
        {
            _socket.MessageReceived += OnMessage;
        }

        async Task EnsureBoundAsync()
        {
            if (_bound) return;
            await _socket.BindServiceNameAsync(string.Empty);
            _bound = true;
        }

        volatile bool _disposed;

        void OnMessage(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            if (_disposed) return;
            try
            {
                var reader = args.GetDataReader();
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                var r = SnmpCodec.Parse(data);
                if (r == null) return;
                r.SourceAddress = args.RemoteAddress.CanonicalName;
                var h = ResponseReceived;
                if (h != null) h(r);
                TaskCompletionSource<SnmpResponse> tcs = null;
                lock (_lock)
                {
                    if (_pending.TryGetValue(r.RequestId, out tcs)) _pending.Remove(r.RequestId);
                }
                if (tcs != null) tcs.TrySetResult(r);
            }
            catch (Exception)
            {
                // ICMP port unreachable etc. surfaces here as an exception; ignore.
            }
        }

        // DatagramSocket hands out one output stream per remote endpoint. Disposing it and asking
        // again makes the next write fail with 0x8000000E, so writers are kept for the session.
        readonly Dictionary<string, DataWriter> _writers = new Dictionary<string, DataWriter>();
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public async Task SendAsync(string host, byte pduType, string oid, int requestId)
        {
            var packet = SnmpCodec.BuildRequest(pduType, requestId, oid);
            await _sendLock.WaitAsync();
            try
            {
                await EnsureBoundAsync();
                DataWriter writer;
                if (!_writers.TryGetValue(host, out writer))
                {
                    var stream = await _socket.GetOutputStreamAsync(new HostName(host), Port);
                    writer = new DataWriter(stream);
                    _writers[host] = writer;
                }
                writer.WriteBytes(packet);
                await writer.StoreAsync();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>Single request with retries. Returns null on timeout.</summary>
        public async Task<SnmpResponse> RequestAsync(string host, byte pduType, string oid,
            int timeoutMs = 1500, int retries = 2, CancellationToken ct = default(CancellationToken))
        {
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                int id = SnmpCodec.NextRequestId();
                var tcs = new TaskCompletionSource<SnmpResponse>();
                lock (_lock) _pending[id] = tcs;
                try
                {
                    await SendAsync(host, pduType, oid, id);
                }
                catch (Exception)
                {
                    lock (_lock) _pending.Remove(id);
                    if (attempt == retries) throw;
                    continue;
                }
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
                if (done == tcs.Task) return tcs.Task.Result;
                lock (_lock) _pending.Remove(id);
            }
            return null;
        }

        public Task<SnmpResponse> GetAsync(string host, string oid, int timeoutMs = 1500, int retries = 2,
            CancellationToken ct = default(CancellationToken))
        {
            return RequestAsync(host, SnmpCodec.PduGet, oid, timeoutMs, retries, ct);
        }

        public Task<SnmpResponse> GetNextAsync(string host, string oid, int timeoutMs = 1500, int retries = 2,
            CancellationToken ct = default(CancellationToken))
        {
            return RequestAsync(host, SnmpCodec.PduGetNext, oid, timeoutMs, retries, ct);
        }

        public void Dispose()
        {
            // Note: MessageReceived must NOT be unsubscribed after the socket is bound —
            // DatagramSocket throws E_ILLEGAL_METHOD_CALL (0x8000000E). _disposed silences it instead.
            _disposed = true;
            foreach (var w in _writers.Values)
            {
                try { w.DetachStream(); w.Dispose(); } catch (Exception) { }
            }
            _writers.Clear();
            try { _socket.Dispose(); } catch (Exception) { }
        }
    }
}
