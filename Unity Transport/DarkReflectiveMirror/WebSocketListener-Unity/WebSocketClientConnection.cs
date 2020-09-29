using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DarkRift;
using DarkRift.Client;
using System.Runtime.InteropServices;

public class WebSocketClientConnection : NetworkClientConnection
{
    public override IEnumerable<IPEndPoint> RemoteEndPoints => new IPEndPoint[1] { dummyEndPoint };
    private WebSocket socket = null;
    private IPEndPoint dummyEndPoint = new IPEndPoint(1, 1);
    private bool verified = false;

    public override IPEndPoint GetRemoteEndPoint(string name){
        return dummyEndPoint;
    }

    public WebSocketClientConnection(string address, int port){
        socket = new WebSocket(new Uri($"ws://{address}:{port}"));
        WebSocketUnityUpdater.Initialize(this);
    }

    public override void Connect(){
        socket.Connect();
    }

    public WebSocket GetSocket(){
        return socket;
    }

    public override bool Disconnect(){
        socket.Close();
        return true;
    }

    public void Update()
    {
        if (socket != null)
        {
            byte[] data = socket.Recv();
            if (data == null || data.Length == 0)
                return;

            using (MessageBuffer buffer = MessageBuffer.Create(data.Length))
            {
                Buffer.BlockCopy(data, 0, buffer.Buffer, 0, data.Length);
                buffer.Count = data.Length;
                HandleMessageReceived(buffer, SendMode.Reliable);
            }
        }
    }

    public override bool SendMessageReliable(MessageBuffer message){
        byte[] messageData = new byte[message.Count];
        Buffer.BlockCopy(message.Buffer, message.Offset, messageData, 0, message.Count);
        socket.Send(messageData);
        return true;
    }

    public override bool SendMessageUnreliable(MessageBuffer message){
        SendMessageReliable(message);
        return true;
    }

    public override ConnectionState ConnectionState => StateToDR2State(socket.GetState());

    ConnectionState StateToDR2State(int state)
    {
        switch (state)
        {
            case (3):
                return ConnectionState.Disconnected;
            case (2):
                return ConnectionState.Disconnecting;
            case (0):
                return ConnectionState.Connecting;
            default:
                return ConnectionState.Connected;
        }
    }
}

public class WebSocketUnityUpdater : MonoBehaviour {
    public static WebSocketUnityUpdater instance;
    private WebSocketClientConnection connection;

    void Awake(){
        if(instance == null){
            instance = this;
            DontDestroyOnLoad(this);
        }else{
            Destroy(this);
        }
    }

    void Update(){
        connection?.Update();
    }

    public static void Initialize(WebSocketClientConnection connection){
        if(instance == null){
            instance = new GameObject("WebSocket Listener").AddComponent<WebSocketUnityUpdater>();
        }

        instance.connection = connection;
    }
}