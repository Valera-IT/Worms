using UnityEngine;
using UnityEngine.UIElements;

/// Экраны сетевой игры: создать комнату, войти по адресу, комната перед боем.
/// Отдельным файлом от остальных экранов, потому что живут они иначе —
/// перерисовываются сами, пока состав комнаты меняется, а не один раз при
/// открытии.
public partial class MenuUI
{
    const string PrefName = "net.name";
    const string PrefAddress = "net.address";
    const string PrefCode = "net.code";

    static string PlayerName
    {
        get => PlayerPrefs.GetString(PrefName, "Игрок");
        set => PlayerPrefs.SetString(PrefName, value);
    }

    static string LastAddress
    {
        get => PlayerPrefs.GetString(PrefAddress, "");
        set => PlayerPrefs.SetString(PrefAddress, value);
    }

    static string LastCode
    {
        get => PlayerPrefs.GetString(PrefCode, "");
        set => PlayerPrefs.SetString(PrefCode, value);
    }

    public void ShowNetHome() => Set(BuildNetHome);

    void BuildNetHome()
    {
        _root.Add(MenuTheme.Screen());
        var (layer, body) = MenuTheme.Sheet("Сетевая игра", 760f);

        body.Add(MenuTheme.Field("Имя", PlayerName, v => PlayerName = v, 20));

        // Через интернет — первым: код комнаты работает и там, где адрес
        // бесполезен, то есть почти везде за пределами домашнего Wi-Fi.
        body.Add(MenuTheme.Button("Создать комнату — по коду", () =>
        {
            NetGame.StartHostOnline(PlayerName);
            Set(BuildNetLobby);
        }));
        body.Add(MenuTheme.Button("Войти по коду", () => Set(BuildNetCode)));
        body.Add(MenuTheme.Note("Код комнаты выдаёт ретранслятор Unity: играть можно через " +
                                "интернет, из разных сетей и городов."));

        var lan = MenuTheme.Text("В одной сети, без интернета", 16, MenuTheme.InkDim);
        lan.style.marginTop = 18;
        body.Add(lan);

        body.Add(MenuTheme.Button("Создать комнату — по адресу", () =>
        {
            var net = NetGame.StartHost(PlayerName);
            if (net.Role != NetRole.Host) { Set(BuildNetHome); return; }
            Set(BuildNetLobby);
        }));
        body.Add(MenuTheme.Button("Войти по адресу", () => Set(BuildNetJoin)));

        var back = MenuTheme.Button("Назад", () => Set(BuildModeSelect));
        back.style.marginTop = 18;
        body.Add(back);

        var err = NetGame.I != null ? NetGame.I.LastError : null;
        if (!string.IsNullOrEmpty(err))
        {
            var note = MenuTheme.Note("Прошлая попытка: " + err);
            note.style.color = new Color(1f, 0.55f, 0.45f);
            body.Add(note);
        }

        _root.Add(layer);
    }

    void BuildNetCode()
    {
        _root.Add(MenuTheme.Screen());
        var (layer, body) = MenuTheme.Sheet("Войти по коду", 760f);

        string code = LastCode;
        body.Add(MenuTheme.Field("Имя", PlayerName, v => PlayerName = v, 20));
        body.Add(MenuTheme.Field("Код комнаты", code, v => code = v, 12));
        body.Add(MenuTheme.Note("Код диктует тот, кто создал комнату: шесть знаков. " +
                                "Регистр и пробелы значения не имеют."));

        body.Add(MenuTheme.Button("Войти", () =>
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            LastCode = code;
            NetGame.JoinOnline(PlayerName, code);
            Set(BuildNetLobby);
        }));

