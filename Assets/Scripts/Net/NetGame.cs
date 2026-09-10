using System;
using System.Collections.Generic;
using UnityEngine;

public enum NetRole { Offline, Host, Client }

/// Где сейчас сетевая часть. Матч в этом списке один: сам бой различает
/// хоста и клиента не фазой, а ролью.
public enum NetPhase { Idle, Connecting, Lobby, Match, Ended }

/// Один участник комнаты.
public class NetPlayer
{
    public int Peer;
    public string Name;

    /// Какой командой правит, -1 — ещё не роздано.
    public int Team = -1;

    public bool Ready;
    public int Ping = -1;

    /// Связь потеряна, но место держим: игрок мог свернуть приложение в
    /// метро. Его команду на это время ведёт бот.
    public bool Absent;
}

/// Сетевой матч целиком: комната, раздача команд, поток ввода и сверка
/// состояния. Живёт отдельным объектом, не умирающим между сценами, потому
/// что комната переживает и меню, и конец боя.
///
/// Роли неравны: хост считает бой по-настоящему, клиенты показывают его и
/// шлют свой ввод. Это тот самый вариант «C» из разбора — поток ввода ради
/// живой картинки плюс сверка в конце каждого хода, которая не даёт
/// расхождению копиться. Пошаговость игры здесь работает на нас: раз в
/// пятнадцать секунд состояние гарантированно сходится к хостовому.
public class NetGame : MonoBehaviour
{
    public static NetGame I { get; private set; }

    /// Порт по умолчанию. Ничей из известных, лежит в динамическом диапазоне.
    public const int DefaultPort = 47021;

    INetTransport _tr;
    NetMatch _match;

    public NetRole Role { get; private set; } = NetRole.Offline;
    public NetPhase Phase { get; private set; } = NetPhase.Idle;

    /// Все участники, включая себя. У клиента список приходит от хоста.
    public readonly List<NetPlayer> Players = new List<NetPlayer>();

    /// Последняя внятная причина, по которой всё кончилось: показывается
    /// в меню вместо молчаливого возврата на главный экран.
    public string LastError { get; private set; }

    /// Номер этого устройства среди участников. У хоста 0, у гостя тот, что
    /// выдал хост сообщением Welcome: собственный номер транспорт знает не
    /// всегда — у ретранслятора его не знает никто, кроме хоста.
    public int LocalPeer => _assignedPeer >= 0 ? _assignedPeer : (_tr != null ? _tr.LocalPeer : -1);

    int _assignedPeer = -1;

    /// Адрес, который хост диктует соседям по комнате в локальной сети.
    public string HostAddress { get; private set; }
    public int HostPort { get; private set; }

    /// Код комнаты у ретранслятора. Пусто — играем по адресу.
    public string RoomCode { get; private set; }

    /// Сколько гостей помещается в комнату сверх хоста.
    public const int MaxGuests = 3;

    public bool IsHost => Role == NetRole.Host;
    public bool Online => Role != NetRole.Offline;
    public INetTransport Transport => _tr;
    public NetMatch Match => _match;

    /// Конфигурация, которую хост правит в лобби и рассылает на старте.
    public MatchConfig Config = MatchConfig.Hotseat();

    /// Сид мира. Приходит от хоста, чтобы карта у всех получилась одна.
    public int Seed;

    public event Action LobbyChanged;
    public event Action<string> Failed;

    // --- поднять и погасить ----------------------------------------------

    static NetGame Ensure()
    {
        if (I != null) return I;
        var go = new GameObject("NetGame");
        DontDestroyOnLoad(go);
        I = go.AddComponent<NetGame>();
        return I;
    }

    /// Поднять комнату. Возвращает адрес, который диктуют соседу.
    public static NetGame StartHost(string playerName, int port = DefaultPort)
    {
        var net = Ensure();
        net.Shutdown(null);
        try
        {
            var udp = UdpTransport.Host(port);
            udp.MaxPeers = MaxGuests;
            net.Attach(udp, NetRole.Host);
            net.HostAddress = UdpTransport.LocalAddress();
            net.HostPort = udp.Port;
            net.Players.Add(new NetPlayer { Peer = 0, Name = Named(playerName), Ready = true });
            net.Phase = NetPhase.Lobby;
            net.LobbyChanged?.Invoke();
        }
        catch (Exception e)
        {
            net.Fail("Не удалось занять порт " + port + ": " + e.Message);
        }
        return net;
    }

