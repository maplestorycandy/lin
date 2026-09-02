using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace IdleLineage.Network;

public sealed class NetworkManager
{
    private static NetworkManager? _instance;
    public static NetworkManager Instance => _instance ??= new NetworkManager();

    public bool IsHost { get; private set; }
    public bool IsConnected { get; private set; }
    public string LocalPlayerId { get; private set; } = Guid.NewGuid().ToString("N")[..8];
    public int Port { get; private set; } = 7777;

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _clientStream;
    private readonly List<ConnectedPeer> _serverPeers = new();
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private CancellationTokenSource? _cts;

    // Events for Game UI & ARPG Screen
    public event Action<HandshakePacket>? OnRemotePlayerJoined;
    public event Action<MovePacket>? OnRemotePlayerMoved;
    public event Action<ActionPacket>? OnRemotePlayerAction;
    public event Action<ChatPacket>? OnChatReceived;
    public event Action<string>? OnRemotePlayerLeft;
    public event Action<string>? OnStatusChanged;

    public readonly Dictionary<string, HandshakePacket> ConnectedPlayers = new();

    private class ConnectedPeer
    {
        public string Id { get; set; } = "";
        public TcpClient Client { get; set; } = null!;
        public NetworkStream Stream { get; set; } = null!;
        public HandshakePacket? Handshake { get; set; }
    }

