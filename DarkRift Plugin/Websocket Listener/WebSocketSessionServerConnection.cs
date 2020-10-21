using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DarkRift;
using DarkRift.Server;
using Fleck;

namespace WebSocketListener
{
    public class WebSocketSessionServerConnection : NetworkServerConnection
    {
        public override ConnectionState ConnectionState => _connectionState;
        public override IEnumerable<IPEndPoint> RemoteEndPoints { get; }

        public event Action<IWebSocketConnection> OnDisconnect;

        private bool _disposedValue;

        private ConnectionState _connectionState;
        private readonly IWebSocketConnection _webSocketSession;

        public WebSocketSessionServerConnection(IWebSocketConnection webSocketSession)
        {
            RemoteEndPoints = new List<IPEndPoint> { new IPEndPoint(IPAddress.Parse(webSocketSession.ConnectionInfo.ClientIpAddress), webSocketSession.ConnectionInfo.ClientPort) };

            _webSocketSession = webSocketSession;
            _connectionState = ConnectionState.Connected;
        }

        public override IPEndPoint GetRemoteEndPoint(string name)
        {
            return RemoteEndPoints.First();
        }

        public override void StartListening() { }

        public void MessageReceivedHandler(byte[] buffer)
        {
            using (var messageBuffer = MessageBuffer.Create(buffer.Length))
            {
                Buffer.BlockCopy(buffer, 0, messageBuffer.Buffer, 0, buffer.Length);
                messageBuffer.Count = buffer.Length;
                HandleMessageReceived(messageBuffer, SendMode.Reliable);
            }
        }

        public override bool SendMessageReliable(MessageBuffer message)
        {
            if (_connectionState == ConnectionState.Disconnected)
            {
                message.Dispose();
                return false;
            }

            var dataBuffer = new byte[message.Count];
            Buffer.BlockCopy(message.Buffer, 0, dataBuffer, 0, message.Count);

            _webSocketSession.Send(dataBuffer);
            message.Dispose();

            return true;
        }

        public override bool SendMessageUnreliable(MessageBuffer message)
        {
            if (_connectionState == ConnectionState.Disconnected)
            {
                message.Dispose();
                return false;
            }

            var dataBuffer = new byte[message.Count];
            Buffer.BlockCopy(message.Buffer, 0, dataBuffer, 0, message.Count);

            _webSocketSession.Send(dataBuffer);
            message.Dispose();

            return true;
        }

        public override bool Disconnect()
        {
            if (_connectionState == ConnectionState.Disconnected)
                return false;

            CloseConnection();
            return true;
        }

        private void CloseConnection()
        {
            _connectionState = ConnectionState.Disconnected;

            OnDisconnect?.Invoke(_webSocketSession);
            _webSocketSession.Close();
        }

        public void ClientDisconnected()
        {
            HandleDisconnection();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (_disposedValue)
                return;

            if (disposing)
            {
                Disconnect();
            }

            _disposedValue = true;
        }
    }
}