using System;
using System.Collections.Generic;
using UnityEngine;

/// Транспорт-петля: хост и клиенты живут в одном процессе и передают друг
/// другу байты через очередь. Сокетов нет вообще.
///
/// Нужен ради тестов. Матч по сети иначе не проверить в редакторе: поднимать
/// вторую копию Unity ради каждого прогона — это не тест, а ритуал. Здесь же
/// весь протокол — комната, ввод, сверка состояния — гоняется в одном
/// PlayMode, и задержку можно задать вручную, чтобы поймать то, что на
/// мгновенной доставке никогда не всплывёт.
public class LoopTransport : INetTransport
{
    struct Parcel
    {
        public int From;
        public byte[] Data;
        public float DueAt;
    }

    static readonly Dictionary<string, LoopTransport> Hosts = new Dictionary<string, LoopTransport>();

    readonly string _room;
    readonly bool _isHost;
    readonly List<Parcel> _inbox = new List<Parcel>();
    readonly Dictionary<int, LoopTransport> _links = new Dictionary<int, LoopTransport>();

    int _localPeer;
    int _nextPeerId = 1;
    bool _connected;

    /// Задержка доставки в секундах в одну сторону. Ноль — мгновенно.
    public float Latency;

    public bool IsHost => _isHost;
    public bool Connected => _connected;
    public int LocalPeer => _localPeer;

    public event Action<int> PeerJoined;
    public event Action<int> PeerLeft;
    public event Action<int, byte[], int> Received;

    public static LoopTransport Host(string room)
    {
        var t = new LoopTransport(room, true) { _localPeer = 0, _connected = true };
        Hosts[room] = t;
        return t;
    }

    public static LoopTransport Join(string room)
    {
        if (!Hosts.TryGetValue(room, out var host))
            throw new InvalidOperationException("Нет комнаты " + room);

        var client = new LoopTransport(room, false);
        int id = host._nextPeerId++;
        client._localPeer = id;
        client._connected = true;
        client._links[0] = host;
        host._links[id] = client;
        host.PeerJoined?.Invoke(id);
        client.PeerJoined?.Invoke(0);
        return client;
    }

    LoopTransport(string room, bool host)
    {
        _room = room;
        _isHost = host;
    }

    public void Tick()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = 0; i < _inbox.Count; i++)
        {
            if (_inbox[i].DueAt > now) continue;
            var parcel = _inbox[i];
            _inbox.RemoveAt(i);
            i--;
            Received?.Invoke(parcel.From, parcel.Data, parcel.Data.Length);
        }
    }

    public void Send(int peer, byte[] data, int length, NetChannel channel)
    {
        if (!_isHost) peer = 0;
        if (!_links.TryGetValue(peer, out var target)) return;

        var copy = new byte[length];
        Array.Copy(data, copy, length);
        target._inbox.Add(new Parcel
        {
            From = _isHost ? 0 : _localPeer,
            Data = copy,
            DueAt = Time.realtimeSinceStartup + Latency
        });
    }

    public void Broadcast(byte[] data, int length, NetChannel channel, int except = -1)
    {
        foreach (var kv in _links)
        {
            if (kv.Key == except) continue;
            Send(kv.Key, data, length, channel);
        }
    }

    public int PingTo(int peer) => Mathf.RoundToInt(Latency * 2000f);

    public void Dispose()
    {
        foreach (var kv in _links)
        {
            var other = kv.Value;
            int me = _isHost ? 0 : _localPeer;
            other._links.Remove(me);
            other.PeerLeft?.Invoke(me);
        }
        _links.Clear();
        _connected = false;
        if (_isHost) Hosts.Remove(_room);
    }
}
