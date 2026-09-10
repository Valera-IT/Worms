using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Проверка сетевого слоя без второго компьютера: хост и клиент поднимаются
/// в одном процессе на 127.0.0.1 и говорят по настоящему UDP — тому же, что
/// и между телефоном и ноутбуком. Проверяются укладка в байты, рукопожатие,
/// комната, надёжный канал с разрезанием длинного сообщения и уход участника.
///
/// Чего здесь нет и быть не может: второго игрового мира. GameManager в
/// процессе один, поэтому сам бой этим тестом не покрыть — за ним нужны две
/// копии игры. Зато всё, что до боя и вокруг него, ловится здесь за
/// несколько секунд. Запуск: меню «Worms → Тест сети».
public static class NetTest
{
    const string Flag = "NetTest.Running";
    const int Port = 47099;

    static double _start;
    static double _mark;
    static int _step;
    static bool _failed;

    static NetGame _host;
    static NetGame _client;
    static NetGame _relay;

    /// Сырое подключение, которое врёт про свою версию игры.
    static UdpTransport _liar;
    static bool _liarSpoke;
    static string _rejectReason;

    static void Advance(int next) { _step = next; _mark = EditorApplication.timeSinceStartup - _start; }
    static double Since => EditorApplication.timeSinceStartup - _start - _mark;

