using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HB.PerfProbe
{
    /// <summary>
    /// 127.0.0.1에만 바인딩하는 한 줄 명령 서버. PC는 <c>adb forward tcp:P tcp:P</c>로 붙는다.
    /// 연결 하나에 명령 한 줄 · 답 한 줄이다. 연결마다 스레드를 따로 쓰고 동시 연결은 <see cref="MaxClients"/>개까지라,
    /// 한 연결이 붙잡고 있어도 다른 연결은 막히지 않는다.
    /// 받은 줄은 큐에 넣고, 메인 스레드가 <see cref="Pump"/>에서 처리해 답을 채운다. 인증(토큰 검사)은 handler가 한다.
    /// </summary>
    internal sealed class CommandServer : IDisposable
    {
        internal sealed class Pending
        {
            public string Line;
            public string Reply;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            int _completed;

            public void Complete(string reply)
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                Reply = reply;
                Done.Set();
            }
        }

        public const int MaxClients = 4;
        /// <summary>snapshot 대기 상한(PerfProbeRunner)보다 길게.</summary>
        static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(300);
        const int ClientReadTimeoutMs = 5000;

        readonly TcpListener _listener;
        readonly ConcurrentQueue<Pending> _queue = new ConcurrentQueue<Pending>();
        readonly HashSet<Pending> _outstanding = new HashSet<Pending>();
        readonly HashSet<TcpClient> _clients = new HashSet<TcpClient>();
        readonly object _lock = new object();
        readonly Thread _thread;
        volatile bool _stopping;

        public int Port { get; }

        /// <param name="port">0이면 빈 포트를 고른다 (시험용).</param>
        public CommandServer(int port)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "HBPerfProbe.Accept" };
            _thread.Start();
        }

        /// <summary>메인 스레드에서 매 프레임 부른다. handler는 명령 줄과 답을 채울 콜백을 받는다 (나중에 불러도 된다).</summary>
        public void Pump(Action<string, Action<string>> handler)
        {
            while (_queue.TryDequeue(out Pending p))
            {
                try
                {
                    handler(p.Line, p.Complete);
                }
                catch (Exception e)
                {
                    p.Complete(ProbeJson.Error(null, e.GetType().Name + ": " + e.Message));
                }
            }
        }

        void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                lock (_lock)
                {
                    if (_stopping || _clients.Count >= MaxClients)
                    {
                        client.Close();
                        continue;
                    }
                    _clients.Add(client);
                }
                new Thread(() => Serve(client)) { IsBackground = true, Name = "HBPerfProbe.Client" }.Start();
            }
        }

        void Serve(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = ClientReadTimeoutMs;
                NetworkStream stream = client.GetStream();
                string line = ReadLimitedLine(stream);
                if (line == null) return;

                string reply;
                if (line.Length > ProbeCommand.MaxLineLength)
                {
                    reply = ProbeJson.Error(null, $"line longer than {ProbeCommand.MaxLineLength}");
                }
                else
                {
                    var pending = new Pending { Line = line };
                    lock (_lock)
                    {
                        if (_stopping) pending.Complete(ProbeJson.Error(null, "probe stopped"));
                        else _outstanding.Add(pending);
                    }
                    if (!pending.Done.IsSet) _queue.Enqueue(pending);
                    reply = pending.Done.Wait(ReplyTimeout) ? pending.Reply : ProbeJson.Error(null, "timeout waiting for main thread");
                    lock (_lock) _outstanding.Remove(pending);
                }
                byte[] bytes = new UTF8Encoding(false).GetBytes(reply + "\n");
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                lock (_lock) _clients.Remove(client);
                client.Close();
            }
        }

        /// <summary>
        /// 한 줄(UTF-8)을 읽는다. 상한을 넘는 줄은 나머지를 버리고 상한+1 길이로 돌려줘 호출부가 거절하게 한다.
        /// 아무것도 못 읽고 연결이 끝나면 null.
        /// </summary>
        static string ReadLimitedLine(Stream stream)
        {
            var bytes = new List<byte>(128);
            bool any = false;
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    if (!any) return null;
                    break;
                }
                any = true;
                if (b == '\n') break;
                if (b == '\r') continue;
                if (bytes.Count <= ProbeCommand.MaxLineLength) bytes.Add((byte)b);
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public void Dispose()
        {
            List<Pending> pending;
            List<TcpClient> clients;
            lock (_lock)
            {
                if (_stopping) return;
                _stopping = true;
                pending = new List<Pending>(_outstanding);
                clients = new List<TcpClient>(_clients);
            }
            _listener.Stop();
            // 큐에 있든 메인 스레드가 이미 꺼냈든(캡처 코루틴 진행 중) 모두 채워 소켓 스레드를 풀어 준다.
            foreach (Pending p in pending) p.Complete(ProbeJson.Error(null, "probe stopped"));
            foreach (TcpClient c in clients) c.Close();
            _thread.Join(1000);
        }
    }
}
