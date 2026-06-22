using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace PartyGame
{
    /// <summary>
    /// Self-hosted HTTP + WebSocket server.
    ///
    /// - Serves the mobile controller page (controller.html from StreamingAssets) over plain HTTP.
    /// - Upgrades WebSocket requests and exchanges <see cref="NetMessage"/> JSON in real time.
    /// - Runs all socket I/O on background threads, but raises its public events
    ///   (<see cref="ClientConnected"/>, <see cref="ClientDisconnected"/>, <see cref="MessageReceived"/>)
    ///   on the Unity main thread so listeners can safely touch the scene.
    ///
    /// Binds to <c>IPAddress.Any</c> which does NOT require admin rights (unlike HttpListener URL ACLs),
    /// so phones on the same LAN can reach it directly.
    /// </summary>
    public class MobileServer : MonoBehaviour
    {
        [Tooltip("TCP port the server listens on.")]
        public int port = 8080;

        [Tooltip("Start the server automatically in Start().")]
        public bool autoStart = true;

        /// <summary>Raised on the main thread when a WebSocket client finishes connecting.</summary>
        public event Action<int> ClientConnected;

        /// <summary>Raised on the main thread when a WebSocket client disconnects.</summary>
        public event Action<int> ClientDisconnected;

        /// <summary>Raised on the main thread for every text message received from a client.</summary>
        public event Action<int, string> MessageReceived;

        public static MobileServer Instance { get; private set; }

        public bool IsRunning { get; private set; }
        public string LocalIp { get; private set; } = "127.0.0.1";

        public string GetConnectUrl() => $"http://{LocalIp}:{port}/";

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        private readonly ConcurrentDictionary<int, ClientConn> _clients = new ConcurrentDictionary<int, ClientConn>();
        private int _nextClientId;

        // Actions queued from worker threads, drained on the main thread in Update().
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        private const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private string _cachedHtml;

        private class ClientConn
        {
            public int Id;
            public TcpClient Tcp;
            public NetworkStream Stream;
            public Thread Thread;
            public readonly object SendLock = new object();
            public volatile bool Alive = true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // A persistent server already exists (e.g. carried over from a previous scene).
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (autoStart)
                StartServer();
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void OnDestroy() => StopServer();
        private void OnApplicationQuit() => StopServer();

        public void StartServer()
        {
            if (IsRunning) return;

            LocalIp = ResolveLocalIp();

            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MobileServer] Failed to start on port {port}: {e.Message}");
                return;
            }

            _running = true;
            IsRunning = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MobileServer-Accept" };
            _acceptThread.Start();

            Debug.Log($"[MobileServer] Listening at {GetConnectUrl()}");
        }

        public void StopServer()
        {
            if (!_running && !IsRunning) return;
            _running = false;
            IsRunning = false;

            try { _listener?.Stop(); } catch { }
            _listener = null;

            foreach (var kv in _clients)
            {
                var c = kv.Value;
                c.Alive = false;
                try { c.Stream?.Close(); } catch { }
                try { c.Tcp?.Close(); } catch { }
            }
            _clients.Clear();
        }

        // ---------------------------------------------------------------- sending

        /// <summary>Send a message to a single client (safe to call from the main thread).</summary>
        public void Send(int clientId, NetMessage msg)
        {
            if (_clients.TryGetValue(clientId, out var c))
                SendText(c, msg.ToJson());
        }

        /// <summary>Send a message to every connected client.</summary>
        public void Broadcast(NetMessage msg)
        {
            string json = msg.ToJson();
            foreach (var kv in _clients)
                SendText(kv.Value, json);
        }

        // ---------------------------------------------------------------- accept / serve

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient tcp;
                try { tcp = _listener.AcceptTcpClient(); }
                catch { break; } // listener stopped

                var t = new Thread(() => HandleConnection(tcp)) { IsBackground = true, Name = "MobileServer-Conn" };
                t.Start();
            }
        }

        private void HandleConnection(TcpClient tcp)
        {
            try
            {
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                string header = ReadHttpHeader(stream);
                if (string.IsNullOrEmpty(header))
                {
                    tcp.Close();
                    return;
                }

                var headers = ParseHeaders(header, out string method, out string path);

                bool isWebSocket = headers.TryGetValue("upgrade", out var up) &&
                                   up.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isWebSocket)
                {
                    if (!headers.TryGetValue("sec-websocket-key", out var key))
                    {
                        tcp.Close();
                        return;
                    }
                    CompleteHandshake(stream, key);
                    RunWebSocketClient(tcp, stream);
                }
                else
                {
                    ServeHttp(stream, method, path);
                    tcp.Close();
                }
            }
            catch (Exception)
            {
                try { tcp.Close(); } catch { }
            }
        }

        private void ServeHttp(NetworkStream stream, string method, string path)
        {
            if (method != "GET")
            {
                WriteHttpResponse(stream, "405 Method Not Allowed", "text/plain", Encoding.UTF8.GetBytes("405"));
                return;
            }

            // Ignore favicon noise quietly.
            if (path != null && path.StartsWith("/favicon"))
            {
                WriteHttpResponse(stream, "204 No Content", "text/plain", Array.Empty<byte>());
                return;
            }

            string html = LoadControllerHtml();
            WriteHttpResponse(stream, "200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
        }

        private string LoadControllerHtml()
        {
            if (!string.IsNullOrEmpty(_cachedHtml))
                return _cachedHtml;

            string filePath = Path.Combine(Application.streamingAssetsPath, "controller.html");
            try
            {
                if (File.Exists(filePath))
                {
                    _cachedHtml = File.ReadAllText(filePath);
                    return _cachedHtml;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MobileServer] Could not read controller.html: {e.Message}");
            }

            return "<!doctype html><html><body><h1>controller.html missing</h1>" +
                   "<p>Place it in Assets/StreamingAssets/controller.html</p></body></html>";
        }

        private static void WriteHttpResponse(NetworkStream stream, string status, string contentType, byte[] body)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            if (body.Length > 0)
                stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        // ---------------------------------------------------------------- websocket

        private void CompleteHandshake(NetworkStream stream, string key)
        {
            string accept;
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.ASCII.GetBytes(key.Trim() + WsGuid));
                accept = Convert.ToBase64String(hash);
            }

            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 101 Switching Protocols\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Accept: ").Append(accept).Append("\r\n");
            sb.Append("\r\n");
            byte[] resp = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(resp, 0, resp.Length);
            stream.Flush();
        }

        private void RunWebSocketClient(TcpClient tcp, NetworkStream stream)
        {
            int id = Interlocked.Increment(ref _nextClientId);
            var conn = new ClientConn { Id = id, Tcp = tcp, Stream = stream, Thread = Thread.CurrentThread };
            _clients[id] = conn;

            _mainThreadQueue.Enqueue(() => ClientConnected?.Invoke(id));

            var assembled = new List<byte>();
            int messageOpcode = 0;

            try
            {
                while (_running && conn.Alive)
                {
                    byte[] h = ReadExact(stream, 2);
                    if (h == null) break;

                    bool fin = (h[0] & 0x80) != 0;
                    int opcode = h[0] & 0x0F;
                    bool masked = (h[1] & 0x80) != 0;
                    long len = h[1] & 0x7F;

                    if (len == 126)
                    {
                        byte[] e = ReadExact(stream, 2);
                        if (e == null) break;
                        len = (e[0] << 8) | e[1];
                    }
                    else if (len == 127)
                    {
                        byte[] e = ReadExact(stream, 8);
                        if (e == null) break;
                        len = 0;
                        for (int i = 0; i < 8; i++) len = (len << 8) | e[i];
                    }

                    byte[] mask = null;
                    if (masked)
                    {
                        mask = ReadExact(stream, 4);
                        if (mask == null) break;
                    }

                    byte[] payload = len == 0 ? Array.Empty<byte>() : ReadExact(stream, (int)len);
                    if (payload == null) break;

                    if (masked)
                        for (int i = 0; i < payload.Length; i++)
                            payload[i] ^= mask[i % 4];

                    switch (opcode)
                    {
                        case 0x8: // close
                            SendClose(conn);
                            conn.Alive = false;
                            break;

                        case 0x9: // ping -> pong
                            SendFrame(conn, 0xA, payload);
                            break;

                        case 0xA: // pong -> ignore
                            break;

                        case 0x0: // continuation
                        case 0x1: // text
                        case 0x2: // binary
                            if (opcode != 0x0)
                                messageOpcode = opcode;
                            assembled.AddRange(payload);
                            if (fin)
                            {
                                if (messageOpcode == 0x1)
                                {
                                    string text = Encoding.UTF8.GetString(assembled.ToArray());
                                    _mainThreadQueue.Enqueue(() => MessageReceived?.Invoke(id, text));
                                }
                                assembled.Clear();
                            }
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // socket error -> treat as disconnect
            }
            finally
            {
                conn.Alive = false;
                _clients.TryRemove(id, out _);
                try { stream.Close(); } catch { }
                try { tcp.Close(); } catch { }
                _mainThreadQueue.Enqueue(() => ClientDisconnected?.Invoke(id));
            }
        }

        private void SendText(ClientConn c, string text)
        {
            SendFrame(c, 0x1, Encoding.UTF8.GetBytes(text));
        }

        private void SendClose(ClientConn c)
        {
            try { SendFrame(c, 0x8, Array.Empty<byte>()); } catch { }
        }

        private void SendFrame(ClientConn c, int opcode, byte[] payload)
        {
            if (!c.Alive) return;

            byte[] frame;
            int len = payload.Length;

            if (len < 126)
            {
                frame = new byte[2 + len];
                frame[1] = (byte)len;
                Buffer.BlockCopy(payload, 0, frame, 2, len);
            }
            else if (len <= 0xFFFF)
            {
                frame = new byte[4 + len];
                frame[1] = 126;
                frame[2] = (byte)((len >> 8) & 0xFF);
                frame[3] = (byte)(len & 0xFF);
                Buffer.BlockCopy(payload, 0, frame, 4, len);
            }
            else
            {
                frame = new byte[10 + len];
                frame[1] = 127;
                for (int i = 0; i < 8; i++)
                    frame[2 + i] = (byte)((len >> ((7 - i) * 8)) & 0xFF);
                Buffer.BlockCopy(payload, 0, frame, 10, len);
            }

            frame[0] = (byte)(0x80 | (opcode & 0x0F)); // FIN + opcode, server frames are not masked

            try
            {
                lock (c.SendLock)
                {
                    c.Stream.Write(frame, 0, frame.Length);
                    c.Stream.Flush();
                }
            }
            catch (Exception)
            {
                c.Alive = false;
            }
        }

        // ---------------------------------------------------------------- low level helpers

        private byte[] ReadExact(NetworkStream stream, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read;
                try { read = stream.Read(buffer, offset, count - offset); }
                catch { return null; }
                if (read <= 0) return null;
                offset += read;
            }
            return buffer;
        }

        private static string ReadHttpHeader(NetworkStream stream)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b;
                try { b = stream.ReadByte(); }
                catch { break; }
                if (b == -1) break;
                sb.Append((char)b);
                int n = sb.Length;
                if (n >= 4 && sb[n - 1] == '\n' && sb[n - 2] == '\r' && sb[n - 3] == '\n' && sb[n - 4] == '\r')
                    break;
                if (n > 16384) break; // header too large, abort
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> ParseHeaders(string raw, out string method, out string path)
        {
            method = "GET";
            path = "/";
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] lines = raw.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length > 0)
            {
                string[] parts = lines[0].Split(' ');
                if (parts.Length >= 2)
                {
                    method = parts[0];
                    path = parts[1];
                }
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line)) continue;
                int idx = line.IndexOf(':');
                if (idx <= 0) continue;
                string key = line.Substring(0, idx).Trim();
                string value = line.Substring(idx + 1).Trim();
                dict[key] = value;
            }
            return dict;
        }

        private static string ResolveLocalIp()
        {
            // Primary outbound interface trick (no packets actually sent for UDP).
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint ep && !IPAddress.IsLoopback(ep.Address))
                        return ep.Address.ToString();
                }
            }
            catch { }

            // Fallback: first private IPv4 from host entry.
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch { }

            return "127.0.0.1";
        }
    }
}
