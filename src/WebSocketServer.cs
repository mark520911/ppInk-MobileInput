using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace gInk
{
    public enum MobileInputMode { Drawing = 0, Cursor = 1 }

    public class MobileSession
    {
        public WebSocket socket;
        public MobileInputMode Mode = MobileInputMode.Drawing;
        public DateTime LastActivity = DateTime.Now;
    }

    public class WebSocketServer
    {
        public static Root Root;
        private HttpListener listener;
        private readonly List<MobileSession> sessions = new List<MobileSession>();
        private bool running = false;
        private string password;

        public WebSocketServer(Root root) { Root = root; }

        public bool Start(string url, string pwd)
        {
            try
            {
                password = pwd;
                if (!string.IsNullOrEmpty(pwd))
                    Console.WriteLine("[WebSocketServer] WARNING: password is exposed in URL query string. Use a trusted network or change to first-frame auth.");
                listener = new HttpListener();
                listener.Prefixes.Clear();
                listener.Prefixes.Add(url);
                listener.Start();
                running = true;
                _ = Task.Run(ListenLoop);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("WebSocketServer Start error: " + ex);
                return false;
            }
        }

        public bool IsListening => listener?.IsListening ?? false;

        public void Stop()
        {
            running = false;
            lock (sessions)
            {
                foreach (var s in sessions)
                {
                    try { s.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
                }
                sessions.Clear();
            }
            try { listener?.Stop(); } catch { }
        }

        public void Close() => Stop();

        private async Task ListenLoop()
        {
            while (running)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    if (ctx.Request.IsWebSocketRequest)
                    {
                        _ = Task.Run(() => HandleClient(ctx));
                    }
                    else
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                    }
                }
                catch { if (!running) break; }
            }
        }

        private async Task HandleClient(HttpListenerContext ctx)
        {
            string pwd = ctx.Request.QueryString["pwd"];
            if (!string.IsNullOrEmpty(password) && pwd != password)
            {
                ctx.Response.StatusCode = 401;
                ctx.Response.Close();
                return;
            }

            WebSocketContext wsCtx;
            try { wsCtx = await ctx.AcceptWebSocketAsync(null); }
            catch
            {
                ctx.Response.StatusCode = 500;
                ctx.Response.Close();
                return;
            }

            var sess = new MobileSession { socket = wsCtx.WebSocket };
            lock (sessions) sessions.Add(sess);

            byte[] buf = new byte[2048];
            try
            {
                while (wsCtx.WebSocket.State == WebSocketState.Open && running)
                {
                    var result = await wsCtx.WebSocket.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.Count > 0)
                    {
                        sess.LastActivity = DateTime.Now;
                        byte[] frame = new byte[result.Count];
                        Array.Copy(buf, frame, result.Count);
                        MobileInputHandler.Instance?.ProcessFrame(sess, frame);
                    }
                }
            }
            catch { }
            finally
            {
                lock (sessions) sessions.Remove(sess);
                try { wsCtx.WebSocket.Dispose(); } catch { }
            }
        }
    }
}