        var back = MenuTheme.Button("Назад", () => Set(BuildNetHome));
        back.style.marginTop = 18;
        body.Add(back);
        _root.Add(layer);
    }

    void BuildNetJoin()
    {
        _root.Add(MenuTheme.Screen());
        var (layer, body) = MenuTheme.Sheet("Войти в комнату", 760f);

        string address = LastAddress;
        body.Add(MenuTheme.Field("Имя", PlayerName, v => PlayerName = v, 20));
        body.Add(MenuTheme.Field("Адрес", address, v => address = v, 40));
        body.Add(MenuTheme.Note("Адрес диктует тот, кто создал комнату: он написан " +
                                "у него на экране. Порт по умолчанию дописывать не нужно."));

        body.Add(MenuTheme.Button("Войти", () =>
        {
            var (host, port) = SplitAddress(address);
            if (string.IsNullOrWhiteSpace(host)) return;
            LastAddress = address;
            NetGame.JoinHost(PlayerName, host, port);
            Set(BuildNetLobby);
        }));

        var back = MenuTheme.Button("Назад", () => Set(BuildNetHome));
        back.style.marginTop = 18;
        body.Add(back);
        _root.Add(layer);
    }

    /// «192.168.1.5» или «192.168.1.5:47021» — второе на случай, когда порт
    /// пришлось менять руками.
    static (string host, int port) SplitAddress(string s)
    {
        s = (s ?? string.Empty).Trim();
        int colon = s.LastIndexOf(':');
        if (colon > 0 && int.TryParse(s.Substring(colon + 1), out int port))
            return (s.Substring(0, colon), port);
        return (s, NetGame.DefaultPort);
    }

    void BuildNetLobby()
    {
        _root.Add(MenuTheme.Screen());
        var net = NetGame.I;
        var (layer, body) = MenuTheme.Sheet("Комната", 820f);

        // Комната через ретранслятор поднимается не мгновенно: пока идёт
        // выделение ячейки, транспорта ещё нет, но экран уже открыт.
        if (net == null || (!net.Online && net.Phase != NetPhase.Connecting))
        {
            body.Add(MenuTheme.Note(string.IsNullOrEmpty(net != null ? net.LastError : null)
                ? "Комнаты нет."
                : net.LastError));
            body.Add(MenuTheme.Button("Назад", () => Set(BuildNetHome)));
            _root.Add(layer);
            return;
        }

        // Крупная строка сверху — то, что диктуют соседу: код у комнаты через
        // ретранслятор, адрес у комнаты в локальной сети.
        var headline = MenuTheme.Text("", 38, MenuTheme.Hot, FontStyle.Bold);
        headline.style.unityTextAlign = TextAnchor.MiddleCenter;
        headline.style.letterSpacing = 4;
        headline.style.marginBottom = 6;
        var headnote = MenuTheme.Note("");
        if (net.IsHost)
        {
            body.Add(headline);
            body.Add(headnote);
        }

        var status = MenuTheme.Note("");
        body.Add(status);

        // Комната через ретранслятор доезжает уже после того, как экран
        // собран: до ответа сервиса неизвестно даже, хост мы или гость.
        // Поэтому экран пересобирается, когда роль наконец выяснилась.
        bool builtAsHost = net.IsHost;

        var list = new VisualElement();
        list.style.marginTop = 10;
        list.style.marginBottom = 10;
        body.Add(list);

        var ready = MenuTheme.Button("Я готов", () =>
        {
            if (!net.Online) return;
            var me = net.Find(net.LocalPeer);
            net.SetReady(me == null || !me.Ready);
        });
        body.Add(ready);

        Button start = null;
        if (net.IsHost)
        {
            start = MenuTheme.Button("Начать бой", () =>
            {
                App.I.Draft = App.I.Draft ?? MatchConfig.Setup();
                net.HostStartMatch(App.I.Draft);
            });
            body.Add(start);

            // Настройка прямо отсюда: комната живёт в NetGame, а не на этом
            // экране, поэтому хост может уйти её править и вернуться — комната
            // за это время не закроется и гостей не потеряет.
            body.Add(MenuTheme.Button("Настройка матча", () => ShowMatchSetup(BuildNetLobby, true)));
            body.Add(MenuTheme.Note("Карта, число червей и время хода берутся из «Настройки матча»; " +
                                    "поправить их можно не выходя из комнаты. " +
                                    "Команды раздаются по числу вошедших, остальные достаются ботам."));
        }

        var leave = MenuTheme.Button("Выйти из комнаты", () =>
        {
            net.Shutdown(null);
            Set(BuildNetHome);
        });
        leave.style.marginTop = 18;
        body.Add(leave);

        // Комната живая: игроки входят, отмечаются готовыми и уходят, пока
        // экран открыт. Перерисовываем список по расписанию — оно само
        // остановится, когда элемент уйдёт с экрана.
        layer.schedule.Execute(() =>
        {
            if (NetGame.I == null || (!NetGame.I.Online && NetGame.I.Phase != NetPhase.Connecting))
            {
                Set(BuildNetHome);
                return;
            }

            if (NetGame.I.IsHost != builtAsHost)
            {
                Set(BuildNetLobby);
                return;
            }

            if (NetGame.I.IsHost)
            {
                bool online = !string.IsNullOrEmpty(NetGame.I.RoomCode);
                headline.text = online
                    ? NetGame.I.RoomCode
                    : NetGame.I.HostAddress + ":" + NetGame.I.HostPort;
                headnote.text = online
                    ? "Продиктуйте этот код тем, кто играет с вами: он работает через интернет."
                    : "Это адрес в локальной сети: устройства должны быть в одной сети.";
            }

            status.text = NetGame.I.Phase == NetPhase.Connecting
                ? (NetGame.I.IsHost ? "Занимаем комнату у ретранслятора…" : "Стучимся к хосту…")
                : "В комнате " + NetGame.I.Players.Count + " из " + (NetGame.MaxGuests + 1);

            list.Clear();
            for (int i = 0; i < NetGame.I.Players.Count; i++)
            {
                var p = NetGame.I.Players[i];
                var row = MenuTheme.Row();
                row.style.justifyContent = Justify.SpaceBetween;
                row.style.height = 44;

                string mark = p.Absent ? "  (нет связи)" : p.Ready ? "  ✓" : "";
                string who = p.Peer == NetGame.I.LocalPeer ? p.Name + "  (вы)" : p.Name;
                row.Add(MenuTheme.Text(who + mark, 22, p.Ready ? MenuTheme.Accent : MenuTheme.Ink, FontStyle.Bold));

                string ping = p.Peer == 0 ? "хост" : p.Ping >= 0 ? p.Ping + " мс" : "…";
                row.Add(MenuTheme.Text(ping, 18, MenuTheme.InkDim));
                list.Add(row);
            }

            var me = NetGame.I.Find(NetGame.I.LocalPeer);
            ready.text = me != null && me.Ready ? "Не готов" : "Я готов";

            if (start != null)
            {
                bool can = NetGame.I.AllReady;
                start.SetEnabled(can);
                start.style.color = can ? MenuTheme.Accent : MenuTheme.InkDim;
                start.text = can ? "Начать бой" : "Ждём готовности";
            }
        }).Every(300);

        _root.Add(layer);
    }
}
