using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace AppleMusicDiscordPresence
{
    /// <summary>
    /// Minimal implementation of the Discord IPC protocol (the discord-ipc-0..9 named pipes
    /// the desktop client listens on). Just enough to do a handshake and push
    /// SET_ACTIVITY (Rich Presence) updates. Not a full Game SDK client.
    ///
    /// All pipe I/O is serialized under <see cref="_ioLock"/> - the media session raises
    /// MediaPropertiesChanged and PlaybackInfoChanged on arbitrary threads, and two
    /// overlapping writes would interleave bytes on the wire.
    /// </summary>
    public class DiscordIpcClient : IDisposable
    {
        // Opcodes from the Discord IPC framing spec.
        private const int OpHandshake = 0;
        private const int OpFrame = 1;
        private const int OpClose = 2;
        private const int OpPing = 3;
        private const int OpPong = 4;

        private readonly string _clientId;
        private readonly object _ioLock = new();
        private NamedPipeClientStream? _pipe;
        private int _nonce;

        /// <summary>Human-readable reason the last <see cref="Connect"/> attempt failed.</summary>
        public string? LastConnectError { get; private set; }

        public DiscordIpcClient(string clientId) => _clientId = clientId;

        public bool IsConnected
        {
            get { lock (_ioLock) return _pipe is { IsConnected: true }; }
        }

        /// <summary>
        /// Tries pipe indices 0-9 (Discord opens the first free one; if you have
        /// multiple Discord-family clients running - stable/PTB/Canary - it may not be 0).
        /// Returns true only once the handshake has been acknowledged.
        /// </summary>
        public bool Connect()
        {
            lock (_ioLock)
            {
                Teardown();
                LastConnectError = null;

                int pipesFound = 0;

                for (int i = 0; i < 10; i++)
                {
                    NamedPipeClientStream? pipe = null;
                    try
                    {
                        pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                        pipe.Connect(500);
                    }
                    catch (TimeoutException)
                    {
                        pipe?.Dispose();
                        continue; // no pipe at this index
                    }
                    catch (Exception ex)
                    {
                        pipe?.Dispose();
                        LastConnectError = $"discord-ipc-{i}: {ex.GetType().Name}: {ex.Message}";
                        continue;
                    }

                    pipesFound++;
                    _pipe = pipe;

                    try
                    {
                        var handshake = JsonSerializer.Serialize(new { v = 1, client_id = _clientId });
                        WriteFrame(OpHandshake, handshake);

                        // On success Discord dispatches a READY frame (opcode 1). Failures
                        // come back either as a CLOSE (opcode 2) or a FRAME with evt=ERROR,
                        // both carrying a { code, message }.
                        var (opcode, payload) = ReadFrameRaw();
                        string frameErr = "";
                        if (opcode == OpFrame && !IsErrorPayload(payload, out frameErr))
                            return true;

                        LastConnectError = opcode switch
                        {
                            OpFrame => $"discord-ipc-{i}: {frameErr}",
                            OpClose => $"discord-ipc-{i}: closed by Discord - {DescribePayload(payload)}",
                            _ => $"discord-ipc-{i}: unexpected opcode {opcode} - {payload}"
                        };
                        Teardown();
                    }
                    catch (Exception ex)
                    {
                        LastConnectError = $"discord-ipc-{i}: handshake I/O failed - {ex.Message}";
                        _pipe = null;
                        try { pipe.Dispose(); } catch { /* ignore */ }
                    }
                }

                LastConnectError ??= pipesFound == 0
                    ? "no discord-ipc-* pipe found - is the Discord desktop client running (and not elevated differently to this process)?"
                    : "handshake was not acknowledged";
                return false;
            }
        }

        private static bool IsErrorPayload(string payload, out string message)
        {
            message = "";
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String
                    && evt.GetString() == "ERROR")
                {
                    message = DescribePayload(payload);
                    return true;
                }
            }
            catch { /* not JSON we recognise - treat as non-error */ }
            return false;
        }

        private static string DescribePayload(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var data = root.TryGetProperty("data", out var d) ? d : root;
                string? code = data.TryGetProperty("code", out var c) ? c.ToString() : null;
                string? msg = data.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (code, msg) switch
                {
                    (not null, not null) => $"code {code}: {msg}",
                    (null, not null) => msg!,
                    (not null, null) => $"code {code}",
                    _ => payload
                };
            }
            catch
            {
                return payload;
            }
        }

        /// <returns>true if the update was written and acknowledged; false if the pipe is down.</returns>
        public bool SetActivity(object activityPayload) => Send(new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity = activityPayload },
            nonce = NextNonce()
        });

        public bool ClearActivity() => Send(new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity = (object?)null },
            nonce = NextNonce()
        });

        private string NextNonce() => Interlocked.Increment(ref _nonce).ToString();

        private bool Send(object message)
        {
            lock (_ioLock)
            {
                if (_pipe is not { IsConnected: true })
                    return false;

                try
                {
                    WriteFrame(OpFrame, JsonSerializer.Serialize(message));

                    // Read until the command reply arrives. Answer PINGs along the way;
                    // a CLOSE (or EOF) means Discord went away.
                    for (int i = 0; i < 8; i++)
                    {
                        var (opcode, payload) = ReadFrameRaw();
                        switch (opcode)
                        {
                            case OpFrame:
                                return true;
                            case OpPing:
                                WriteFrame(OpPong, payload);
                                continue;
                            default: // OpClose, or something we don't speak
                                Teardown();
                                return false;
                        }
                    }

                    return true; // got only PINGs; treat the write as delivered
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    Teardown();
                    return false;
                }
            }
        }

        // caller holds _ioLock
        private void WriteFrame(int opcode, string json)
        {
            if (_pipe == null) return;

            var body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);

            _pipe.Write(frame, 0, frame.Length);
            _pipe.Flush();
        }

        // caller holds _ioLock. Throws IOException on a short/implausible read.
        private (int Opcode, string Payload) ReadFrameRaw()
        {
            if (_pipe == null) throw new IOException("pipe closed");

            var header = ReadExactly(8);
            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            if (length is < 0 or > 64 * 1024)
                throw new IOException($"implausible IPC frame length {length}");

            var body = ReadExactly(length);
            return (opcode, Encoding.UTF8.GetString(body));
        }

        // caller holds _ioLock
        private byte[] ReadExactly(int count)
        {
            var buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int r = _pipe!.Read(buf, read, count - read);
                if (r <= 0) throw new IOException("unexpected end of pipe");
                read += r;
            }
            return buf;
        }

        // caller holds _ioLock
        private void Teardown()
        {
            try { _pipe?.Dispose(); } catch { /* ignore */ }
            _pipe = null;
        }

        public void Dispose()
        {
            lock (_ioLock) Teardown();
        }
    }
}