    public static List<string> GetLocalIpAddresses()
    {
        var ips = new List<string>();
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    ips.Add(ip.ToString());
                }
            }
        }
        catch { }
        if (ips.Count == 0) ips.Add("127.0.0.1");
        return ips;
    }

    public void StartHost(int port = 7777)
    {
        Stop();
        Port = port;
        IsHost = true;
        IsConnected = true;
        _cts = new CancellationTokenSource();
        ConnectedPlayers.Clear();

        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Task.Run(() => ServerListenLoop(_cts.Token), _cts.Token);
            OnStatusChanged?.Invoke($"[伺服器] 已啟動，監聽通訊埠 {port}");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            OnStatusChanged?.Invoke($"[伺服器] 啟動失敗: {ex.Message}");
        }
    }

    public void ConnectToHost(string hostIp, int port = 7777)
    {
        Stop();
        Port = port;
        IsHost = false;
        _cts = new CancellationTokenSource();
        ConnectedPlayers.Clear();

        Task.Run(async () =>
        {
            try
            {
                _mainThreadQueue.Enqueue(() => OnStatusChanged?.Invoke($"[連線] 正在連線至 {hostIp}:{port}..."));
                _client = new TcpClient();
                await _client.ConnectAsync(hostIp, port);
                _clientStream = _client.GetStream();
                IsConnected = true;
                _mainThreadQueue.Enqueue(() => OnStatusChanged?.Invoke($"[連線] 成功連接至房間！"));
                _ = ClientReceiveLoop(_cts.Token);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                _mainThreadQueue.Enqueue(() => OnStatusChanged?.Invoke($"[連線] 連線失敗: {ex.Message}"));
            }
        });
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;

        if (IsHost && _listener != null)
        {
            try { _listener.Stop(); } catch { }
            _listener = null;
            lock (_serverPeers)
            {
                foreach (var p in _serverPeers)
                {
                    try { p.Client.Close(); } catch { }
                }
                _serverPeers.Clear();
            }
        }

        if (_client != null)
        {
            try { _client.Close(); } catch { }
            _client = null;
            _clientStream = null;
        }

        IsConnected = false;
        IsHost = false;
        ConnectedPlayers.Clear();
    }

    public void Update()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); } catch (Exception ex) { GD.PushError($"[Network] MainThread exception: {ex}"); }
        }
    }

    public void SendHandshake(HandshakePacket handshake)
    {
        handshake.PlayerId = LocalPlayerId;
        Broadcast(NetEnvelope.Create(NetPacketType.Handshake, handshake));
    }

    public void SendMove(MovePacket move)
    {
        move.PlayerId = LocalPlayerId;
        Broadcast(NetEnvelope.Create(NetPacketType.Move, move));
    }

    public void SendAction(ActionPacket action)
    {
        action.PlayerId = LocalPlayerId;
        Broadcast(NetEnvelope.Create(NetPacketType.Action, action));
    }

    public void SendChat(string senderName, string message, string colorHex = "#66d9ef")
    {
        var chat = new ChatPacket
        {
            SenderId = LocalPlayerId,
            SenderName = senderName,
            Message = message,
            ColorHex = colorHex
        };
        Broadcast(NetEnvelope.Create(NetPacketType.Chat, chat));
    }

    private void Broadcast(NetEnvelope envelope)
    {
        if (!IsConnected) return;

        string json = JsonSerializer.Serialize(envelope) + "\n";
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        if (IsHost)
        {
            lock (_serverPeers)
            {
                for (int i = _serverPeers.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _serverPeers[i].Stream.Write(bytes, 0, bytes.Length);
                    }
                    catch
                    {
                        _serverPeers.RemoveAt(i);
                    }
                }
            }
        }
        else if (_clientStream != null && _client != null && _client.Connected)
        {
            try
            {
                _clientStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                _mainThreadQueue.Enqueue(() => OnStatusChanged?.Invoke($"[連線] 傳送中斷: {ex.Message}"));
            }
        }
    }

    private async Task ServerListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(token);
                var peer = new ConnectedPeer
                {
                    Client = tcpClient,
                    Stream = tcpClient.GetStream()
                };
                lock (_serverPeers) { _serverPeers.Add(peer); }
                _ = ServerPeerLoop(peer, token);
            }
            catch { break; }
        }
    }

    private async Task ServerPeerLoop(ConnectedPeer peer, CancellationToken token)
    {
        using var reader = new StreamReader(peer.Stream, Encoding.UTF8);
        try
        {
            while (!token.IsCancellationRequested && peer.Client.Connected)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var envelope = JsonSerializer.Deserialize<NetEnvelope>(line);
                if (envelope == null) continue;

                // Process locally on host
                _mainThreadQueue.Enqueue(() => HandleEnvelope(envelope, peer));

                // Forward to all other clients
                byte[] forwardBytes = Encoding.UTF8.GetBytes(line + "\n");
                lock (_serverPeers)
                {
                    foreach (var other in _serverPeers)
                    {
                        if (other != peer)
                        {
                            try { other.Stream.Write(forwardBytes, 0, forwardBytes.Length); } catch { }
                        }
                    }
                }
            }
        }
        catch { }
        finally
        {
            lock (_serverPeers) { _serverPeers.Remove(peer); }
            if (!string.IsNullOrEmpty(peer.Id))
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    ConnectedPlayers.Remove(peer.Id);
                    OnRemotePlayerLeft?.Invoke(peer.Id);
                });
                var leaveEnv = NetEnvelope.Create(NetPacketType.Leave, new LeavePacket { PlayerId = peer.Id });
                Broadcast(leaveEnv);
            }
        }
    }

    private async Task ClientReceiveLoop(CancellationToken token)
    {
        if (_clientStream == null) return;
        using var reader = new StreamReader(_clientStream, Encoding.UTF8);
        try
        {
            while (!token.IsCancellationRequested && _client != null && _client.Connected)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var envelope = JsonSerializer.Deserialize<NetEnvelope>(line);
                if (envelope == null) continue;

                _mainThreadQueue.Enqueue(() => HandleEnvelope(envelope, null));
            }
        }
        catch { }
        finally
        {
            _mainThreadQueue.Enqueue(() =>
            {
                IsConnected = false;
                OnStatusChanged?.Invoke("[連線] 與伺服器連線已中斷");
            });
        }
    }

    private void HandleEnvelope(NetEnvelope envelope, ConnectedPeer? senderPeer)
    {
        switch (envelope.Type)
        {
            case NetPacketType.Handshake:
                var handshake = envelope.Deserialize<HandshakePacket>();
                if (handshake != null && handshake.PlayerId != LocalPlayerId)
                {
                    if (senderPeer != null) senderPeer.Id = handshake.PlayerId;
                    ConnectedPlayers[handshake.PlayerId] = handshake;
                    OnRemotePlayerJoined?.Invoke(handshake);
                }
                break;

            case NetPacketType.Move:
                var move = envelope.Deserialize<MovePacket>();
                if (move != null && move.PlayerId != LocalPlayerId)
                {
                    OnRemotePlayerMoved?.Invoke(move);
                }
                break;

            case NetPacketType.Action:
                var action = envelope.Deserialize<ActionPacket>();
                if (action != null && action.PlayerId != LocalPlayerId)
                {
                    OnRemotePlayerAction?.Invoke(action);
                }
                break;

            case NetPacketType.Chat:
                var chat = envelope.Deserialize<ChatPacket>();
                if (chat != null)
                {
                    OnChatReceived?.Invoke(chat);
                }
                break;

            case NetPacketType.Leave:
                var leave = envelope.Deserialize<LeavePacket>();
                if (leave != null)
                {
                    ConnectedPlayers.Remove(leave.PlayerId);
                    OnRemotePlayerLeft?.Invoke(leave.PlayerId);
                }
                break;
        }
    }
}
