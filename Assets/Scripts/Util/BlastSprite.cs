using UnityEngine;

/// Кадры взрыва: вспышка, клуб дыма и искра. Рисуются кодом на Pix и
/// кэшируются один раз на запуск — как тело червя, купол и иконки оружия.
///
/// До фазы 13d взрыв был жёлтым кругом Sprites.Circle, который за 0,35 с
/// раздувался и таял. Круг читался отладочным маркером: у него нет ни
/// сердцевины, ни рваной кромки, ни того, что взрыв сперва вспыхивает,
/// а потом расходится кольцом.
public static class BlastSprite
{
    static readonly Color32 Core = new Color32(255, 250, 214, 255);
    static readonly Color32 Fire = new Color32(255, 176, 52, 255);
    static readonly Color32 Ember = new Color32(226, 88, 26, 255);
    static readonly Color32 Soot = new Color32(236, 236, 240, 255);

    const int FlashSize = 64;
    public const float FlashPpu = 32f;   // 64 px / 32 — кадр в два юнита

    const int PuffSize = 32;
    const float PuffPpu = 32f;

    const int SparkSize = 10;
    const float SparkPpu = 64f;

    static Sprite[] _flash, _puff;
    static Sprite _spark;

    /// Вспышка: три кадра. Разгорание — плотный ком с белой сердцевиной,
    /// пик — шар во весь радиус, затухание — рваное кольцо с выеденной
    /// серединой. Кромка у всех трёх гуляет по углу: ровный круг выдавал бы
    /// примитив, а не огонь.
    public static Sprite[] Flash
    {
        get
        {
            if (_flash != null && _flash[0] != null) return _flash;
            _flash = new Sprite[3];
            for (int i = 0; i < 3; i++) _flash[i] = BuildFlash(i).ToSprite(FlashPpu, false, FilterMode.Bilinear);
            return _flash;
        }
    }

    /// Клубы дыма: четыре кадра одного комка. Кадр выбирается при рождении
    /// и дальше не меняется — клуб живёт формой, а не перелистыванием;
    /// разные кадры нужны затем, чтобы из одной точки не поднимался
    /// столбик одинаковых шариков.
    public static Sprite[] Puff
    {
        get
        {
            if (_puff != null && _puff[0] != null) return _puff;
            _puff = new Sprite[4];
            for (int i = 0; i < 4; i++) _puff[i] = BuildPuff(i).ToSprite(PuffPpu, false, FilterMode.Bilinear);
            return _puff;
        }
    }

    /// Искра рикошета: белая точка в тёплом ореоле. Одна на все — их и так
    /// видно доли секунды, кадры тут были бы платой ни за что.
    public static Sprite Spark
    {
        get
        {
            if (_spark != null) return _spark;
            return _spark = BuildSpark().ToSprite(SparkPpu, false, FilterMode.Bilinear);
        }
    }

    static Pix BuildFlash(int frame)
    {
        var p = new Pix(FlashSize, FlashSize);
        float c = FlashSize * 0.5f;

        // Радиус, доля выеденной середины и границы поясов — вот и вся разница
        // между кадрами. Сердцевина к третьему кадру исчезает: остывший взрыв
        // белым уже не светит.
        float grow = frame == 0 ? 0.58f : frame == 1 ? 1f : 1.14f;
        float hollow = frame == 2 ? 0.6f : 0f;
        float core = frame == 0 ? 0.58f : frame == 1 ? 0.34f : -1f;
        float fire = frame == 0 ? 0.85f : frame == 1 ? 0.68f : 0f;
        float dim = frame == 2 ? 0.85f : 1f;
        float ph = frame * 1.9f;

        float rOut = (FlashSize * 0.5f - 2f) * grow;

        for (int y = 0; y < FlashSize; y++)
        for (int x = 0; x < FlashSize; x++)
        {
            float dx = x + 0.5f - c, dy = y + 0.5f - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float th = Mathf.Atan2(dy, dx);

            // Две гармоники по углу: одна даёт крупные языки, вторая — зубцы.
            float R = rOut * (1f + 0.11f * Mathf.Sin(3f * th + ph)
                                 + 0.07f * Mathf.Sin(7f * th - ph * 1.6f)
                                 + 0.04f * Mathf.Sin(13f * th + ph * 2.3f));
            if (d > R) continue;

            float t = d / R;              // 0 в центре, 1 на кромке
            if (t < hollow) continue;

            var col = t < core ? Core : t < fire ? Fire : Ember;
            float a = Mathf.Clamp01((1f - t) / 0.26f + 0.4f) * dim;
            p.Blend(x, y, new Color32(col.r, col.g, col.b, (byte)(a * 255f)));
        }

        return p;
    }

    static Pix BuildPuff(int frame)
    {
        var p = new Pix(PuffSize, PuffSize);
        float c = PuffSize * 0.5f;

        // Датчик от номера кадра: комки должны быть разными, но одинаковыми
        // от запуска к запуску — иначе снимок листа не с чем сравнивать.
        var rnd = new System.Random(frame * 31 + 7);
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        p.Soft(c, c, Rand(7.5f, 10f), Soot, 0.8f);
        for (int i = 0; i < 5; i++)
        {
            float th = Rand(0f, Mathf.PI * 2f);
            float off = Rand(2.5f, 7f);
            p.Soft(c + Mathf.Cos(th) * off, c + Mathf.Sin(th) * off, Rand(3.5f, 8f), Soot, 0.85f);
        }

        return p;
    }

    static Pix BuildSpark()
    {
        var p = new Pix(SparkSize, SparkSize);
        float c = SparkSize * 0.5f;
        p.Soft(c, c, 4.5f, new Color32(Fire.r, Fire.g, Fire.b, 170), 1f);
        p.Soft(c, c, 1.8f, Core, 0.5f);
        return p;
    }
}
