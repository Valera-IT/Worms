using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

/// Транспорт поверх голого UDP: хост слушает порт, клиенты шлют ему пакеты.
///
/// Почему не TCP и не готовая библиотека. Игра пошаговая, и характер трафика
/// у неё двойной: поток ввода активного игрока, где свежий кадр важнее
/// потерянного, и редкие важные сообщения — старт матча, смена хода, сверка
/// состояния, — которые обязаны дойти в порядке. TCP на первом даёт затор
/// из-за повторов того, что уже неактуально; поэтому здесь два канала над
/// одним сокетом: ненадёжный без всяких обязательств и надёжный с
/// накопительным подтверждением и повтором.
///
/// Никаких потоков: пакеты разгребаются в Tick из главного цикла. Пошаговой
/// игре с десятком сообщений в секунду этого хватает, а отладка остаётся
/// однопоточной.
public class UdpTransport : INetTransport
{
    // --- формат пакета ---------------------------------------------------
    // [вид:1][подтверждение:2] и дальше по виду.
    // Подтверждение едет в каждом пакете: это последний номер надёжного
    // сообщения, принятый отправителем пакета без пропусков.

    const byte KConnect = 0;     // клиент → хост: хочу войти
    const byte KAccept = 1;      // хост → клиент: принят, вот твой номер
    const byte KReject = 2;      // хост → клиент: не принят, вот причина
    const byte KUnreliable = 3;  // полезная нагрузка без гарантий
    const byte KReliable = 4;    // [номер:2][ещё будет:1] и данные
    const byte KAck = 5;         // пустой пакет только ради подтверждения
    const byte KPing = 6;        // [метка времени:4]
    const byte KPong = 7;        // эхо метки
    const byte KBye = 8;         // ухожу

    /// Сколько данных кладём в один пакет. 1200 байт проходят через любой
    /// разумный путь без фрагментации на уровне IP; надёжные сообщения
    /// длиннее режутся на куски и собираются обратно.
    const int Mtu = 1200;

    const float ResendAfter = 0.12f;   // повтор неподтверждённого
    const float PingEvery = 1.0f;
    const float TimeoutAfter = 6.0f;   // молчит дольше — считаем ушедшим

    class Peer
    {
        public int Id;
        public IPEndPoint End;

        // исходящий надёжный поток
        public ushort NextSeq = 1;
        public readonly List<Pending> Unacked = new List<Pending>();

        // входящий надёжный поток
        public ushort Expected = 1;
        public readonly Dictionary<ushort, byte[]> Held = new Dictionary<ushort, byte[]>();
        public readonly List<byte> Assembly = new List<byte>();

        public ushort AckOut;      // что подтверждаем мы
        public float LastHeard;
        public float LastPing;
        public int Ping = -1;
    }

    struct Pending
    {
        public ushort Seq;
        public byte[] Data;
        public int Length;
        public float LastSent;
    }

    readonly Socket _sock;
    readonly Dictionary<int, Peer> _peers = new Dictionary<int, Peer>();
    readonly byte[] _rx = new byte[2048];
    readonly byte[] _tx = new byte[Mtu + 8];

    readonly bool _isHost;
    int _localPeer;
    int _nextPeerId = 1;
    IPEndPoint _hostEnd;
    bool _connected;
    float _lastConnectTry;
    string _rejectReason;

    public bool IsHost => _isHost;
    public bool Connected => _connected;
    public int LocalPeer => _localPeer;
    public string RejectReason => _rejectReason;

    public event Action<int> PeerJoined;
    public event Action<int> PeerLeft;
    public event Action<int, byte[], int> Received;

    /// Сколько мест ещё свободно. Хост отказывает сверх лимита, чтобы
    /// шестой игрок не входил в матч на четверых.
    public int MaxPeers = 3;

    /// Поднять хост на порту.
    public static UdpTransport Host(int port) => new UdpTransport(true, port, null);

    /// Подключиться к хосту по адресу.
    public static UdpTransport Join(string address, int port)
    {
        var ip = ResolveFirst(address);
        if (ip == null) throw new ArgumentException("Не разобрал адрес: " + address);
        return new UdpTransport(false, 0, new IPEndPoint(ip, port));
    }

