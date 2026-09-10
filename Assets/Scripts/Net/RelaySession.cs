using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// Разговор с Unity Relay: вход в сервисы, выделение ячейки под комнату и код,
/// по которому в неё входят.
///
/// Отделено от RelayTransport намеренно. Транспорт занимается байтами и не
/// знает ни про какие сервисы; здесь — сеть поверх HTTP, ключи и ожидание
/// ответа. Первое проверяется без интернета, второе без него не проверяется
/// вовсе, и смешивать их в одном файле значило бы сделать непроверяемым и то,
/// и другое.
public static class RelaySession
{
    /// Через что говорить с ретранслятором. «udp» — открытый канал, «dtls» —
    /// шифрованный. По проводу здесь идут нажатия и позиции червей, а не
    /// пароли, поэтому берётся дешёвый: шифрование стоит рукопожатия на входе
    /// и лишних байт в каждом пакете. Строка одна и меняется одним словом.
    const string Channel = RelayServerEndpoint.ConnectionTypeUdp;

    /// Сервисы поднимаются один раз на запуск.
    static bool _ready;
    static Task _readyTask;

    /// Проект должен быть привязан к Unity Cloud: без этого сервисы не
    /// поднимаются вовсе, и внятно сказать об этом лучше здесь, чем
    /// исключением из недр SDK.
    public static bool ProjectLinked => !string.IsNullOrEmpty(Application.cloudProjectId);

    public const string NotLinkedHint =
        "Проект не привязан к Unity Cloud, поэтому ретранслятор недоступен. " +
        "Это делается один раз: Project Settings → Services → Link project. " +
        "Игра по адресу в локальной сети работает и без этого.";

    /// Войти в сервисы и представиться анонимно. Учётной записи игрок не
    /// заводит: имя в комнате — своё, а Unity нужен только идентификатор,
    /// чтобы выдать ячейку.
    public static async Task Ready()
    {
        if (_ready) return;
        if (!ProjectLinked) throw new InvalidOperationException(NotLinkedHint);

        if (_readyTask == null)
        {
            _readyTask = Startup();
        }
        await _readyTask;
        _ready = true;
    }

    static async Task Startup()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    /// Хост: занять ячейку под комнату и получить код. maxPeers — сколько
    /// гостей поместится, себя считать не надо.
    public static async Task<(RelayServerData server, string code)> Allocate(int maxPeers)
    {
        await Ready();

        var allocation = await RelayService.Instance.CreateAllocationAsync(maxPeers);
        string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        var end = Pick(allocation.ServerEndpoints);
        var server = new RelayServerData(
            end.Host, (ushort)end.Port,
            allocation.AllocationIdBytes,
            allocation.ConnectionData,
            allocation.ConnectionData,   // хосту собственные данные годятся за «данные хоста»
            allocation.Key,
            end.Secure);

        return (server, code);
    }

    /// Гость: войти в чужую ячейку по коду.
    public static async Task<RelayServerData> Join(string code)
    {
        await Ready();

        var join = await RelayService.Instance.JoinAllocationAsync(Normalize(code));

        var end = Pick(join.ServerEndpoints);
        return new RelayServerData(
            end.Host, (ushort)end.Port,
            join.AllocationIdBytes,
            join.ConnectionData,
            join.HostConnectionData,
            join.Key,
            end.Secure);
    }

    /// Код диктуют вслух, поэтому пробелы и регистр прощаем.
    public static string Normalize(string code) =>
        (code ?? string.Empty).Trim().Replace(" ", string.Empty).ToUpperInvariant();

    static RelayServerEndpoint Pick(List<RelayServerEndpoint> endpoints)
    {
        if (endpoints == null || endpoints.Count == 0)
            throw new Exception("Ретранслятор не назвал ни одной точки входа");

        for (int i = 0; i < endpoints.Count; i++)
            if (endpoints[i].ConnectionType == Channel) return endpoints[i];

        // Нужного канала нет — берём первый, чем совсем ничего.
        return endpoints[0];
    }

    /// Причина отказа человеческими словами. SDK кидает своё исключение на
    /// каждый случай, а игроку нужно знать одно: это он ошибся кодом или
    /// что-то не так с сетью.
    public static string Explain(Exception e)
    {
        while (e is AggregateException agg && agg.InnerExceptions.Count > 0) e = agg.InnerExceptions[0];

        if (e is InvalidOperationException) return e.Message;   // проект не привязан

        if (e is RelayServiceException relay)
        {
            switch (relay.Reason)
            {
                case RelayExceptionReason.JoinCodeNotFound:
                    return "Комната с таким кодом не найдена — проверьте код";
                case RelayExceptionReason.AllocationNotFound:
                    return "Комната закрылась";
                case RelayExceptionReason.NoSuitableRelay:
                    return "Ретранслятор не нашёл подходящего сервера — попробуйте ещё раз";
                case RelayExceptionReason.NetworkError:
                    return "Сеть недоступна: ретранслятору нужен интернет";
                default:
                    return "Ретранслятор отказал: " + relay.Message;
            }
        }

        if (e is ServicesInitializationException)
            return "Не удалось поднять сервисы Unity: " + e.Message;

        return e.Message;
    }
}