    /// Войти в комнату по адресу хоста.
    public static NetGame JoinHost(string playerName, string address, int port = DefaultPort)
    {
        var net = Ensure();
        net.Shutdown(null);
        try
        {
            var udp = UdpTransport.Join(address, port);
            net.Attach(udp, NetRole.Client);
            net.HostAddress = address;
            net.HostPort = port;
            net._myName = Named(playerName);
            net.Phase = NetPhase.Connecting;
        }
        catch (Exception e)
        {
            net.Fail("Не разобрал адрес: " + e.Message);
        }
        return net;
    }

    /// Поднять комнату через ретранслятор Unity: соседям диктуется код, а не
    /// адрес, и сидеть с ними в одной сети больше не нужно.
    ///
    /// Ответ приходит не сразу — сервисы поднимаются, ячейка выделяется, код
    /// запрашивается, — поэтому метод возвращает объект в фазе Connecting, а
    /// экран комнаты дожидается кода сам.
    public static NetGame StartHostOnline(string playerName)
    {
        var net = Ensure();
        net.Shutdown(null);
        net._myName = Named(playerName);
        net.Phase = NetPhase.Connecting;
        net.RoomCode = null;
        net.OpenRoom();
        return net;
    }

    async void OpenRoom()
    {
        try
        {
            var (server, code) = await RelaySession.Allocate(MaxGuests);
            if (this == null) return;                 // ушли из меню, пока ждали

            Attach(RelayTransport.Host(server, code), NetRole.Host);
            RoomCode = code;
            Players.Add(new NetPlayer { Peer = 0, Name = _myName, Ready = true });
            Phase = NetPhase.Lobby;
            LobbyChanged?.Invoke();
        }
        catch (Exception e)
        {
            Fail(RelaySession.Explain(e));
        }
    }

    /// Войти в чужую комнату по коду.
    public static NetGame JoinOnline(string playerName, string code)
    {
        var net = Ensure();
        net.Shutdown(null);
        net._myName = Named(playerName);
        net.Phase = NetPhase.Connecting;
        net.RoomCode = RelaySession.Normalize(code);
        net.EnterRoom(net.RoomCode);
        return net;
    }

    async void EnterRoom(string code)
    {
        try
        {
            var server = await RelaySession.Join(code);
            if (this == null) return;

            Attach(RelayTransport.Join(server, code), NetRole.Client);
            RoomCode = code;
            Phase = NetPhase.Connecting;   // ждём Connect от транспорта, потом Hello
        }
        catch (Exception e)
        {
            Fail(RelaySession.Explain(e));
        }
    }

    /// Комната внутри одного процесса — этим живут редакторские тесты.
    public static NetGame StartLoopHost(string room, string playerName)
    {
        var net = Ensure();
        net.Shutdown(null);
        net.Attach(LoopTransport.Host(room), NetRole.Host);
        net.Players.Add(new NetPlayer { Peer = 0, Name = Named(playerName), Ready = true });
        net.Phase = NetPhase.Lobby;
        net.LobbyChanged?.Invoke();
        return net;
    }

    /// Клиент отдельным объектом, мимо singleton. Нужен тесту: хост и клиент
    /// поднимаются в одном процессе, и второй из них не должен занимать
    /// NetGame.I — иначе они отбирали бы друг у друга и место, и матч.
    public static NetGame CreateStandaloneClient(string playerName, string address, int port = DefaultPort)
    {
        var go = new GameObject("NetGame (клиент)");
        var net = go.AddComponent<NetGame>();
        net._standalone = true;
        net._myName = Named(playerName);
        try
        {
            net.Attach(UdpTransport.Join(address, port), NetRole.Client);
            net.HostAddress = address;
            net.HostPort = port;
            net.Phase = NetPhase.Connecting;
        }
        catch (Exception e)
        {
            net.Fail("Не разобрал адрес: " + e.Message);
        }
        return net;
    }

