using System;
using System.Collections.Generic;
using System.Net;
using DarkRift.Server;
using Fleck;

namespace WebSocketListener
{
    public class WebSocketNetworkListener : NetworkListener
    {
        public override Version Version => new Version(1, 0, 2);

        private WebSocketServer _serverSocket;

        private readonly IPEndPoint _ipEndPoint;
        public string port;
        public string address;

        private readonly Dictionary<IWebSocketConnection, WebSocketSessionServerConnection> _connections =
            new Dictionary<IWebSocketConnection, WebSocketSessionServerConnection>();

        public WebSocketNetworkListener(NetworkListenerLoadData pluginLoadData) : base(pluginLoadData)
        {
            address = pluginLoadData.Settings["address"];
            port = pluginLoadData.Settings["port"];
        }

        public override void StartListening()
        {

            _serverSocket = new WebSocketServer("ws://" + address + ":" + port);

            _serverSocket.Start(socket =>
            {
                socket.OnOpen = () => { NewSessionConnectedHandler(socket); };
                socket.OnClose = () => { SessionClosedHandler(socket); };
                socket.OnBinary = (data) => { NewDataReceivedHandler(socket, data); };
            });
        }

        private void NewSessionConnectedHandler(IWebSocketConnection session)
        {

            WebSocketSessionServerConnection serverConnection = new WebSocketSessionServerConnection(session);
            serverConnection.OnDisconnect += ServerSessionEndHandler;
            RegisterClientSession(session, serverConnection);
            RegisterConnection(serverConnection);
        }

        private void RegisterClientSession(IWebSocketConnection session, WebSocketSessionServerConnection connection)
        {
            lock (_connections)
            {
                if (_connections.ContainsKey(session))
                    return;

                _connections[session] = connection;
            }
        }

        private void UnregisterClientSession(IWebSocketConnection session)
        {
            lock (_connections)
            {
                if (!_connections.ContainsKey(session))
                    return;

                _connections.Remove(session);
            }
        }

        private void ServerSessionEndHandler(IWebSocketConnection session)
        {
            SessionClosedHandler(session);
        }

        private void SessionClosedHandler(IWebSocketConnection session)
        {
            lock (_connections)
            {
                if (!_connections.ContainsKey(session))
                    return;

                _connections[session].ClientDisconnected();
                UnregisterClientSession(session);
            }
        }

        private void NewDataReceivedHandler(IWebSocketConnection session, byte[] dataBuffer)
        {
            lock (_connections)
            {
                if (!_connections.ContainsKey(session)) return;
                _connections[session].MessageReceivedHandler(dataBuffer);
            }
        }
    }
}