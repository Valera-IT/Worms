using System;
using System.Collections.Generic;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

/// Третья реализация транспорта: через ретранслятор Unity Relay.
///
/// Разница с UdpTransport одна, но решающая: там устройства говорят напрямую и
/// потому обязаны видеть друг друга — то есть сидеть в одной сети. Здесь оба
/// подключаются к серверу Unity, который пересылает пакеты между ними, и
/// домашний роутер с его NAT перестаёт быть препятствием. Платой стал лишний
/// крюк по маршруту: пинг до соседа по комнате складывается из двух дорог до
/// ближайшего к хосту дата-центра.
///
/// Своей надёжной доставки здесь нет: под Relay всё равно нужен Unity
/// Transport, а у него это готовые конвейеры. Дублировать поверх них своё
/// подтверждение с повтором значило бы платить дважды за одно и то же.
public class RelayTransport : INetTransport
{
    // Первый байт каждого пакета: полезная нагрузка или служебный замер
    // задержки. Замер держим здесь, а не выше, чтобы игровому коду не
    // приходилось знать, каким транспортом он сегодня разговаривает.
    const byte KPayload = 0;
    const byte KPing = 1;
    const byte KPong = 2;

    const float PingEvery = 1.0f;

    /// Пакет до фрагментации. Сверка состояния мира в него укладывается с
    /// запасом; на большее конвейер фрагментации всё равно не рассчитан.
    const int FragmentCapacity = 16384;

    NetworkDriver _driver;
    NetworkPipeline _reliable;

    readonly Dictionary<int, NetworkConnection> _peers = new Dictionary<int, NetworkConnection>();
    readonly Dictionary<int, int> _pings = new Dictionary<int, int>();
    readonly Dictionary<int, float> _lastPing = new Dictionary<int, float>();
    readonly byte[] _scratch = new byte[FragmentCapacity];

    readonly bool _isHost;
    int _nextPeerId = 1;
    bool _connected;

    public bool IsHost => _isHost;
    public bool Connected => _connected;
    public int LocalPeer { get; private set; }

    /// Код комнаты, который хост диктует соседям. У клиента — тот, по которому
    /// вошли.
    public string JoinCode { get; }

    public event Action<int> PeerJoined;
    public event Action<int> PeerLeft;
    public event Action<int, byte[], int> Received;

    /// Хост: слушать выданную ретранслятором ячейку.
    public static RelayTransport Host(RelayServerData server, string joinCode)
    {
        var t = new RelayTransport(server, true, joinCode);
        t._driver.Bind(NetworkEndpoint.AnyIpv4);
        if (!t._driver.Bound) throw new Exception("Не удалось занять сокет под ретранслятор");
        t._driver.Listen();
        t.LocalPeer = 0;
        t._connected = true;
        return t;
    }

    /// Клиент: подключиться к ячейке хоста. Номер участника выдаст хост
    /// поверх этого транспорта, как и в игре по адресу.
    public static RelayTransport Join(RelayServerData server, string joinCode)
    {
        var t = new RelayTransport(server, false, joinCode);
        t._driver.Bind(NetworkEndpoint.AnyIpv4);
        if (!t._driver.Bound) throw new Exception("Не удалось занять сокет под ретранслятор");
        t.LocalPeer = -1;
        t._peers[0] = t._driver.Connect();
        return t;
    }

    RelayTransport(RelayServerData server, bool host, string joinCode)
    {
        _isHost = host;
        JoinCode = joinCode;

        var settings = new NetworkSettings();
        settings.WithRelayParameters(ref server);
        settings.WithFragmentationStageParameters(payloadCapacity: FragmentCapacity);

        _driver = NetworkDriver.Create(settings);
        // Фрагментация обязана стоять первой: следующие ступени пакетов
        // длиннее MTU не понимают.
        _reliable = _driver.CreatePipeline(
            typeof(Unity.Networking.Transport.FragmentationPipelineStage),
            typeof(Unity.Networking.Transport.ReliableSequencedPipelineStage));
    }

    static float Now => Time.realtimeSinceStartup;

