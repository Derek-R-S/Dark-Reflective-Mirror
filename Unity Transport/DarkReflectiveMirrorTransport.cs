using DarkReflectiveMirror;
using DarkRift;
using DarkRift.Client.Unity;
using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(UnityClient))]
public class DarkReflectiveMirrorTransport : Transport
{
    #region Relay Server Variables
    public string relayIP = "34.72.21.213";
    public ushort relayPort = 4296;
    public bool forceRelayTraffic = false;
    [Tooltip("If your relay server has a password enter it here, or else leave it blank.")]
    public string relayPassword;
    #endregion
    #region Server List Data
    [Header("Server Data")]
    public int maxServerPlayers = 10;
    public string serverName = "My awesome server!";
    public string extraServerData = "Cool Map 1";
    [Tooltip("This allows you to make 'private' servers that do not show up on the built in server list.")]
    public bool showOnServerList = true;
    public bool showDebugLogs = false;
    public UnityEvent serverListUpdated;
    public List<RelayServerInfo> relayServerList = new List<RelayServerInfo>();
    [HideInInspector] public bool isAuthenticated = false;
    #endregion
    #region Script Variables
    public const string Scheme = "darkrelay";
    private BiDictionary<ushort, int> connectedRelayClients = new BiDictionary<ushort, int>();
    private BiDictionary<int, int> connectedDirectClients = new BiDictionary<int, int>();
    private UnityClient drClient;
    private bool isClient;
    private bool isConnected;
    private bool isServer;
    private bool directConnected = false;
    [Header("Current Server Info")]
    [Tooltip("This what what others use to connect, as soon as you start a server this will be valid. It can even be 0 if you are the first client on the relay!")]
    public ushort serverID;
    private bool shutdown = false;
    private int currentMemberID = 0;
    private string directConnectAddress = "";
    #endregion
    #region Standard Transport Variables
    [Header("Direct Connect Data")]
    private DarkMirrorDirectConnectModule directConnectModule;
    [Tooltip("The amount of time (in secs) it takes before we give up trying to direct connect.")]
    public float directConnectTimeout = 5;
    [Tooltip("If your scene does not need to connect on awake, set this to false, then use 'ConnectToRelay();' when needed.")]
    public bool connectToRelayOnAwake = true;
    #endregion

    void Awake()
    {
        if (connectToRelayOnAwake) { ConnectToRelay(); }
    }
    
    public void ConnectToRelay()
    {
        IPAddress ipAddress;
        if (!IPAddress.TryParse(relayIP, out ipAddress)) 
            ipAddress = Dns.GetHostEntry(relayIP).AddressList[0];

        drClient = GetComponent<UnityClient>();
        directConnectModule = GetComponent<DarkMirrorDirectConnectModule>();

        if (drClient.ConnectionState == ConnectionState.Disconnected)
            drClient.Connect(IPAddress.Parse(ipAddress.ToString()), relayPort, true);

        drClient.Disconnected += Client_Disconnected;
        drClient.MessageReceived += Client_MessageReceived;
    }

    #region Relay Functions

