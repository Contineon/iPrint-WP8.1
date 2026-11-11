// High level printer operations: discovery, probe by IP, status, maintenance.
// Discovery follows snmpFindStart(): SNMP GetNext on the Epson device-id OID sent to broadcast.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace iprint.Escpr
{
    public sealed class PrinterInfo
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public string Model { get; set; }
        public string DeviceIdRaw { get; set; }
        public bool SupportsEscpr { get; set; }

        public static PrinterInfo From(string address, DeviceId id)
        {
            return new PrinterInfo
            {
                Address = address,
                Name = id.DisplayName,
                Model = id.Model,
                DeviceIdRaw = id.Raw,
                SupportsEscpr = id.SupportsEscpr
            };
        }
    }

    public static class EpsonPrinter
    {
        /// <summary>Broadcast discovery. onFound is called (on a worker thread) for each new Epson printer.</summary>
        public static async Task DiscoverAsync(Action<PrinterInfo> onFound, int durationMs, CancellationToken ct)
        {
            var seen = new HashSet<string>();
            using (var snmp = new SnmpSession())
            {
                snmp.ResponseReceived += r =>
                {
                    if (!r.IsOk || r.ValueType != SnmpValueType.OctetString) return;
                    if (!r.Oid.StartsWith(EpsonOids.DeviceIdPrefix)) return;
                    var id = DeviceId.Parse(r.Value);
                    if (id == null || !id.IsEpson) return;
                    lock (seen)
                    {
                        if (!seen.Add(r.SourceAddress)) return;
                    }
                    onFound(PrinterInfo.From(r.SourceAddress, id));
                };

                var targets = BroadcastAddresses();
                for (int round = 0; round < 3 && !ct.IsCancellationRequested; round++)
                {
                    foreach (var t in targets)
                    {
                        try
                        {
                            await snmp.SendAsync(t, SnmpCodec.PduGetNext, EpsonOids.DeviceIdPrefix, SnmpCodec.NextRequestId());
                        }
                        catch (Exception)
                        {
                            // a broadcast address may be rejected on some networks; keep trying others
                        }
                    }
                    try { await Task.Delay(durationMs / 3, ct); } catch (TaskCanceledException) { }
                }
            }
        }

        /// <summary>Directed broadcast of every IPv4 interface plus 255.255.255.255.</summary>
        public static List<string> BroadcastAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var h in NetworkInformation.GetHostNames())
                {
                    if (h.Type != HostNameType.Ipv4 || h.IPInformation == null || h.IPInformation.PrefixLength == null) continue;
                    var parts = h.CanonicalName.Split('.').Select(byte.Parse).ToArray();
                    if (parts.Length != 4) continue;
                    uint ip = ((uint)parts[0] << 24) | ((uint)parts[1] << 16) | ((uint)parts[2] << 8) | parts[3];
                    int prefix = h.IPInformation.PrefixLength.Value;
                    if (prefix <= 0 || prefix >= 32) continue;
                    uint mask = 0xFFFFFFFFu << (32 - prefix);
                    uint bc = (ip & mask) | ~mask;
                    list.Add(string.Format("{0}.{1}.{2}.{3}", bc >> 24, (bc >> 16) & 0xFF, (bc >> 8) & 0xFF, bc & 0xFF));
                }
            }
            catch (Exception) { }
            list.Add("255.255.255.255");
            return list.Distinct().ToList();
        }

        /// <summary>Asks one address for its device ID (manual IP). Returns null if no Epson printer answers.</summary>
        public static async Task<PrinterInfo> ProbeAsync(string address, CancellationToken ct)
        {
            using (var snmp = new SnmpSession())
            {
                var r = await snmp.GetAsync(address, EpsonOids.DeviceId, 1500, 2, ct);
                if (r == null || !r.IsOk || r.ValueType != SnmpValueType.OctetString)
                    r = await snmp.GetNextAsync(address, EpsonOids.DeviceIdPrefix, 1500, 1, ct);
                if (r == null || !r.IsOk || r.ValueType != SnmpValueType.OctetString) return null;
                var id = DeviceId.Parse(r.Value);
                if (id == null || !id.IsEpson) return null;
                return PrinterInfo.From(address, id);
            }
        }

        /// <summary>Why the last GetStatusAsync returned null (shown in the UI for diagnostics).</summary>
        public static string LastStatusError { get; private set; }

        /// <summary>
        /// Reads "@BDC ST2" status. Tries the SNMP control tunnel ("st" 01 00 01) first, then the
        /// direct status object that libescprlib also uses. Never throws except on cancellation.
        /// </summary>
        public static async Task<St2Status> GetStatusAsync(string address, CancellationToken ct)
        {
            var errors = new List<string>();
            try
            {
                using (var snmp = new SnmpSession())
                {
                    var attempts = new[]
                    {
                        Tuple.Create(SnmpCodec.PduGet, EpsonOids.Status, "st"),
                        Tuple.Create(SnmpCodec.PduGet, EpsonOids.StatusDirect, "status"),
                        Tuple.Create(SnmpCodec.PduGetNext, EpsonOids.StatusDirectPrefix, "status-next"),
                    };
                    foreach (var a in attempts)
                    {
                        ct.ThrowIfCancellationRequested();
                        SnmpResponse r;
                        try
                        {
                            r = await snmp.RequestAsync(address, a.Item1, a.Item2, 3000, 1, ct);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            errors.Add(a.Item3 + ": " + ex.Message);
                            continue;
                        }
                        if (r == null) { errors.Add(a.Item3 + ": timeout"); continue; }
                        if (!r.IsOk) { errors.Add(a.Item3 + ": snmp error " + r.ErrorStatus + "/" + r.ValueType); continue; }
                        if (r.ValueType != SnmpValueType.OctetString) { errors.Add(a.Item3 + ": type " + r.ValueType); continue; }
                        var st = St2Status.Parse(r.Value);
                        if (st == null) { errors.Add(a.Item3 + ": no ST2 in " + r.Value.Length + " bytes"); continue; }
                        LastStatusError = null;
                        return st;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
            LastStatusError = string.Join("; ", errors);
            return null;
        }

        /// <summary>Sends head cleaning or nozzle check (epsMakeMainteCmd) over TCP 9100.</summary>
        public static async Task RunMaintenanceAsync(string address, bool nozzleCheck, CancellationToken ct)
        {
            var data = EscprCommands.Maintenance(nozzleCheck, DateTime.Now);
            using (var conn = new RawConnection())
            {
                await conn.ConnectAsync(address, ct);
                await conn.WriteAsync(data, 0, data.Length, ct);
                await conn.FlushAsync(ct);
                // give the printer time to read everything before the socket closes
                await Task.Delay(1500, ct);
            }
        }

        // ---------------- waste ink counters (EEPROM, read only) ----------------
        // Values captured from PrintHelp talking to an L805 over SNMP:
        //   read key 0x43 0x01; main = 0x1C,0x1D,0x102 ; extra (borderless) = 0x1E,0x1F,0x103 ;
        //   raw 14756 shown as 83.97 %  ->  divider 175.73 (100 % ~= 17573).

        sealed class WasteModel
        {
            public string ModelTag;
            public byte Key1, Key2;
            public double Divider;
            public string[] Names;
            public int[][] Cells;   // little endian: low byte first
        }

        static readonly WasteModel[] WasteModels =
        {
            new WasteModel
            {
                ModelTag = "L805", Key1 = 0x43, Key2 = 0x01, Divider = 175.73,
                Names = new[] { "main counter", "extra counter" },
                Cells = new[] { new[] { 0x1C, 0x1D, 0x102 }, new[] { 0x1E, 0x1F, 0x103 } }
            },
        };

        public sealed class WasteCounter
        {
            public string Name { get; set; }
            public int Raw { get; set; }
            public double Percent { get; set; }
        }

        public static bool HasWasteCounters(string model)
        {
            return model != null && Array.Exists(WasteModels, m => model.ToUpperInvariant().Contains(m.ModelTag));
        }

        /// <summary>Reads one EEPROM byte: "||" 07 00 key1 key2 'A' ~'A' rot('A') lo hi -> "EE:AAAAVV;".</summary>
        static async Task<int> ReadEepromAsync(SnmpSession snmp, string address, byte k1, byte k2, int cell, CancellationToken ct)
        {
            var oid = EpsonOids.Ctrl("||", 0x07, 0x00, k1, k2, 65, 190, 160, (byte)(cell & 0xFF), (byte)(cell >> 8));
            var r = await snmp.GetAsync(address, oid, 2000, 2, ct);
            if (r == null || !r.IsOk || r.Value == null) throw new InvalidOperationException("no answer from EEPROM");
            var text = System.Text.Encoding.UTF8.GetString(r.Value, 0, r.Value.Length);
            int i = text.IndexOf("EE:", StringComparison.Ordinal);
            if (i < 0 || i + 9 > text.Length) throw new InvalidOperationException("bad EEPROM reply");
            return Convert.ToInt32(text.Substring(i + 7, 2), 16);
        }

        /// <summary>Returns null if the model is unknown or the printer did not answer.</summary>
        public static async Task<List<WasteCounter>> ReadWasteCountersAsync(string address, string model, CancellationToken ct)
        {
            if (model == null) return null;
            var cfg = Array.Find(WasteModels, m => model.ToUpperInvariant().Contains(m.ModelTag));
            if (cfg == null) return null;
            try
            {
                using (var snmp = new SnmpSession())
                {
                    var result = new List<WasteCounter>();
                    for (int c = 0; c < cfg.Cells.Length; c++)
                    {
                        int raw = 0;
                        for (int b = 0; b < cfg.Cells[c].Length; b++)
                            raw |= await ReadEepromAsync(snmp, address, cfg.Key1, cfg.Key2, cfg.Cells[c][b], ct) << (8 * b);
                        result.Add(new WasteCounter
                        {
                            Name = cfg.Names[c], Raw = raw, Percent = Math.Round(raw / cfg.Divider, 2)
                        });
                    }
                    return result;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }
    }
}