    // --- цикл -------------------------------------------------------------

    public void Tick()
    {
        if (!_driver.IsCreated) return;
        _driver.ScheduleUpdate().Complete();

        if (_isHost)
        {
            NetworkConnection fresh;
            while ((fresh = _driver.Accept()) != default)
            {
                int id = _nextPeerId++;
                _peers[id] = fresh;
                _lastPing[id] = 0f;
                PeerJoined?.Invoke(id);
            }
        }

        // Ключи копируем: обработчик события может тронуть словарь.
        var ids = new List<int>(_peers.Keys);
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (!_peers.TryGetValue(id, out var conn)) continue;

            NetworkEvent.Type ev;
            while ((ev = _driver.PopEventForConnection(conn, out var reader)) != NetworkEvent.Type.Empty)
            {
                switch (ev)
                {
                    case NetworkEvent.Type.Connect:
                        _connected = true;
                        PeerJoined?.Invoke(id);
                        break;

                    case NetworkEvent.Type.Data:
                        TakeData(id, ref reader);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _peers.Remove(id);
                        if (!_isHost) _connected = false;
                        PeerLeft?.Invoke(id);
                        break;
                }
            }
        }

        Keepalive();
    }

    void TakeData(int peer, ref Unity.Collections.DataStreamReader reader)
    {
        int len = reader.Length;
        if (len < 1) return;

        byte kind = reader.ReadByte();
        int payload = len - 1;

        if (kind == KPing)
        {
            uint stamp = reader.ReadUInt();
            if (!_peers.TryGetValue(peer, out var back)) return;
            if (_driver.BeginSend(back, out var w) != 0) return;
            w.WriteByte(KPong);
            w.WriteUInt(stamp);
            _driver.EndSend(w);
            return;
        }

        if (kind == KPong)
        {
            uint stamp = reader.ReadUInt();
            _pings[peer] = (int)((uint)(Now * 1000f) - stamp);
            return;
        }

        if (kind != KPayload || payload > _scratch.Length) return;
        for (int i = 0; i < payload; i++) _scratch[i] = reader.ReadByte();
        Received?.Invoke(peer, _scratch, payload);
    }

    void Keepalive()
    {
        float now = Now;
        foreach (var kv in _peers)
        {
            if (!_lastPing.TryGetValue(kv.Key, out float last)) last = 0f;
            if (now - last < PingEvery) continue;
            _lastPing[kv.Key] = now;

            if (_driver.BeginSend(kv.Value, out var w) != 0) continue;
            w.WriteByte(KPing);
            w.WriteUInt((uint)(now * 1000f));
            _driver.EndSend(w);
        }
    }

    // --- отправка ---------------------------------------------------------

    public void Send(int peer, byte[] data, int length, NetChannel channel)
    {
        if (!_isHost) peer = 0;
        if (!_peers.TryGetValue(peer, out var conn)) return;
        RawSend(conn, KPayload, data, length, channel);
    }

    public void Broadcast(byte[] data, int length, NetChannel channel, int except = -1)
    {
        foreach (var kv in _peers)
        {
            if (kv.Key == except) continue;
            RawSend(kv.Value, KPayload, data, length, channel);
        }
    }

    void RawSend(NetworkConnection conn, byte kind, byte[] data, int length, NetChannel channel)
    {
        var pipe = channel == NetChannel.Reliable ? _reliable : NetworkPipeline.Null;
        if (_driver.BeginSend(pipe, conn, out var w, length + 1) != 0) return;
        w.WriteByte(kind);
        for (int i = 0; i < length; i++) w.WriteByte(data[i]);
        _driver.EndSend(w);
    }

    public int PingTo(int peer) => _pings.TryGetValue(peer, out int p) ? p : -1;

    public void Dispose()
    {
        if (!_driver.IsCreated) return;
        foreach (var kv in _peers) _driver.Disconnect(kv.Value);
        _driver.ScheduleUpdate().Complete();
        _peers.Clear();
        _driver.Dispose();
        _connected = false;
    }
}
