// TCP 9100 (RAW / pdl-datastream) connection. Equivalent of epson-net-raw.c.

using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace iprint.Escpr
{
    public sealed class RawConnection : IJobOutput, IDisposable
    {
        public const string Port = "9100";

        readonly StreamSocket _socket = new StreamSocket();
        IOutputStream _stream;

        public long BytesSent { get; private set; }

        public async Task ConnectAsync(string host, CancellationToken ct)
        {
            _socket.Control.NoDelay = false;
            _socket.Control.KeepAlive = true;
            var connect = _socket.ConnectAsync(new HostName(host), Port).AsTask(ct);
            var done = await Task.WhenAny(connect, Task.Delay(10000, ct));
            if (done != connect) throw new TimeoutException("printer did not answer on port 9100");
            await connect;
            _stream = _socket.OutputStream;
        }

        public async Task WriteAsync(byte[] data, int offset, int count, CancellationToken ct)
        {
            if (count <= 0) return;
            IBuffer buf = data.AsBuffer(offset, count);
            uint written = 0;
            while (written < count)
            {
                ct.ThrowIfCancellationRequested();
                IBuffer part = written == 0 ? buf : data.AsBuffer(offset + (int)written, count - (int)written);
                written += await _stream.WriteAsync(part).AsTask(ct);
            }
            BytesSent += count;
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            await _stream.FlushAsync().AsTask(ct);
        }

        public void Dispose()
        {
            _socket.Dispose();
        }
    }
}