    private void Client_MessageReceived(object sender, DarkRift.Client.MessageReceivedEventArgs e)
    {
        try
        {
            using (Message message = e.GetMessage())
            using (DarkRiftReader reader = message.GetReader())
            {
                OpCodes opCode = (OpCodes)message.Tag;
                switch (opCode)
                {
                    case OpCodes.ServerConnectionData:
                        string serverIP = reader.ReadString();

                        if (directConnectModule == null)
                        {
                            directConnectAddress = "";
                            return;
                        }

                        directConnectAddress = serverIP;
                        if (showDebugLogs)
                            Debug.Log("Received direct connect info from server.");
                        break;
                    case OpCodes.Authenticated:
                        isAuthenticated = true;
                        if (showDebugLogs)
                            Debug.Log("Authenticated with server.");
                        break;
                    case OpCodes.AuthenticationRequest:
                        using (DarkRiftWriter writer = DarkRiftWriter.Create())
                        {
                            writer.Write(relayPassword);
                            using (Message sendAuthenticationResponse = Message.Create((ushort)OpCodes.AuthenticationResponse, writer))
                                drClient.Client.SendMessage(sendAuthenticationResponse, SendMode.Reliable);
                        }
                        if (showDebugLogs)
                            Debug.Log("Server requested authentication key.");
                        break;
                    case OpCodes.GetData:
                        int dataLength = reader.ReadInt32();
                        byte[] receivedData = new byte[dataLength];
                        System.Buffer.BlockCopy(reader.ReadBytes(), 0, receivedData, 0, dataLength);

                        if (isServer)
                            OnServerDataReceived?.Invoke(connectedRelayClients.GetByFirst(reader.ReadUInt16()), new ArraySegment<byte>(receivedData), e.SendMode == SendMode.Unreliable ? 1 : 0);

                        if (isClient)
                            OnClientDataReceived?.Invoke(new ArraySegment<byte>(receivedData), e.SendMode == SendMode.Unreliable ? 1 : 0);

                        break;
                    case OpCodes.ServerLeft:

                        if (isClient)
                        {
                            isClient = false;
                            OnClientDisconnected?.Invoke();
                        }

                        break;
                    case OpCodes.PlayerDisconnected:

                        if (isServer)
                        {
                            ushort user = reader.ReadUInt16();
                            OnServerDisconnected?.Invoke(connectedRelayClients.GetByFirst(user));
                            connectedRelayClients.Remove(user);
                            if (showDebugLogs)
                                Debug.Log($"Client {user} left room.");
                        }

                        break;
                    case OpCodes.RoomCreated:
                        serverID = reader.ReadUInt16();
                        isConnected = true;
                        if (showDebugLogs)
                            Debug.Log("Server Created on relay.");
                        break;
                    case OpCodes.ServerJoined:
                        ushort clientID = reader.ReadUInt16();

                        if (isClient)
                        {
                            isConnected = true;
                            OnClientConnected?.Invoke();
                            if (showDebugLogs)
                                Debug.Log("Successfully joined server.");
                        }

                        if (isServer)
                        {
                            connectedRelayClients.Add(clientID, currentMemberID);
                            OnServerConnected?.Invoke(currentMemberID);
                            currentMemberID++;
                            if (showDebugLogs)
                                Debug.Log($"Client {clientID} joined the server.");
                        }
                        break;
                    case OpCodes.ServerListResponse:
                        int serverListCount = reader.ReadInt32();
                        relayServerList.Clear();
                        for(int i = 0; i < serverListCount; i++)
                        {
                            relayServerList.Add(new RelayServerInfo()
                            {
                                serverName = reader.ReadString(),
                                currentPlayers = reader.ReadInt32(),
                                maxPlayers = reader.ReadInt32(),
                                serverID = reader.ReadUInt16(),
                                serverData = reader.ReadString()
                            });
                        }
                        serverListUpdated?.Invoke();
                        if (showDebugLogs)
                            Debug.Log("Received Server List.");
                        break;

                }
            }
        }
        catch {
            // Server shouldnt send messed up data but we do have an unreliable channel, so eh.
        }
    }

