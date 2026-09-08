using UnityEngine;

/// Снаряжение, которое висит на черве: купол парашюта и выхлоп ранца.
/// До сих пор это были растянутый белый квадрат и круг — единственные два
/// места, где реквизит поз рисовался примитивом, а не кистью. Кадры, как и
/// у червя, строятся кодом на Pix и кэшируются один раз на запуск.
public static class GearSprite
{
    static readonly Color32 Silk = new Color32(242, 244, 250, 255);
    static readonly Color32 Shade = new Color32(198, 208, 226, 255);
    static readonly Color32 Cord = new Color32(228, 226, 218, 255);
    static readonly Color32 Ink = new Color32(24, 22, 30, 255);

    static readonly Color32 Core = new Color32(255, 246, 206, 255);
    static readonly Color32 Fire = new Color32(255, 178, 54, 255);
    static readonly Color32 Ember = new Color32(233, 96, 30, 255);

    const int CanopyW = 64, CanopyH = 40;
    const float CanopyPpu = 30f;   // 64 px / 30 ≈ 2,1 юнита в размахе

    const int FlameW = 24, FlameH = 36;
    const float FlamePpu = 42f;

    static Sprite[] _canopy, _flame;

    /// Купол: три кадра хлопка. Ткань между стропами то прогибается, то
    /// выпирает — от этого купол дышит, а не висит доской.
    public static Sprite[] Canopy
    {
        get
        {
            if (_canopy != null && _canopy[0] != null) return _canopy;
            _canopy = new Sprite[3];
            for (int i = 0; i < 3; i++) _canopy[i] = BuildCanopy(i).ToSprite(CanopyPpu);
            return _canopy;
        }
    }

    /// Пламя ранца: три кадра трепета — длина и рваность языка гуляют.
    public static Sprite[] Flame
    {
        get
        {
            if (_flame != null && _flame[0] != null) return _flame;
            _flame = new Sprite[3];
            for (int i = 0; i < 3; i++) _flame[i] = BuildFlame(i).ToSprite(FlamePpu);
            return _flame;
        }
    }

    static Pix BuildCanopy(int frame)
    {
        var p = new Pix(CanopyW, CanopyH);

        // Кромка купола гуляет по кадрам: середина и края ходят в противофазе,
        // иначе получится не хлопок, а качание всей ткани целиком.
        float bow = frame == 0 ? 0f : frame == 1 ? 1.6f : -1.4f;
        float cx = CanopyW * 0.5f;
        float top = CanopyH - 5f;
        float rimY = 15f;

        // Полотнище: столбик за столбиком от кромки до свода. Свод — дуга,
        // кромка — та же дуга, опущенная и подрезанная хлопком.
        for (int x = 2; x < CanopyW - 2; x++)
        {
            float u = (x - cx) / (cx - 2f);            // −1…1 поперёк купола
            float k = 1f - u * u;
            if (k <= 0f) continue;

            float roof = rimY + (top - rimY) * Mathf.Sqrt(k);
            float hem = rimY - 2.5f * k + bow * Mathf.Cos(u * 6.2f);

            // Три клина ткани: средний светлее, боковые уходят в тень.
            var c = Mathf.Abs(u) < 0.34f ? Silk : Shade;
            for (int y = Mathf.RoundToInt(hem); y <= Mathf.RoundToInt(roof); y++) p.Set(x, y, c);
        }

        // Стропы сходятся к точке подвеса под куполом.
        for (int i = -2; i <= 2; i++)
        {
            float u = i / 2f;
            float x = cx + u * (cx - 4f);
            float k = 1f - u * u;
            float hem = rimY - 2.5f * k + bow * Mathf.Cos(u * 6.2f);
            p.Line(x, hem, cx, 1f, 1.1f, Cord);
        }

        p.Outline(Ink);
        return p;
    }

    static Pix BuildFlame(int frame)
    {
        var p = new Pix(FlameW, FlameH);
        float cx = FlameW * 0.5f;

        // Язык пламени сужается книзу и через кадр меняет длину и увод вбок:
        // ровный конус читался бы сосулькой, а не огнём.
        float len = frame == 0 ? 26f : frame == 1 ? 32f : 22f;
        float lean = frame == 1 ? 0.8f : frame == 2 ? -1.1f : 0f;

        for (int i = 0; i < 3; i++)
        {
            float f = i / 2f;                       // 0 внешний слой, 1 сердцевина
            float w = Mathf.Lerp(6.5f, 2.2f, f);
            float l = len * Mathf.Lerp(1f, 0.55f, f);
            var c = i == 0 ? Ember : i == 1 ? Fire : Core;

            for (int y = 0; y < l; y++)
            {
                float t = y / l;                    // 0 у сопла, 1 у кончика
                float half = w * (1f - t * t);
                float x0 = cx + lean * t * t;
                p.Line(x0 - half, FlameH - 3f - y, x0 + half, FlameH - 3f - y, 1f, c);
            }
        }

        return p;
    }
}
