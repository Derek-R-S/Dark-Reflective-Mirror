// This is an optional module for adding direct connect support

using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(DarkReflectiveMirrorTransport))]
public class DarkMirrorDirectConnectModule : MonoBehaviour
{
    public Transport directConnectTransport;
    private DarkReflectiveMirrorTransport darkMirrorTransport;

    void Awake()
    {
        darkMirrorTransport = GetComponent<DarkReflectiveMirrorTransport>();

        if(directConnectTransport == null)
        {
            Debug.Log("Direct Connect Transport is null!");
            return;
        }

        if(directConnectTransport is DarkReflectiveMirrorTransport)
        {
            Debug.Log("Direct Connect Transport Cannot be the relay, silly. :P");
            return;
        }

        directConnectTransport.OnServerConnected.AddListener(OnServerConnected);
        directConnectTransport.OnServerDataReceived.AddListener(OnServerDataReceived);
        directConnectTransport.OnServerDisconnected.AddListener(OnServerDisconnected);
        directConnectTransport.OnServerError.AddListener(OnServerError);
        directConnectTransport.OnClientConnected.AddListener(OnClientConnected);
        directConnectTransport.OnClientDataReceived.AddListener(OnClientDataReceived);
        directConnectTransport.OnClientDisconnected.AddListener(OnClientDisconnected);
        directConnectTransport.OnClientError.AddListener(OnClientError);
    }

    void OnDisable()
    {
        if (directConnectTransport == null || directConnectTransport is DarkReflectiveMirrorTransport)
            return;

        directConnectTransport.OnServerConnected.RemoveListener(OnServerConnected);
        directConnectTransport.OnServerDataReceived.RemoveListener(OnServerDataReceived);
        directConnectTransport.OnServerDisconnected.RemoveListener(OnServerDisconnected);
        directConnectTransport.OnServerError.RemoveListener(OnServerError);
        directConnectTransport.OnClientConnected.RemoveListener(OnClientConnected);
        directConnectTransport.OnClientDataReceived.RemoveListener(OnClientDataReceived);
        directConnectTransport.OnClientDisconnected.RemoveListener(OnClientDisconnected);
        directConnectTransport.OnClientError.RemoveListener(OnClientError);
    }

    public void StartServer()
    {
        directConnectTransport.ServerStart();
        if (darkMirrorTransport.showDebugLogs)
            Debug.Log("Direct Connect Server Created!");
    }

    public void StopServer()
    {
        directConnectTransport.ServerStop();
    }

    public void JoinServer(string ip)
    {
        directConnectTransport.ClientConnect(ip);
    }

    public void KickClient(int clientID)
    {
        if (darkMirrorTransport.showDebugLogs)
            Debug.Log("Kicked direct connect client.");
        directConnectTransport.ServerDisconnect(clientID);
    }

    public void ClientDisconnect()
    {
        directConnectTransport.ClientDisconnect();
    }

    public void ServerSend(List<int> clientIDs, ArraySegment<byte> data, int channel)
    {
        directConnectTransport.ServerSend(clientIDs, channel, data);
    }

    public bool ClientSend(ArraySegment<byte> data, int channel)
    {
        return directConnectTransport.ClientSend(channel, data);
    }

    #region Transport Callbacks
    void OnServerConnected(int clientID)
    {
        if (darkMirrorTransport.showDebugLogs)
            Debug.Log("Direct Connect Client Connected");
        darkMirrorTransport.DirectAddClient(clientID);
    }

    void OnServerDataReceived(int clientID, ArraySegment<byte> data, int channel)
    {
        darkMirrorTransport.DirectReceiveData(data, channel, clientID);
    }

    void OnServerDisconnected(int clientID)
    {
        darkMirrorTransport.DirectRemoveClient(clientID);
    }

    void OnServerError(int client, Exception error)
    {
        if (darkMirrorTransport.showDebugLogs)
            Debug.Log("Direct Server Error: " + error);
    }

    void OnClientConnected()
    {
        if (darkMirrorTransport.showDebugLogs)
            Debug.Log("Direct Connect Client Joined");

        darkMirrorTransport.DirectClientConnected();
    }

    void OnClientDisconnected()
    {
        darkMirrorTransport.DirectDisconnected();
    }

    void OnClientDataReceived(ArraySegment<byte> data, int channel)
    {
        darkMirrorTransport.DirectReceiveData(data, channel);
    }

    void OnClientError(Exception error)
    {
        if (darkMirrorTransport.showDebugLogs)
            Debug.Log("Direct Client Error: " + error);
    }
    #endregion
}