    /// Второе устройство того же процесса. Отдельный объект: два NetGame в
    /// одной сцене не уживаются, поэтому тест держит клиента вручную.
    public static NetGame CreateLoopClient(string room, string playerName)
    {
        var go = new GameObject("NetGame (клиент)");
        var net = go.AddComponent<NetGame>();
        net.Attach(LoopTransport.Join(room), NetRole.Client);
        net._myName = Named(playerName);
        net._standalone = true;
        net.Phase = NetPhase.Connecting;
        net.SendHello();
        return net;
    }

    static string Named(string s) => string.IsNullOrWhiteSpace(s) ? "Игрок" : s.Trim();

    bool _standalone;   // объект теста, не singleton
    string _myName = "Игрок";

    void Attach(INetTransport tr, NetRole role)
    {
        _tr = tr;
        Role = role;
        LastError = null;
        _helloSent = false;
        _assignedPeer = role == NetRole.Host ? 0 : -1;
        Players.Clear();
        _tr.PeerJoined += OnPeerJoined;
        _tr.PeerLeft += OnPeerLeft;
        _tr.Received += OnReceived;
        _match = new NetMatch(this);
    }

    /// Уйти из комнаты. Причина, если она есть, остаётся в LastError, чтобы
    /// меню объяснило игроку, что случилось.
    public void Shutdown(string reason)
    {
        if (_tr != null)
        {
            _tr.PeerJoined -= OnPeerJoined;
            _tr.PeerLeft -= OnPeerLeft;
            _tr.Received -= OnReceived;
            _tr.Dispose();
            _tr = null;
        }
        _match = null;
        // Одиночный матч после сетевого обязан снова считать бой сам, а
        // ландшафт — перестать докладывать о воронках в никуда.
        NetSim.Reset();
        DestructibleTerrain.OpSink = null;
        Role = NetRole.Offline;
        RoomCode = null;
        _assignedPeer = -1;
        Phase = reason == null ? NetPhase.Idle : NetPhase.Ended;
        LastError = reason;
        Players.Clear();
    }

    void Fail(string why)
    {
        Shutdown(why);
        Failed?.Invoke(why);
    }

    void OnDestroy()
    {
        Shutdown(null);
        if (I == this) I = null;
    }

    // --- цикл -------------------------------------------------------------

    void Update()
    {
        if (_tr == null) return;
        _tr.Tick();

        if (Role == NetRole.Client && Phase == NetPhase.Connecting && _tr.Connected && !_helloSent)
            SendHello();

        for (int i = 0; i < Players.Count; i++)
            Players[i].Ping = _tr.PingTo(Players[i].Peer);

        if (Phase == NetPhase.Match) _match?.Tick();
    }

    bool _helloSent;

    void SendHello()
    {
        SendHello(_ready);
        _helloSent = true;
    }

    bool _ready;

    /// Hello служит и представлением, и отметкой готовности: повторный
    /// Hello от знакомого участника просто обновляет флаг. Отдельное
    /// сообщение ради одного бита не окупается.
    void SendHello(bool ready)
    {
        var w = new NetWriter(64);
        w.U8((byte)NetMsg.Hello);
        w.U16(NetVersion.Current);
        w.Str(_myName);
        w.Bool(ready);
        SendTo(0, ref w, NetChannel.Reliable);
    }

    // --- отправка ---------------------------------------------------------

    public void SendTo(int peer, ref NetWriter w, NetChannel ch)
    {
        _tr?.Send(peer, w.Buffer, w.Length, ch);
    }

    public void SendAll(ref NetWriter w, NetChannel ch, int except = -1)
    {
        _tr?.Broadcast(w.Buffer, w.Length, ch, except);
    }

    // --- участники --------------------------------------------------------

    void OnPeerJoined(int peer)
    {
        // У хоста настоящий вход участника — это Hello с именем, а не сам
        // факт пакета: до имени показывать в комнате нечего.
        if (Role == NetRole.Client && peer == 0) Phase = NetPhase.Connecting;
    }

    void OnPeerLeft(int peer)
    {
        if (Role == NetRole.Client)
        {
            Fail(TransportReject() ?? "Связь с хостом потеряна");
            return;
        }

        var p = Find(peer);
        if (p == null) return;

        if (Phase == NetPhase.Match)
        {
            // Матч не рушим: место держим, команду до конца боя ведёт бот.
            p.Absent = true;
            _match?.HandOverToBot(p.Team);
            BroadcastLobby();
        }
        else
        {
            Players.Remove(p);
            BroadcastLobby();
        }
        LobbyChanged?.Invoke();
    }