    [MenuItem("Worms/Тест сети")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _failed = false;
        _liarSpoke = false;
        _rejectReason = null;
        SessionState.SetBool(Flag, true);
        App.AutostartMatch = false;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false))
        {
            App.AutostartMatch = false;
            EditorApplication.update += Tick;
        }
    }

    static void Say(string msg) => Debug.Log("NET: " + msg);
    static void Fail(string msg) { Debug.LogError("NET: " + msg); _failed = true; }

    static bool Check(bool ok, string what)
    {
        if (ok) Say("✓ " + what);
        else Fail("✗ " + what);
        return ok;
    }

    static void Finish()
    {
        if (_client != null) Object.Destroy(_client.gameObject);
        if (_host != null) _host.Shutdown(null);
        if (_relay != null) { _relay.Shutdown(null); _relay = null; }
        if (_liar != null) { _liar.Received -= OnLiarPacket; _liar.Dispose(); _liar = null; }
        Say(_failed ? "ТЕСТ СЕТИ: ПРОВАЛ" : "ТЕСТ СЕТИ: ВСЁ ПРОШЛО");
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    // --- шаг 1: укладка в байты ------------------------------------------

    static void CodecChecks()
    {
        var w = new NetWriter(64);
        w.U8(200);
        w.I16(-1234);
        w.U32(4000000000u);
        w.F32(3.5f);
        w.Unit(-0.5f);
        w.Coord(-37.25f);
        w.Angle(270f);
        w.Str("Червяки");

        var r = new NetReader(w.Buffer, 0, w.Length);
        Check(r.U8() == 200, "байт доезжает");
        Check(r.I16() == -1234, "знаковое короткое доезжает");
        Check(r.U32() == 4000000000u, "большое беззнаковое доезжает");
        Check(Mathf.Approximately(r.F32(), 3.5f), "float доезжает");
        Check(Mathf.Abs(r.Unit() + 0.5f) < 0.01f, "ось ввода в одном байте");
        Check(Mathf.Abs(r.Coord() + 37.25f) < 0.04f, "координата в двух байтах");
        Check(Mathf.Abs(r.Angle() - 270f) < 1.5f, "угол в одном байте");
        Check(r.Str() == "Червяки", "кириллица доезжает");
        Check(r.Ok, "чтение не сорвалось");

        // Оборванный пакет обязан не бросать исключение, а честно сказать,
        // что данных не хватило: по сети приходит и мусор тоже.
        var torn = new NetReader(w.Buffer, 0, 3);
        torn.U32(); torn.U32();
        Check(!torn.Ok, "оборванный пакет не роняет чтение");

        // Настройки матча целиком.
        var cfg = MatchConfig.Hotseat();
        cfg.SetTeamCount(3);
        cfg.WormsPerTeam = 5;
        cfg.TurnTime = 22f;
        cfg.Terrain = TerrainKind.Canyon;
        var cw = new NetWriter(256);
        MatchConfigCodec.Write(ref cw, cfg);
        var cr = new NetReader(cw.Buffer, 0, cw.Length);
        var back = MatchConfigCodec.Read(ref cr);
        Check(back.TeamCount == 3 && back.WormsPerTeam == 5
              && Mathf.Approximately(back.TurnTime, 22f)
              && back.Terrain == TerrainKind.Canyon
              && back.Teams[1].Name == cfg.Teams[1].Name,
              "настройки матча доезжают целиком");

        // Кадр ввода.
        var fake = new FakeInput { Move = 0.75f, JumpPressed = true, FireHeld = true, HasAimTarget = true, AimTarget = new Vector2(12.5f, -3.25f), WeaponRequest = 7 };
        var iw = new NetWriter(32);
        NetInput.Write(ref iw, fake, 1);
        var ir = new NetReader(iw.Buffer, 0, iw.Length);
        var ni = new NetInput();
        Check(ni.Read(ref ir), "кадр ввода читается");
        Check(Mathf.Abs(ni.Move - 0.75f) < 0.02f && ni.JumpPressed && ni.FireHeld
              && ni.HasAimTarget && Vector2.Distance(ni.AimTarget, fake.AimTarget) < 0.1f
              && ni.WeaponRequest == 7,
              "кадр ввода доезжает целиком");

        // Опоздавший старый кадр не должен затирать свежий.
        var ow = new NetWriter(32);
        NetInput.Write(ref ow, new FakeInput { Move = -1f }, 0);
        var or_ = new NetReader(ow.Buffer, 0, ow.Length);
        ni.Read(ref or_);
        Check(Mathf.Abs(ni.Move - 0.75f) < 0.02f, "опоздавший кадр ввода отброшен");
    }

    class FakeInput : IGameInput
    {
        public InputScheme Scheme => InputScheme.Keyboard;
        public float Move { get; set; }
        public float AimAxis { get; set; }
        public bool HasAimTarget { get; set; }
        public Vector2 AimTarget { get; set; }
        public bool HasMark { get; set; }
        public Vector2 Mark { get; set; }
        public bool JumpPressed { get; set; }
        public bool FirePressed { get; set; }
        public bool FireHeld { get; set; }
        public bool FireReleased { get; set; }
        public int WeaponRequest { get; set; } = -1;
        public int WeaponCycle { get; set; }
        public bool SelectWormPressed { get; set; }
    }

    // --- шаги 2+: живое соединение ---------------------------------------

    static byte[] _bigSent;
    static byte[] _bigGot;

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;

        switch (_step)
        {
            case 0:
                Say("--- укладка в байты ---");
                CodecChecks();
                Say("--- соединение ---");
                _host = NetGame.StartHost("Хозяин", Port);
                if (!Check(_host != null && _host.Role == NetRole.Host, "хост поднялся на порту " + Port))
                {
                    Finish();
                    return;
                }
                _client = NetGame.CreateStandaloneClient("Гость", "127.0.0.1", Port);
                Advance(1);
                break;

            case 1:
                if (_host.Players.Count == 2 && _client.Players.Count == 2)
                {
                    Check(true, "клиент вошёл в комнату");
                    Check(_client.LocalPeer > 0, "клиенту выдан номер участника");
                    Check(_host.Find(_client.LocalPeer) != null, "хост знает гостя по номеру");
                    Check(_client.Phase == NetPhase.Lobby, "гость понял, что он в комнате");
                    Advance(2);
                }
                else if (Since > 5.0)
                {
                    Fail("клиент не вошёл в комнату за пять секунд");
                    Finish();
                }
                break;

            case 2:
                // Надёжный канал: длинное сообщение режется на куски и
                // собирается обратно. Сверка состояния мира приедет именно так.
                _bigGot = null;
                _bigSent = new byte[5000];
                for (int i = 0; i < _bigSent.Length; i++) _bigSent[i] = (byte)(i * 7);
                _client.Transport.Received += OnBig;
                _host.Transport.Send(_client.LocalPeer, _bigSent, _bigSent.Length, NetChannel.Reliable);
                Advance(3);
                break;

            case 3:
                if (_bigGot != null)
                {
                    bool same = _bigGot.Length == _bigSent.Length;
                    for (int i = 0; same && i < _bigGot.Length; i++)
                        if (_bigGot[i] != _bigSent[i]) same = false;
                    Check(same, "длинное надёжное сообщение собралось целиком");
                    _client.Transport.Received -= OnBig;
                    Advance(4);
                }
                else if (Since > 5.0)
                {
                    Fail("длинное сообщение не доехало за пять секунд");
                    Advance(4);
                }
                break;

            case 4:
                // Чужая версия игры обязана получить внятный отказ, а не
                // молча разъехаться посреди матча. Проверяем это отдельным
                // сырым подключением: транспорт свою версию сверяет на входе,
                // поэтому подключаемся честно, а врём уже в Hello.
                _liar = UdpTransport.Join("127.0.0.1", Port);
                _liar.Received += OnLiarPacket;
                _rejectReason = null;
                Advance(5);
                break;

            case 5:
                _liar.Tick();
                if (_liar.Connected && !_liarSpoke)
                {
                    var w = new NetWriter(64);
                    w.U8((byte)NetMsg.Hello);
                    w.U16(999);
                    w.Str("Из будущего");
                    w.Bool(true);
                    _liar.Send(0, w.Buffer, w.Length, NetChannel.Reliable);
                    _liarSpoke = true;
                }
                if (_rejectReason != null)
                {
                    Check(_rejectReason.Contains("Версии"), "чужая версия получила отказ: " + _rejectReason);
                    Check(_host.Players.Count == 2, "чужая версия в комнату не попала");
                    _liar.Received -= OnLiarPacket;
                    _liar.Dispose();
                    _liar = null;
                    Advance(6);
                }
                else if (Since > 4.0)
                {
                    Fail("отказ по версии не пришёл");
                    if (_liar != null) { _liar.Received -= OnLiarPacket; _liar.Dispose(); _liar = null; }
                    Advance(6);
                }
                break;

            case 6:
                _client.SetReady(true);
                Advance(7);
                break;

            case 7:
            {
                var guest = _host.Find(_client.LocalPeer);
                if (guest != null && guest.Ready)
                {
                    Check(true, "готовность гостя дошла до хоста");
                    Advance(8);
                }
                else if (Since > 3.0)
                {
                    Fail("готовность гостя не дошла");
                    Advance(8);
                }
                break;
            }

            case 8:
                Say("--- раздача команд ---");
                _host.HostStartMatch(MatchConfig.Hotseat());
                Advance(9);
                break;

            case 9:
                if (Since > 1.0)
                {
                    Check(_host.Players[0].Team == 0 && _host.Players[1].Team == 1,
                          "хост раздал команды по участникам");
                    Check(_host.Config.Teams[0].IsBot == false && _host.Config.Teams[1].IsBot == false,
                          "команды живых игроков не под ботом");
                    Check(_client.Seed == _host.Seed && _client.Seed != 0,
                          "сид мира у обоих один");
                    Check(_client.Config.TeamCount == _host.Config.TeamCount,
                          "настройки матча доехали до клиента");
                    Advance(30);
                }
                break;

            case 30:
                // Журнал разрушений: мира в тесте нет, поэтому операции
                // подаём тем же путём, каким их подаёт ландшафт, — через
                // OpSink, который хост повесил на себя, начав матч.
                Say("--- журнал ландшафта ---");
                // Прямо в хостовый NetMatch, а не через DestructibleTerrain.OpSink:
                // хост и гость здесь живут в одном процессе и делят это
                // статическое поле, и последним его выставил гость — в null.
                if (!Check(_host.Match != null, "у хоста есть сетевая часть боя"))
                {
                    Advance(10);
                    break;
                }
                _host.Match.SendTerrainOp(TerrainOp.Blast(new Vector2(3f, 4f), 1.5f));
                _host.Match.SendTerrainOp(TerrainOp.Dug(new Vector2(0f, 0f), new Vector2(2f, 1f), 0.5f));
                _host.Match.SendTerrainOp(TerrainOp.Beam(new Vector2(5f, 6f), 30f, 4f, 0.4f, new Color32(200, 100, 50, 255)));
                Advance(10);
                break;

            case 10:
                Say("--- уход участника ---");
                Object.Destroy(_client.gameObject);
                _client = null;
                Advance(11);
                break;

            case 11:
                if (_host.Players.Count == 2 && _host.Players[1].Absent)
                {
                    Check(true, "ушедший помечен отсутствующим, место за ним осталось");
                    Advance(31);
                }
                else if (Since > 8.0)
                {
                    Fail("хост не заметил ухода гостя");
                    Advance(12);
                }
                break;

            case 31:
                // Тот же игрок возвращается тем же именем: хост обязан догнать
                // ему карту всеми разрушениями с начала матча.
                Say("--- возвращение посреди боя ---");
                _events = 0;
                _client = NetGame.CreateStandaloneClient("Гость", "127.0.0.1", Port);
                _client.Transport.Received += OnEvent;
                Advance(32);
                break;

            case 32:
                if (_events >= 3)
                {
                    Check(true, "вернувшемуся приехал журнал разрушений: " + _events);
                    Check(!_host.Players[1].Absent, "вернувшийся снова в комнате");
                    _client.Transport.Received -= OnEvent;
                    Advance(12);
                }
                else if (Since > 8.0)
                {
                    Fail($"журнал разрушений не доехал: пакетов {_events} из 3");
                    _client.Transport.Received -= OnEvent;
                    Advance(12);
                }
                break;

            case 12:
                if (_client != null) Object.Destroy(_client.gameObject);
                _client = null;
                _host.Shutdown(null);
                _host = null;
                Say("--- комната через ретранслятор ---");
                Advance(13);
                break;

            case 13:
                if (RelaySession.ProjectLinked)
                {
                    // Проект привязан — можно занять настоящую ячейку и
                    // получить настоящий код.
                    _relay = NetGame.StartHostOnline("Хозяин");
                    Advance(14);
                }
                else
                {
                    // Не привязан — проверяем то единственное, что здесь
                    // вообще можно проверить: что игрок получит внятное
                    // объяснение, а не исключение из недр SDK.
                    _relay = NetGame.StartHostOnline("Хозяин");
                    Advance(15);
                }
                break;

            case 14:
                if (!string.IsNullOrEmpty(_relay.RoomCode))
                {
                    Check(_relay.RoomCode.Length >= 4, "ретранслятор выдал код комнаты: " + _relay.RoomCode);
                    Check(_relay.IsHost && _relay.Phase == NetPhase.Lobby, "комната поднялась");
                    _relay.Shutdown(null);
                    Finish();
                }
                else if (_relay.Phase == NetPhase.Ended)
                {
                    Fail("ретранслятор отказал: " + _relay.LastError);
                    Finish();
                }
                else if (Since > 25.0)
                {
                    Fail("ретранслятор не ответил за 25 секунд");
                    Finish();
                }
                break;

            case 15:
                if (_relay.Phase == NetPhase.Ended)
                {
                    Check(_relay.LastError == RelaySession.NotLinkedHint,
                          "без привязки к Unity Cloud отказ объяснён словами");
                    Say("ПРОПУЩЕНО: живой ретранслятор не проверить — проект не привязан к Unity Cloud");
                    Finish();
                }
                else if (Since > 10.0)
                {
                    Fail("отказ ретранслятора не пришёл за десять секунд");
                    Finish();
                }
                break;
        }
    }

    static void OnLiarPacket(int peer, byte[] data, int length)
    {
        if (length < 1 || data[0] != (byte)NetMsg.Reject) return;
        var r = new NetReader(data, 1, length - 1);
        _rejectReason = r.Str();
    }

    /// Считаем только Event: остальное — комната, ход и состояние.
    static int _events;

    static void OnEvent(int peer, byte[] data, int length)
    {
        if (length > 0 && data[0] == (byte)NetMsg.Event) _events++;
    }

    static void OnBig(int peer, byte[] data, int length)
    {
        if (length < 1000) return;   // служебные пакеты комнаты мимо
        _bigGot = new byte[length];
        System.Array.Copy(data, _bigGot, length);
    }
}