    UdpTransport(bool host, int port, IPEndPoint hostEnd)
    {
        _isHost = host;
        _hostEnd = hostEnd;
        _localPeer = host ? 0 : -1;

        _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _sock.Blocking = false;
        // Windows иначе роняет сокет ошибкой на ICMP «порт недоступен» от
        // ещё не поднятого хоста — соединение обрывается на ровном месте.
        try { _sock.IOControl(unchecked((int)0x9800000C), new byte[] { 0, 0, 0, 0 }, null); }
        catch (Exception) { /* не Windows — и не надо */ }

        _sock.Bind(new IPEndPoint(IPAddress.Any, host ? port : 0));

        if (host)
        {
            _connected = true;
        }
        else
        {
            var p = new Peer { Id = 0, End = hostEnd, LastHeard = Now };
            _peers[0] = p;
            SendConnect();
        }
    }

    static float Now => Time.realtimeSinceStartup;

    static IPAddress ResolveFirst(string address)
    {
        if (IPAddress.TryParse(address, out var direct)) return direct;
        try
        {
            var entry = Dns.GetHostEntry(address);
            foreach (var a in entry.AddressList)
                if (a.AddressFamily == AddressFamily.InterNetwork) return a;
        }
        catch (Exception) { }
        return null;
    }

    /// Адрес, который хост диктует соседу по комнате. Ищем адрес в локальной
    /// сети, а не петлю: подсказать «127.0.0.1» второму устройству бесполезно.
    public static string LocalAddress()
    {
        try
        {
            using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                // Соединение UDP-сокета никого не трогает по сети, но
                // заставляет систему выбрать исходящий интерфейс — и он же
                // виден соседям по Wi-Fi.
                probe.Connect("192.168.1.1", 9);
                if (probe.LocalEndPoint is IPEndPoint le) return le.Address.ToString();
            }
        }
        catch (Exception) { }
        return "127.0.0.1";
    }

    public int Port => _sock.LocalEndPoint is IPEndPoint le ? le.Port : 0;

    // --- отправка --------------------------------------------------------

    public void Send(int peer, byte[] data, int length, NetChannel channel)
    {
        if (!_isHost) peer = 0;
        if (!_peers.TryGetValue(peer, out var p)) return;
        if (channel == NetChannel.Reliable) SendReliable(p, data, length);
        else SendUnreliable(p, data, length);
    }

    public void Broadcast(byte[] data, int length, NetChannel channel, int except = -1)
    {
        foreach (var kv in _peers)
        {
            if (kv.Key == except) continue;
            if (channel == NetChannel.Reliable) SendReliable(kv.Value, data, length);
            else SendUnreliable(kv.Value, data, length);
        }
    }

    void SendUnreliable(Peer p, byte[] data, int length)
    {
        if (length > Mtu - 3)
        {
            Debug.LogWarning($"[net] пакет без гарантий длиной {length} не влезает, выброшен");
            return;
        }
        _tx[0] = KUnreliable;
        _tx[1] = (byte)(p.AckOut & 0xFF);
        _tx[2] = (byte)(p.AckOut >> 8);
        Array.Copy(data, 0, _tx, 3, length);
        RawSend(p, _tx, length + 3);
    }

    void SendReliable(Peer p, byte[] data, int length)
    {
        // Длинное сообщение (сверка состояния мира) режется на куски.
        // Надёжный канал упорядочен, поэтому сборка на той стороне — это
        // просто склейка подряд идущих кусков до того, у которого снят
        // признак «ещё будет».
        const int room = Mtu - 6;
        int offset = 0;
        do
        {
            int take = Mathf.Min(room, length - offset);
            bool more = offset + take < length;
            var frame = new byte[take + 6];
            frame[0] = KReliable;
            // подтверждение проставим при самой отправке — оно свежее
            ushort seq = p.NextSeq++;
            frame[3] = (byte)(seq & 0xFF);
            frame[4] = (byte)(seq >> 8);
            frame[5] = more ? (byte)1 : (byte)0;
            Array.Copy(data, offset, frame, 6, take);
            p.Unacked.Add(new Pending { Seq = seq, Data = frame, Length = frame.Length, LastSent = 0f });
            offset += take;
        } while (offset < length);

        FlushUnacked(p);
    }

    /// Новое (LastSent == 0) уходит сразу, отправленное недавно ждёт своей
    /// очереди на повтор.
    void FlushUnacked(Peer p)
    {
        float now = Now;
        for (int i = 0; i < p.Unacked.Count; i++)
        {
            var pend = p.Unacked[i];
            if (pend.LastSent > 0f && now - pend.LastSent < ResendAfter) continue;
            pend.Data[1] = (byte)(p.AckOut & 0xFF);
            pend.Data[2] = (byte)(p.AckOut >> 8);
            RawSend(p, pend.Data, pend.Length);
            pend.LastSent = now;
            p.Unacked[i] = pend;
        }
    }

    void SendConnect()
    {
        if (!_peers.TryGetValue(0, out var p)) return;
        _tx[0] = KConnect;
        _tx[1] = 0; _tx[2] = 0;
        _tx[3] = (byte)(NetVersion.Current & 0xFF);
        _tx[4] = (byte)(NetVersion.Current >> 8);
        RawSend(p, _tx, 5);
    }

    void SendBare(Peer p, byte kind)
    {
        _tx[0] = kind;
        _tx[1] = (byte)(p.AckOut & 0xFF);
        _tx[2] = (byte)(p.AckOut >> 8);
        RawSend(p, _tx, 3);
    }

    void RawSend(Peer p, byte[] data, int length)
    {
        try { _sock.SendTo(data, 0, length, SocketFlags.None, p.End); }
        catch (SocketException e) { Debug.LogWarning($"[net] не отправилось: {e.SocketErrorCode}"); }
    }

    // --- приём -----------------------------------------------------------

    public void Tick()
    {
        Receive();
        Maintain();
    }

    void Receive()
    {
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);
        while (true)
        {
            int n;
            try
            {
                if (_sock.Available <= 0) return;
                n = _sock.ReceiveFrom(_rx, 0, _rx.Length, SocketFlags.None, ref from);
            }
            catch (SocketException) { return; }
            if (n < 3) continue;

            var end = (IPEndPoint)from;
            byte kind = _rx[0];
            ushort ack = (ushort)(_rx[1] | (_rx[2] << 8));
            var peer = FindPeer(end);

            if (peer == null)
            {
                if (_isHost && kind == KConnect) AcceptPeer(end);
                continue;
            }

            peer.LastHeard = Now;
            AcceptAck(peer, ack);

            switch (kind)
            {
                case KConnect:
                    // Повтор от клиента, который не услышал Accept.
                    if (_isHost) SendAccept(peer);
                    break;

                case KAccept:
                    if (!_isHost && !_connected)
                    {
                        _localPeer = _rx[3] | (_rx[4] << 8);
                        _connected = true;
                        PeerJoined?.Invoke(0);
                    }
                    break;

                case KReject:
                    if (!_isHost)
                    {
                        var rr = new NetReader(_rx, 3, n - 3);
                        _rejectReason = rr.Str();
                        _connected = false;
                        PeerLeft?.Invoke(0);
                    }
                    break;

                case KUnreliable:
                    Received?.Invoke(peer.Id, Slice(_rx, 3, n - 3), n - 3);
                    break;

                case KReliable:
                    TakeReliable(peer, n);
                    break;

                case KPing:
                    _tx[0] = KPong;
                    _tx[1] = (byte)(peer.AckOut & 0xFF);
                    _tx[2] = (byte)(peer.AckOut >> 8);
                    Array.Copy(_rx, 3, _tx, 3, 4);
                    RawSend(peer, _tx, 7);
                    break;

                case KPong:
                {
                    var rr = new NetReader(_rx, 3, 4);
                    uint sent = rr.U32();
                    uint now = (uint)(Now * 1000f);
                    peer.Ping = (int)(now - sent);
                    break;
                }

                case KBye:
                    Drop(peer);
                    break;
            }
        }
    }

    static byte[] Slice(byte[] src, int offset, int length)
    {
        var outp = new byte[Mathf.Max(0, length)];
        if (length > 0) Array.Copy(src, offset, outp, 0, length);
        return outp;
    }

    void TakeReliable(Peer p, int n)
    {
        if (n < 6) return;
        ushort seq = (ushort)(_rx[3] | (_rx[4] << 8));
        bool more = _rx[5] != 0;
        var body = Slice(_rx, 5, n - 5); // первый байт тела — признак «ещё будет»

        // Уже приняли или ещё не дошла очередь — кладём и подтверждаем.
        if (SeqOlder(seq, p.Expected)) { SendBare(p, KAck); return; }
        if (!p.Held.ContainsKey(seq)) p.Held[seq] = body;

        while (p.Held.TryGetValue(p.Expected, out var chunk))
        {
            p.Held.Remove(p.Expected);
            p.AckOut = p.Expected;
            p.Expected++;
            bool chunkMore = chunk[0] != 0;
            for (int i = 1; i < chunk.Length; i++) p.Assembly.Add(chunk[i]);
            if (!chunkMore)
            {
                var msg = p.Assembly.ToArray();
                p.Assembly.Clear();
                Received?.Invoke(p.Id, msg, msg.Length);
            }
        }
        SendBare(p, KAck);
    }

    /// Номера кольцевые: 65535 старше единицы, а не наоборот.
    static bool SeqOlder(ushort a, ushort b) => (short)(a - b) < 0;

    void AcceptAck(Peer p, ushort ack)
    {
        if (ack == 0) return;
        for (int i = p.Unacked.Count - 1; i >= 0; i--)
            if (!SeqOlder(ack, p.Unacked[i].Seq)) p.Unacked.RemoveAt(i);
    }

    Peer FindPeer(IPEndPoint end)
    {
        foreach (var kv in _peers)
            if (kv.Value.End.Equals(end)) return kv.Value;
        return null;
    }

    void AcceptPeer(IPEndPoint end)
    {
        ushort theirVersion = 0;
        if (_rx.Length >= 5) theirVersion = (ushort)(_rx[3] | (_rx[4] << 8));

        var p = new Peer { Id = _nextPeerId, End = end, LastHeard = Now };

        if (theirVersion != NetVersion.Current)
        {
            RejectPeer(p, $"Версии игры разные: у вас {theirVersion}, у хоста {NetVersion.Current}");
            return;
        }
        if (_peers.Count >= MaxPeers)
        {
            RejectPeer(p, "В комнате нет свободных мест");
            return;
        }

        _nextPeerId++;
        _peers[p.Id] = p;
        SendAccept(p);
        PeerJoined?.Invoke(p.Id);
    }

    void SendAccept(Peer p)
    {
        _tx[0] = KAccept;
        _tx[1] = (byte)(p.AckOut & 0xFF);
        _tx[2] = (byte)(p.AckOut >> 8);
        _tx[3] = (byte)(p.Id & 0xFF);
        _tx[4] = (byte)(p.Id >> 8);
        RawSend(p, _tx, 5);
    }

    void RejectPeer(Peer p, string why)
    {
        var w = new NetWriter(64);
        w.Str(why);
        _tx[0] = KReject;
        _tx[1] = 0; _tx[2] = 0;
        Array.Copy(w.Buffer, 0, _tx, 3, w.Length);
        RawSend(p, _tx, 3 + w.Length);
    }

    void Maintain()
    {
        float now = Now;
        List<Peer> lost = null;

        foreach (var kv in _peers)
        {
            var p = kv.Value;
            FlushUnacked(p);

            if (now - p.LastPing >= PingEvery)
            {
                p.LastPing = now;
                var w = new NetWriter(8);
                w.U32((uint)(now * 1000f));
                _tx[0] = KPing;
                _tx[1] = (byte)(p.AckOut & 0xFF);
                _tx[2] = (byte)(p.AckOut >> 8);
                Array.Copy(w.Buffer, 0, _tx, 3, 4);
                RawSend(p, _tx, 7);
            }


            if (now - p.LastHeard > TimeoutAfter)
                (lost ??= new List<Peer>()).Add(p);
        }

        // Клиент, которого ещё не приняли, повторяет заявку: первый пакет
        // мог потеряться, а хост мог быть ещё не поднят.
        if (!_isHost && !_connected && now - _lastConnectTry >= 0.5f)
        {
            _lastConnectTry = now;
            SendConnect();
        }

        if (lost != null)
            foreach (var p in lost) Drop(p);
    }

    void Drop(Peer p)
    {
        _peers.Remove(p.Id);
        if (!_isHost) _connected = false;
        PeerLeft?.Invoke(p.Id);
    }

    public int PingTo(int peer) => _peers.TryGetValue(peer, out var p) ? p.Ping : -1;

    public void Dispose()
    {
        foreach (var kv in _peers) SendBare(kv.Value, KBye);
        _peers.Clear();
        try { _sock.Close(); } catch (Exception) { }
        _connected = false;
    }
}