    string TransportReject() => _tr is UdpTransport udp ? udp.RejectReason : null;

    public NetPlayer Find(int peer)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].Peer == peer) return Players[i];
        return null;
    }

    /// Команда, которой правит это устройство. -1 — только смотрим.
    public int LocalTeam
    {
        get
        {
            var me = Find(LocalPeer);
            return me != null ? me.Team : -1;
        }
    }

    /// Кому принадлежит команда: номер участника или -1, если она под ботом.
    public int OwnerOf(int team)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].Team == team && !Players[i].Absent) return Players[i].Peer;
        return -1;
    }

    // --- приём ------------------------------------------------------------

    void OnReceived(int peer, byte[] data, int length)
    {
        var r = new NetReader(data, 0, length);
        var msg = (NetMsg)r.U8();

        switch (msg)
        {
            case NetMsg.Hello: OnHello(peer, ref r); break;
            case NetMsg.Welcome: OnWelcome(ref r); break;
            case NetMsg.Reject: OnReject(ref r); break;
            case NetMsg.Lobby: OnLobby(ref r); break;
            case NetMsg.Start: OnStart(ref r); break;
            default:
                _match?.Receive(peer, msg, ref r);
                break;
        }
    }

    void OnHello(int peer, ref NetReader r)
    {
        if (Role != NetRole.Host) return;

        ushort version = r.U16();
        string name = r.Str();
        bool ready = r.Bool();
        if (version != NetVersion.Current)
        {
            Reject(peer, $"Версии игры разные: у вас {version}, у хоста {NetVersion.Current}");
            return;
        }

        var known = Find(peer);
        if (known != null)
        {
            known.Ready = ready;
            BroadcastLobby();
            LobbyChanged?.Invoke();
            return;
        }

        if (Phase != NetPhase.Lobby)
        {
            // Матч уже идёт. Пускаем только того, кто уходил и вернулся:
            // по имени, потому что номер участника после переподключения
            // другой.
            var back = FindAbsent(name);
            if (back == null) return;
            back.Peer = peer;
            back.Absent = false;
            Welcome(peer);
            BroadcastLobby();
            _match?.SendFullState(peer);
            LobbyChanged?.Invoke();
            return;
        }

        if (Players.Count > MaxGuests)
        {
            Reject(peer, "В комнате нет свободных мест");
            return;
        }

        Players.Add(new NetPlayer { Peer = peer, Name = UniqueName(name), Ready = ready });
        Welcome(peer);
        BroadcastLobby();
        LobbyChanged?.Invoke();
    }

    /// Хост называет гостю его номер. Через ретранслятор это единственный
    /// способ его узнать: транспорт там раздаёт соединения, а не участников.
    void Welcome(int peer)
    {
        var w = new NetWriter(16);
        w.U8((byte)NetMsg.Welcome);
        w.U16((ushort)peer);
        SendTo(peer, ref w, NetChannel.Reliable);
    }

    void OnWelcome(ref NetReader r)
    {
        if (Role != NetRole.Client) return;
        int id = r.U16();
        if (!r.Ok) return;
        _assignedPeer = id;
        if (Phase == NetPhase.Connecting) Phase = NetPhase.Lobby;
        LobbyChanged?.Invoke();
    }

    void Reject(int peer, string why)
    {
        var w = new NetWriter(128);
        w.U8((byte)NetMsg.Reject);
        w.Str(why);
        SendTo(peer, ref w, NetChannel.Reliable);
    }

    void OnReject(ref NetReader r)
    {
        if (Role != NetRole.Client) return;
        string why = r.Str();
        Fail(string.IsNullOrEmpty(why) ? "Хост не пустил в комнату" : why);
    }

    NetPlayer FindAbsent(string name)
    {
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].Absent && Players[i].Name == name) return Players[i];
        return null;
    }

    /// Два «Игрока» в списке комнаты неразличимы, а по имени мы ещё и
    /// узнаём вернувшегося. Поэтому тёзка получает номер.
    string UniqueName(string name)
    {
        bool taken = false;
        for (int i = 0; i < Players.Count; i++)
            if (Players[i].Name == name) taken = true;
        if (!taken) return name;

        for (int n = 2; n < 32; n++)
        {
            string candidate = name + " " + n;
            bool busy = false;
            for (int i = 0; i < Players.Count; i++)
                if (Players[i].Name == candidate) busy = true;
            if (!busy) return candidate;
        }
        return name;
    }

    public void BroadcastLobby()
    {
        if (Role != NetRole.Host) return;
        var w = new NetWriter(256);
        w.U8((byte)NetMsg.Lobby);
        w.U8((byte)Players.Count);
        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            w.U16((ushort)p.Peer);
            w.Str(p.Name);
            w.I8((sbyte)p.Team);
            w.Bool(p.Ready);
            w.Bool(p.Absent);
        }
        SendAll(ref w, NetChannel.Reliable);
    }

    void OnLobby(ref NetReader r)
    {
        if (Role != NetRole.Client) return;
        int n = r.U8();
        Players.Clear();
        for (int i = 0; i < n; i++)
        {
            var p = new NetPlayer
            {
                Peer = r.U16(),
                Name = r.Str(),
                Team = r.I8(),
                Ready = r.Bool(),
                Absent = r.Bool()
            };
            Players.Add(p);
        }
        if (!r.Ok) return;
        if (Phase == NetPhase.Connecting) Phase = NetPhase.Lobby;
        LobbyChanged?.Invoke();
    }

    /// Готовность — единственное, что клиент решает сам в лобби.
    public void SetReady(bool ready)
    {
        if (Role == NetRole.Host)
        {
            var me = Find(0);
            if (me != null) me.Ready = ready;
            BroadcastLobby();
            LobbyChanged?.Invoke();
            return;
        }
        _ready = ready;
        SendHello(ready);

        var me2 = Find(LocalPeer);
        if (me2 != null) me2.Ready = ready;
        LobbyChanged?.Invoke();
    }

    public bool AllReady
    {
        get
        {
            if (Players.Count < 2) return false;
            for (int i = 0; i < Players.Count; i++)
                if (!Players[i].Ready) return false;
            return true;
        }
    }

    // --- старт матча -------------------------------------------------------

    /// Хост раздаёт команды и рассылает старт. Живых игроков сажаем по
    /// командам сверху вниз, остаток добираем ботами — так комната на двоих
    /// в матче на четверых превращается в двоих против двух ботов, а не в
    /// матч с пустыми командами.
    public void HostStartMatch(MatchConfig cfg)
    {
        if (Role != NetRole.Host) return;

        Config = cfg ?? MatchConfig.Hotseat();
        if (Config.TeamCount < Players.Count) Config.SetTeamCount(Players.Count);

        for (int i = 0; i < Players.Count && i < Config.TeamCount; i++)
        {
            Players[i].Team = i;
            Config.Teams[i].IsBot = false;
        }
        for (int t = Players.Count; t < Config.TeamCount; t++)
            Config.Teams[t].IsBot = true;

        Seed = UnityEngine.Random.Range(1, 100000);

        var w = new NetWriter(512);
        w.U8((byte)NetMsg.Start);
        w.I32(Seed);
        MatchConfigCodec.Write(ref w, Config);
        w.U8((byte)Players.Count);
        for (int i = 0; i < Players.Count; i++)
        {
            w.U16((ushort)Players[i].Peer);
            w.I8((sbyte)Players[i].Team);
        }
        SendAll(ref w, NetChannel.Reliable);

        BeginMatchLocally();
    }

    void OnStart(ref NetReader r)
    {
        if (Role != NetRole.Client) return;
        Seed = r.I32();
        Config = MatchConfigCodec.Read(ref r);
        int n = r.U8();
        for (int i = 0; i < n; i++)
        {
            int peer = r.U16();
            int team = r.I8();
            var p = Find(peer);
            if (p != null) p.Team = team;
        }
        if (!r.Ok) { Fail("Хост прислал непонятный старт матча"); return; }
        BeginMatchLocally();
    }

    void BeginMatchLocally()
    {
        Phase = NetPhase.Match;
        Config.Seed = Seed;
        if (App.I != null) App.I.StartMatch(Config);
        _match?.OnMatchBegan();
    }
}
