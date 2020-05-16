using DarkReflectiveMirror;
using DarkRift;
using DarkRift.Client.Unity;
using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEngine;

[RequireComponent(typeof(UnityClient))]
public class DarkReflectiveMirrorTransport : Transport
{
    public string relayIP = "127.0.0.1";
    public ushort relayPort = 4296;
    public int maxServerPlayers = 10;
    public const string Scheme = "darkrelay";
    private BiDictionary<ushort, int> connectedClients = new BiDictionary<ushort, int>();
    private UnityClient drClient;
    private bool isClient;
    private bool isConnected;
    private bool isServer;
    public ushort serverID;
    private bool shutdown = false;
    private int currentMemberID = 0;

    void Awake()
    {
        IPAddress ipAddress;
        if (!IPAddress.TryParse(relayIP, out ipAddress)) { ipAddress = Dns.GetHostEntry(relayIP).AddressList[0]; }

        drClient = GetComponent<UnityClient>();
        if(drClient.ConnectionState == ConnectionState.Disconnected)
        
        drClient.Connect(IPAddress.Parse(ipAddress.ToString()), relayPort, true);
        drClient.Disconnected += Client_Disconnected;
        drClient.MessageReceived += Client_MessageReceived;
    }

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
                    case OpCodes.GetData:
                        int dataLength = reader.ReadInt32();
                        byte[] receivedData = new byte[dataLength];
                        System.Buffer.BlockCopy(reader.ReadBytes(), 0, receivedData, 0, dataLength);

                        if (isServer)
                            OnServerDataReceived?.Invoke(connectedClients.GetByFirst(reader.ReadUInt16()), new ArraySegment<byte>(receivedData), e.SendMode == SendMode.Unreliable ? 1 : 0);

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
                            OnServerDisconnected?.Invoke(connectedClients.GetByFirst(user));
                        }

                        break;
                    case OpCodes.RoomCreated:
                        serverID = reader.ReadUInt16();
                        isConnected = true;
                        break;
                    case OpCodes.ServerJoined:
                        ushort clientID = reader.ReadUInt16();

                        if (isClient)
                        {
                            isConnected = true;
                            OnClientConnected?.Invoke();
                        }

                        if (isServer)
                        {
                            connectedClients.Add(clientID, currentMemberID);
                            OnServerConnected?.Invoke(currentMemberID);
                            currentMemberID++;
                        }
                        break;

                }
            }
        }
        catch {
            // Server shouldnt send messed up data but we do have an unreliable channel, so eh.
        }
    }

    private void Client_Disconnected(object sender, DarkRift.Client.DisconnectedEventArgs e)
    {
        if (isClient)
        {
            isClient = false;
            OnClientDisconnected?.Invoke();
        }
    }

    public override bool Available()
    {
        return drClient.ConnectionState == DarkRift.ConnectionState.Connected;
    }

    public override void ClientConnect(string address)
    {
        ushort hostID = 0;
        if (!Available() ) //|| !ushort.TryParse(address, out hostID))
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

        isClient = true;
        isConnected = false;

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(hostID);
            using (Message sendJoinMessage = Message.Create((ushort)OpCodes.JoinServer, writer))
                drClient.Client.SendMessage(sendJoinMessage, SendMode.Reliable);
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

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            using (Message sendJoinMessage = Message.Create((ushort)OpCodes.LeaveRoom, writer))
                drClient.Client.SendMessage(sendJoinMessage, SendMode.Reliable);
        }
    }

    public override bool ClientSend(int channelId, ArraySegment<byte> segment)
    {
        // Only channels are 0 (reliable), 1 (unreliable)

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(segment.Count);
            writer.Write(segment.Array.Take(segment.Count).ToArray());
            using (Message sendDataMessage = Message.Create((ushort)OpCodes.SendData, writer))
                drClient.Client.SendMessage(sendDataMessage, channelId == 0 ? SendMode.Reliable : SendMode.Unreliable);
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
        ushort userID;
        if (connectedClients.TryGetBySecond(connectionId, out userID))
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                writer.Write(userID);
                using (Message sendKickMessage = Message.Create((ushort)OpCodes.KickPlayer, writer))
                    drClient.Client.SendMessage(sendKickMessage, SendMode.Reliable);
            }

            return true;
        }
        return false;
    }

    public override string ServerGetClientAddress(int connectionId)
    {
        return connectedClients.GetBySecond(connectionId).ToString();
    }

    public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment)
    {
        // TODO: Optimize
        List<ushort> clients = new List<ushort>();

        for (int i = 0; i < connectionIds.Count; i++)
        {
            clients.Add(connectedClients.GetBySecond(connectionIds[i]));
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

        using (DarkRiftWriter writer = DarkRiftWriter.Create())
        {
            writer.Write(maxServerPlayers);
            using (Message sendStartMessage = Message.Create((ushort)OpCodes.CreateRoom, writer))
                drClient.Client.SendMessage(sendStartMessage, SendMode.Reliable);
        }

        // Wait until server is actually ready or 10 seconds have passed and server failed
        int timeOut = 0;
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
            Debug.LogError("FAILED TO CREATE SERVER ON RELAY!");
        }
    }

    public override void ServerStop()
    {
        if (isServer)
        {
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
        drClient.Disconnect();
    }
}

enum OpCodes { Default = 0, RequestID = 1, JoinServer = 2, SendData = 3, GetID = 4, ServerJoined = 5, GetData = 6, CreateRoom = 7, ServerLeft = 8, PlayerDisconnected = 9, RoomCreated = 10, LeaveRoom = 11, KickPlayer = 12 }
