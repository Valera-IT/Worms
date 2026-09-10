using System;
using UnityEngine;

/// Что летит по проводу. Игра пошаговая, поэтому сообщений мало и все они
/// короткие: поток ввода активного игрока (вариант A) и сверка состояния в
/// конце хода (вариант B) — вместе это и есть гибрид из плана.
public enum NetMsg : byte
{
    /// Клиент → хост, первое сообщение: версия протокола и имя игрока.
    Hello = 1,

    /// Хост → клиент: принят, вот твой номер места в комнате.
    Welcome,

    /// Хост → всем: состав комнаты (кто где сидит, кто готов).
    Lobby,

    /// Хост → всем: матч начинается, вот конфигурация и сид мира.
    /// Мир строится из сида у каждого сам — карту по проводу не гоняем.
    Start,

    /// Клиент → хост: снимок ввода за кадр. Самое частое сообщение.
    Input,

    /// Хост → всем: чей ход, каким червём, ветер, время. Точка синхронизации.
    Turn,

    /// Хост → всем: полное состояние мира. Раз в ход и при переподключении.
    Snapshot,

    /// Хост → всем: то, что нельзя вывести из состояния — выстрел, взрыв,
    /// звук, всплывающее число урона.
    Event,

    /// В обе стороны: держим соединение живым и меряем задержку.
    Ping,

    /// Уходящий предупреждает, что уходит; иначе ждём таймаута.
    Bye,

    /// Хост → клиент: не пущу, и вот почему. Отдельным сообщением поверх
    /// транспорта, а не средствами самого транспорта: у ретранслятора такого
    /// средства нет вовсе, а причина отказа игроку нужна одинаково, каким бы
    /// путём он ни подключался.
    Reject
}

/// Версия протокола. Поднимается при любой несовместимой правке формата:
/// клиент с другой версией отбивается на Hello с внятной причиной, а не
/// разъезжается посреди матча.
public static class NetVersion
{
    /// 2 — появились Welcome с номером участника и Reject с причиной отказа
    /// (сентябрь 2026, шаг 15b): через ретранслятор номер выдать больше
    /// некому, кроме самого хоста.
    /// 3 — в настройках команды приехал номер банка голоса (шаг 14c): без него
    /// одна и та же команда говорила бы у хоста и у клиента разными голосами.
    public const ushort Current = 3;
}

/// Запись примитивов в байты. Своя, а не BinaryWriter: нужен единый порядок
/// байт на всех платформах и упаковка углов и координат в два байта вместо
/// четырёх — трафик хода из-за этого укладывается в килобайты.
public struct NetWriter
{
    byte[] _buf;
    int _pos;

    public NetWriter(int capacity)
    {
        _buf = new byte[Mathf.Max(16, capacity)];
        _pos = 0;
    }

    public int Length => _pos;
    public byte[] Buffer => _buf;

    void Need(int n)
    {
        if (_pos + n <= _buf.Length) return;
        int cap = _buf.Length;
        while (cap < _pos + n) cap *= 2;
        Array.Resize(ref _buf, cap);
    }

    public void U8(byte v) { Need(1); _buf[_pos++] = v; }
    public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);
    public void I8(sbyte v) => U8(unchecked((byte)v));

    public void U16(ushort v)
    {
        Need(2);
        _buf[_pos++] = (byte)(v & 0xFF);
        _buf[_pos++] = (byte)(v >> 8);
    }

    public void I16(short v) => U16(unchecked((ushort)v));

    public void U32(uint v)
    {
        Need(4);
        _buf[_pos++] = (byte)(v & 0xFF);
        _buf[_pos++] = (byte)((v >> 8) & 0xFF);
        _buf[_pos++] = (byte)((v >> 16) & 0xFF);
        _buf[_pos++] = (byte)(v >> 24);
    }

    public void I32(int v) => U32(unchecked((uint)v));

    public void F32(float v) => U32(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0));

    /// Дробь -1..1 одним байтом. Этим едут оси ввода: точности хватает с
    /// запасом, а разница между четырьмя байтами и одним на потоке ввода
    /// заметна.
    public void Unit(float v) => I8((sbyte)Mathf.Clamp(Mathf.RoundToInt(v * 127f), -127, 127));

    /// Координата мира двумя байтами с шагом 1/16 юнита. Мир не шире
    /// нескольких сотен юнитов, так что диапазон ±2048 берётся с большим
    /// запасом, а полупиксельная точность глазом не ловится.
    public void Coord(float v) => I16((short)Mathf.Clamp(Mathf.RoundToInt(v * 16f), -32768, 32767));

    public void Vec(Vector2 v) { Coord(v.x); Coord(v.y); }

    /// Угол в градусах одним байтом с шагом ~1,4°.
    public void Angle(float deg) => U8((byte)(Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 360f * 255f) & 0xFF));

    public void Str(string s)
    {
        if (string.IsNullOrEmpty(s)) { U8(0); return; }
        var bytes = System.Text.Encoding.UTF8.GetBytes(s);
        int n = Mathf.Min(bytes.Length, 255);
        U8((byte)n);
        Need(n);
        Array.Copy(bytes, 0, _buf, _pos, n);
        _pos += n;
    }

    public void Bytes(byte[] src, int count)
    {
        Need(count);
        Array.Copy(src, 0, _buf, _pos, count);
        _pos += count;
    }

    public byte[] ToArray()
    {
        var outp = new byte[_pos];
        Array.Copy(_buf, outp, _pos);
        return outp;
    }
}

/// Чтение того же формата. Все методы терпимы к обрыву: за концом буфера
/// возвращаются нули, а Ok становится false. Клиент с битым или чужим
/// пакетом должен отвалиться, а не уронить игру исключением в Update.
public struct NetReader
{
    readonly byte[] _buf;
    readonly int _end;
    int _pos;

    public NetReader(byte[] buf, int offset, int length)
    {
        _buf = buf;
        _pos = offset;
        _end = offset + length;
        Ok = true;
    }

    public bool Ok { get; private set; }
    public int Remaining => Mathf.Max(0, _end - _pos);

    bool Have(int n)
    {
        if (_pos + n <= _end) return true;
        Ok = false;
        return false;
    }

    public byte U8() => Have(1) ? _buf[_pos++] : (byte)0;
    public bool Bool() => U8() != 0;
    public sbyte I8() => unchecked((sbyte)U8());

    public ushort U16()
    {
        if (!Have(2)) return 0;
        ushort v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public short I16() => unchecked((short)U16());

    public uint U32()
    {
        if (!Have(4)) return 0;
        uint v = (uint)(_buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24));
        _pos += 4;
        return v;
    }

    public int I32() => unchecked((int)U32());
    public float F32() => BitConverter.ToSingle(BitConverter.GetBytes(U32()), 0);
    public float Unit() => I8() / 127f;
    public float Coord() => I16() / 16f;
    public Vector2 Vec() { float x = Coord(); float y = Coord(); return new Vector2(x, y); }
    public float Angle() => U8() / 255f * 360f;

    public string Str()
    {
        int n = U8();
        if (n == 0 || !Have(n)) return string.Empty;
        string s = System.Text.Encoding.UTF8.GetString(_buf, _pos, n);
        _pos += n;
        return s;
    }

    public byte[] Bytes(int count)
    {
        if (!Have(count)) return Array.Empty<byte>();
        var outp = new byte[count];
        Array.Copy(_buf, _pos, outp, 0, count);
        _pos += count;
        return outp;
    }
}
