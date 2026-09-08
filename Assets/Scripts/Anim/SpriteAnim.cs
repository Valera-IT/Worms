using UnityEngine;

/// Клип: готовый набор кадров с частотой и признаком петли. Строится один раз
/// на запуск и живёт в статическом кэше — проигрыватель его только читает,
/// поэтому один клип спокойно крутят сразу два десятка червей.
///
/// Кадров два ряда, а не один: у червя тело и черты лица лежат разными
/// SpriteRenderer'ами (цвет команды красит тело и не должен красить глаза),
/// и разъехаться по номеру кадра они не имеют права. Поэтому ряды хранятся
/// вместе и берутся всегда по одному индексу.
public class SpriteAnim
{
    public readonly string Name;
    public readonly Sprite[] Body;
    public readonly Sprite[] Face;
    public readonly float Fps;
    public readonly bool Loop;

    public int Frames => Body.Length;
    public float Length => Frames / Mathf.Max(0.01f, Fps);

    public SpriteAnim(string name, Sprite[] body, Sprite[] face, float fps, bool loop)
    {
        if (body == null || body.Length == 0)
            throw new System.ArgumentException("Клип без кадров: " + name);
        if (face == null || face.Length != body.Length)
            throw new System.ArgumentException("Тело и лицо разной длины: " + name);

        Name = name;
        Body = body;
        Face = face;
        Fps = Mathf.Max(0.01f, fps);
        Loop = loop;
    }
}

/// Головка проигрывателя: помнит клип, время и номер кадра. Обычный класс,
/// а не MonoBehaviour, — чтобы его можно было гонять в редакторских тестах
/// без сцены и без игрового времени.
public class SpriteAnimPlayer
{
    public SpriteAnim Clip { get; private set; }
    public int Frame { get; private set; }

    /// Одиночный клип доигран до последнего кадра. У петли всегда false.
    public bool Done { get; private set; }

    /// Событие на конец: одиночный клип шлёт его ровно один раз, петля —
    /// на каждом обороте. По нему WormAnimator возвращается из «боли»
    /// и «приземления» в обычную стойку.
    public event System.Action<SpriteAnim> Ended;

    float _time;

    /// Поставить клип. Тот же клип по умолчанию не перезапускается: состояние
    /// червя пересчитывается каждый кадр, и перезапуск сбрасывал бы ходьбу
    /// на первый кадр вечно.
    public void Play(SpriteAnim clip, bool restart = false)
    {
        if (clip == null) return;
        if (clip == Clip && !restart) return;
        Clip = clip;
        _time = 0f;
        Frame = 0;
        Done = false;
    }

    public void Tick(float dt)
    {
        if (Clip == null || dt <= 0f) return;
        if (Done && !Clip.Loop) return;

        _time += dt;
        float len = Clip.Length;
        bool ended = _time >= len;

        if (Clip.Loop)
        {
            if (ended) _time = Mathf.Repeat(_time, len);
            Frame = Mathf.Clamp(Mathf.FloorToInt(_time * Clip.Fps), 0, Clip.Frames - 1);
            if (ended) Ended?.Invoke(Clip);
            return;
        }

        if (ended)
        {
            _time = len;
            Frame = Clip.Frames - 1;
            Done = true;
            Ended?.Invoke(Clip);
            return;
        }

        Frame = Mathf.Clamp(Mathf.FloorToInt(_time * Clip.Fps), 0, Clip.Frames - 1);
    }

    /// Показать кадр по номеру, а не по времени. Так работает прицел: кадр
    /// там выбирает угол, а не секундомер, и головке нечего отсчитывать.
    public void Show(SpriteAnim clip, int frame)
    {
        if (clip == null) return;
        Clip = clip;
        _time = 0f;
        Done = false;
        Frame = Mathf.Clamp(frame, 0, clip.Frames - 1);
    }

    public Sprite BodyFrame => Clip != null ? Clip.Body[Frame] : null;
    public Sprite FaceFrame => Clip != null ? Clip.Face[Frame] : null;
}