    public static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "0.0.0.0";
    }

    public void UpdateServerData(string serverData, int maxPlayers)
    {
        if (!isServer)
            return;

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(serverData);
            writer.Write(maxPlayers);
            using (Message sendUpdateRequest = Message.Create((ushort)OpCodes.UpdateRoomData, writer))
                drClient.SendMessage(sendUpdateRequest, SendMode.Reliable);
        }
    }

    public void RequestServerList()
    {
        // Start a coroutine just in case the client tries to request the server list too early.
        StopCoroutine(RequestServerListWait());
        StartCoroutine(RequestServerListWait());
    }

    IEnumerator RequestServerListWait()
    {
        int tries = 0;
        // Wait up to a maximum of 10 seconds before giving up.
        while(tries < 40)
        {
            if (isAuthenticated)
            {
                using (DarkRiftWriter writer = DarkRiftWriter.Create())
                {
                    using (Message sendServerListRequest = Message.Create((ushort)OpCodes.RequestServers, writer))
                        drClient.SendMessage(sendServerListRequest, SendMode.Reliable);
                }
                break;
            }
            yield return new WaitForSeconds(0.25f);
        }
    }

    private void Client_Disconnected(object sender, DarkRift.Client.DisconnectedEventArgs e)
    {
        isAuthenticated = false;
        if (isClient)
        {
            isClient = false;
            isConnected = false;
            OnClientDisconnected?.Invoke();
        }
    }

    #endregion
    #region Mirror Functions

    public override bool Available()
    {
        return drClient.ConnectionState == DarkRift.ConnectionState.Connected;
    }

    public override void ClientConnect(string address)
    {
        ushort hostID = 0;
        if (!Available() || !ushort.TryParse(address, out hostID))
        {
            Debug.Log("Not connected to relay or address is not a proper ID!");
            OnClientDisconnected?.Invoke();
            return;
        }

        if(isClient || isServer)
        {
            Debug.Log("Cannot connect while hosting/already connected.");
            return;
        }

        // Make sure the client is authenticated before trying to join a room.
        int timeOut = 0;
        while (true)
        {
            timeOut++;
            drClient.Dispatcher.ExecuteDispatcherTasks();
            if (isAuthenticated || timeOut >= 100)
                break;

            System.Threading.Thread.Sleep(100);
        }

        if (timeOut >= 100 && !isAuthenticated)
        {
            Debug.Log("Failed to authenticate in time with backend! Make sure your secret key and IP/port are correct.");
            OnClientDisconnected?.Invoke();
            return;
        }

        isClient = true;
        isConnected = false;
        directConnected = false;

        // Tell the server we want to join a room
        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(hostID);
            // If we dont support direct connections, tell it to force use the relay.
            writer.Write(directConnectModule == null);
            writer.Write(GetLocalIPAddress());
            using (Message sendJoinMessage = Message.Create((ushort)OpCodes.JoinServer, writer))
                drClient.Client.SendMessage(sendJoinMessage, SendMode.Reliable);
        }

        if (directConnectModule != null)
            StartCoroutine(WaitForConnecting(hostID));
    }

    IEnumerator WaitForConnecting(ushort host)
    {
        float currentTime = 0;

        while(currentTime < directConnectTimeout)
        {
            currentTime += Time.deltaTime;
            if (!string.IsNullOrEmpty(directConnectAddress))
            {
                directConnectModule.JoinServer(directConnectAddress);
                currentTime = 0;
                break;
            }

            if (isConnected)
            {
                if (showDebugLogs)
                    Debug.Log("Stopping direct connect attempt. Server doesnt support direct connect and used relay.");

                yield break;
            }

            yield return new WaitForEndOfFrame();
        }

        if(currentTime > 0)
        {
            // Waiting for info timed out, just use relay and connect.
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(host);
                writer.Write(true);
                writer.Write("0.0.0.0");
                using (Message sendJoinMessage = Message.Create((ushort)OpCodes.JoinServer, writer))
                    drClient.Client.SendMessage(sendJoinMessage, SendMode.Reliable);
            }
            if (showDebugLogs)
                Debug.Log("Failed to receive IP from server, falling back to relay.");
        }
        else
        {
            if (showDebugLogs)
                Debug.Log($"Received server connection info, attempting direct connection to {directConnectAddress}...");

            while (currentTime < directConnectTimeout)
            {
                currentTime += Time.deltaTime;

                if (isConnected)
                {
                    if (showDebugLogs)
                        Debug.Log("Direct connect successful!");

                    yield break;
                }

                yield return new WaitForEndOfFrame();
            }

            directConnectModule.ClientDisconnect();

            // Force join the server using relay since direct connect failed.
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(host);
                writer.Write(true);
                writer.Write("0.0.0.0");
                using (Message sendJoinMessage = Message.Create((ushort)OpCodes.JoinServer, writer))
                    drClient.Client.SendMessage(sendJoinMessage, SendMode.Reliable);
            }

            if (showDebugLogs)
                Debug.Log("Failed to direct connect, falling back to relay.");
        }
    }

    public override void ClientConnect(Uri uri)
    {
        ClientConnect(uri.Host);
    }

    public override bool ClientConnected()
    {
        return isConnected;
    }

    public override void ClientDisconnect()
    {
        isClient = false;

        StopAllCoroutines();

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            using (Message sendLeaveMessage = Message.Create((ushort)OpCodes.LeaveRoom, writer))
                drClient.Client.SendMessage(sendLeaveMessage, SendMode.Reliable);
        }

        if (directConnectModule != null)
            directConnectModule.ClientDisconnect();
    }

    public override bool ClientSend(int channelId, ArraySegment<byte> segment)
    {
        // Only channels are 0 (reliable), 1 (unreliable)

        if (directConnected)
        {
            return directConnectModule.ClientSend(segment, channelId);
        }
        else
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(segment.Count);
                writer.Write(segment.Array.Take(segment.Count).ToArray());
                using (Message sendDataMessage = Message.Create((ushort)OpCodes.SendData, writer))
                    drClient.Client.SendMessage(sendDataMessage, channelId == 0 ? SendMode.Reliable : SendMode.Unreliable);
            }
        }

        return true;
    }

    public override int GetMaxPacketSize(int channelId = 0)
    {
        return 1000;
    }

    public override bool ServerActive()
    {
        return isServer;
    }

    public override bool ServerDisconnect(int connectionId)
    {
        ushort relayID;
        int directID;
        if (connectedRelayClients.TryGetBySecond(connectionId, out relayID))
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(relayID);
                using (Message sendKickMessage = Message.Create((ushort)OpCodes.KickPlayer, writer))
                    drClient.Client.SendMessage(sendKickMessage, SendMode.Reliable);
            }

            return true;
        }

        if (connectedDirectClients.TryGetBySecond(connectionId, out directID))
        {
            directConnectModule.KickClient(directID);

            return true;
        }
        return false;
    }

    public override string ServerGetClientAddress(int connectionId)
    {
        int directID = 0;

        if(connectedDirectClients.TryGetBySecond(connectionId, out directID))
        {
            return "DIRECT-" + directID;
        }

        return connectedRelayClients.GetBySecond(connectionId).ToString();
    }

    public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment)
    {
        // TODO: Optimize
        List<ushort> clients = new List<ushort>();
        List<int> directClients = new List<int>();
        bool tryDirectConnect = directConnectModule != null;

        for (int i = 0; i < connectionIds.Count; i++)
        {
            if (tryDirectConnect)
            {
                int clientID = 0;
                if (connectedDirectClients.TryGetBySecond(connectionIds[i], out clientID))
                {
                    directClients.Add(clientID);
                    continue;
                }
            }
            clients.Add(connectedRelayClients.GetBySecond(connectionIds[i]));
            // Including more than 10 client ids per single packet to the relay server could get risky with MTU so less risks if we split it into chunks of 1 packet per 10 players its sending to the server
            if (clients.Count >= 10)
            {
                ServerSendData(clients, segment, channelId);
                clients.Clear();
            }
        }

        if (clients.Count > 0)
        {
            ServerSendData(clients, segment, channelId);
        }

        if(directClients.Count > 0)
        {
            directConnectModule.ServerSend(directClients, segment, channelId);
        }

        return true;
    }

    void ServerSendData(List<ushort> clients, ArraySegment<byte> data, int channelId)
    {
        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(data.Count);
            writer.Write(data.Array.Take(data.Count).ToArray());
            writer.Write(clients.ToArray());
            using (Message sendDataMessage = Message.Create((ushort)OpCodes.SendData, writer))
                drClient.Client.SendMessage(sendDataMessage, channelId == 0 ? SendMode.Reliable : SendMode.Unreliable);
        }
    }

    public override void ServerStart()
    {
        if (!Available())
        {
            Debug.Log("Not connected to relay, server failed to start!");
            return;
        }

        if (isClient || isServer)
        {
            Debug.Log("Cannot connect while hosting/already connected.");
            return;
        }

        isServer = true;
        isConnected = false;
        currentMemberID = 1;
        connectedRelayClients = new BiDictionary<ushort, int>();
        connectedDirectClients = new BiDictionary<int, int>();

        // Wait to make sure we are authenticated with the server before actually trying to request creating a room.
        int timeOut = 0;
        while (true)
        {
            timeOut++;
            drClient.Dispatcher.ExecuteDispatcherTasks();
            if (isAuthenticated || timeOut >= 100)
                break;

            System.Threading.Thread.Sleep(100);
        }

        if(timeOut >= 100)
        {
            Debug.Log("Failed to authenticate in time with backend! Make sure your secret key and IP/port are correct.");
            return;
        }

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(maxServerPlayers);
            writer.Write(serverName);
            writer.Write(showOnServerList);
            writer.Write(extraServerData);
            // If we are forcing relay traffic, or we dont have the direct connect module, tell it to only use relay. If not, tell it we can try direct connections.
            writer.Write(forceRelayTraffic ? true : directConnectModule == null ? true : false);
            writer.Write(GetLocalIPAddress());
            using (Message sendStartMessage = Message.Create((ushort)OpCodes.CreateRoom, writer))
                drClient.Client.SendMessage(sendStartMessage, SendMode.Reliable);
        }

        if (!forceRelayTraffic && directConnectModule != null)
            directConnectModule.StartServer();

        // Wait until server is actually ready or 10 seconds have passed and server failed
        timeOut = 0;
        while (true)
        {
            timeOut++;
            drClient.Dispatcher.ExecuteDispatcherTasks();
            if (isConnected || timeOut >= 100)
                break;
            System.Threading.Thread.Sleep(100);
        }

        if(timeOut >= 100)
        {
            Debug.LogError("Failed to create the server on the relay. Are you connected? Double check the secret key and IP/port.");
        }
    }

    public override void ServerStop()
    {
        if (isServer)
        {
            if (directConnectModule != null)
                directConnectModule.StopServer();

            isServer = false;
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                using (Message sendStopMessage = Message.Create((ushort)OpCodes.LeaveRoom, writer))
                    drClient.Client.SendMessage(sendStopMessage, SendMode.Reliable);
            }
        }
    }

    public override Uri ServerUri()
    {
        UriBuilder builder = new UriBuilder();
        builder.Scheme = Scheme;
        builder.Host = serverID.ToString();
        return builder.Uri;
    }

    public override void Shutdown()
    {
        shutdown = true;
        if (drClient) { drClient.Disconnect(); }
    }
    #endregion

    #region Direct Connect Module
    public void DirectAddClient(int clientID)
    {
        if (!isServer)
            return;

        connectedDirectClients.Add(clientID, currentMemberID);
        OnServerConnected?.Invoke(currentMemberID);
        currentMemberID++;
    }

    public void DirectRemoveClient(int clientID)
    {
        if (!isServer)
            return;

        OnServerDisconnected?.Invoke(connectedDirectClients.GetByFirst(clientID));
        connectedDirectClients.Remove(clientID);
    }

    public void DirectReceiveData(ArraySegment<byte> data, int channel, int clientID = -1)
    {
        if (isServer)
            OnServerDataReceived?.Invoke(connectedDirectClients.GetByFirst(clientID), new ArraySegment<byte>(data.Array, 0, data.Count), channel);

        if (isClient)
            OnClientDataReceived?.Invoke(data, channel);
    }

    public void DirectClientConnected()
    {
        directConnected = true;
        isConnected = true;
        OnClientConnected?.Invoke();
    }

    public void DirectDisconnected()
    {
        if (directConnected)
        {
            isConnected = false;
            isClient = false;
            OnClientDisconnected?.Invoke();
        }
    }
    #endregion
}

public struct RelayServerInfo
{
    public string serverName;
    public int currentPlayers;
    public int maxPlayers;
    public ushort serverID;
    public string serverData;
}

enum OpCodes { Default = 0, RequestID = 1, JoinServer = 2, SendData = 3, GetID = 4, ServerJoined = 5, GetData = 6, CreateRoom = 7, ServerLeft = 8, PlayerDisconnected = 9, RoomCreated = 10, LeaveRoom = 11, KickPlayer = 12, AuthenticationRequest = 13, AuthenticationResponse = 14, RequestServers = 15, ServerListResponse = 16, Authenticated = 17, UpdateRoomData = 18, ServerConnectionData = 19 }
