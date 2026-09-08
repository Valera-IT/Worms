using UnityEngine;

/// Вода: тело, кромка и блики (фаза 13e).
///
/// Раньше вся вода была одной квадой цвета `TerrainStyle.Water` с прямой
/// верхней гранью — линейка поперёк карты. Теперь под ту же кваду подложены
/// две бегущие волны и рассыпаны короткие блики, а сама квада опущена так,
/// чтобы её грань пряталась под волной.
///
/// Волна — это спрайт-полоса длиной 64 юнита, у которой профиль сшит из двух
/// гармоник с целым числом периодов: левый край равен правому, поэтому
/// плитки рядом дают бесконечную ленту, а бег — сдвиг ленты с возвратом
/// на длину плитки. Плитки белые, цвет даёт `SpriteRenderer`, поэтому пять
/// стилей мира не умножают набор текстур: полос всего три и строятся один раз
/// на запуск, как кадры червя.
///
/// Подъём воды при потопе сглаживает `GameManager.FloodTick`; здесь только
/// следим за `DestructibleTerrain.WaterLevel` каждый кадр, чтобы кромка ехала
/// вместе с телом.
public class Water : MonoBehaviour
{
    /// Порядки отрисовки. Черви — 10, тело воды — 15 (как было).
    const int OrderBack = 14, OrderBody = 15, OrderFront = 16, OrderFoam = 17, OrderGlint = 18;

    /// Геометрия полосы: 768 пикселей по 12 в юните — плитка в 64 юнита.
    /// Двенадцать взяты у ландшафта (`DestructibleTerrain.PixelsPerUnit`):
    /// с другим шагом кромка выбивалась бы из пиксельной сетки карты.
    const int StripW = 768, StripH = 32;
    const float StripPPU = DestructibleTerrain.PixelsPerUnit;
    const float TileWidth = StripW / StripPPU;      // 64 юнита
    /// Средняя линия полосы: от неё волна ходит вверх и вниз, и ею полоса
    /// садится на уровень воды.
    const int Baseline = 18;
    /// Сколько полоса заливает вниз от средней линии — на столько же опущена
    /// верхняя грань квады, чтобы стык не оказался виден в провале волны.
    const float Skirt = Baseline / StripPPU;        // 1,5 юнита

    /// Амплитуды в пикселях полосы и скорости бега в юнитах в секунду.
    /// Дальняя волна выше, длиннее, медленнее и идёт навстречу ближней —
    /// от этого кромка не выглядит одной ползущей картинкой.
    const float AmpFront = 6f, AmpBack = 9f;
    const float SpeedFront = 0.8f, SpeedBack = -0.5f;

    /// Дыхание: кромка не только бежит вбок, но и качается вверх-вниз и
    /// раздаётся по высоте. Без этого две ленты ползли одной жёсткой картинкой
    /// и вода выглядела ровно такой же мёртвой, как одна прямая квада.
    /// Частоты у слоёв несоизмеримые — рисунок не повторяется.
    const float BobAmp = 0.10f, SwellAmp = 0.28f;

    /// Блики: сколько живут, как часто зажигаются и сколько горит разом.
    const float GlintLife = 0.55f, GlintEvery = 0.16f;
    const int GlintMax = 12;

    static Sprite _front, _back, _foam;

    Transform _body;                    // квада: всё, что ниже кромки
    Transform _frontRow, _backRow;      // ленты плиток, которые бегут
    float _frontX, _backX;              // сдвиг вбок
    float _frontY, _backY;              // качание вверх-вниз
    float _frontS = 1f, _backS = 1f;    // раздача по высоте

    Glint[] _glints;
    float _glintTimer;
    System.Random _rnd;
    Camera _cam;
    Color _glintColor;

    class Glint { public Transform T; public SpriteRenderer R; public float Life; public float Span; }

    /// Собирает воду под общим корнем мира. Цвета — из стиля ландшафта.
    public static Water Build(Transform root, TerrainStyle style, int seed)
    {
        var go = new GameObject("Water");
        go.transform.SetParent(root, false);
        var w = go.AddComponent<Water>();
        w.Setup(style, seed);
        return w;
    }

    void Setup(TerrainStyle style, int seed)
    {
        _rnd = new System.Random(seed ^ 0x5EA1);

        var body = style.Water;
        // Дальняя волна бледнее и прозрачнее — иначе две ленты слипаются
        // в одно тёмное пятно там, где перекрываются.
        var back = Color.Lerp(body, Color.white, 0.18f);
        back.a = body.a * 0.62f;
        var foam = Color.Lerp(body, Color.white, 0.75f);
        foam.a = Mathf.Min(1f, body.a + 0.15f);
        _glintColor = foam;

        _body = Sprites.Make("Body", Sprites.Square, body, OrderBody, transform).transform;

        _backRow = Row("Back", Strip(1), back, OrderBack);
        _frontRow = Row("Front", Strip(0), body, OrderFront);
        // Пена лежит на той же ленте, что и ближняя волна: у них общий профиль,
        // разъехаться они не могут.
        var foamRow = Row("Foam", Foam, foam, OrderFoam);
        foamRow.SetParent(_frontRow, false);
        foamRow.localPosition = Vector3.zero;

        _glints = new Glint[GlintMax];
        for (int i = 0; i < GlintMax; i++)
        {
            var sr = Sprites.Make("Glint", Sprites.Square, _glintColor, OrderGlint, transform);
            sr.gameObject.SetActive(false);
            _glints[i] = new Glint { T = sr.transform, R = sr };
        }

        Layout();
    }

    /// Лента из четырёх плиток: карта шириной 96 юнитов, сдвиг ленты доходит
    /// до 64 — двух плиток слева и двух справа хватает при любом сдвиге.
    Transform Row(string name, Sprite sprite, Color color, int order)
    {
        var row = new GameObject(name).transform;
        row.SetParent(transform, false);
        for (int i = -2; i <= 1; i++)
        {
            var sr = Sprites.Make("Tile", sprite, color, order, row);
            sr.transform.localPosition = new Vector3(i * TileWidth, 0f, 0f);
        }
        return row;
    }

    /// Ставит тело и ленты на текущий уровень воды. Зовётся из `Update` и из
    /// `GameManager` сразу после смены уровня — чтобы кромка не отставала
    /// на кадр в момент, когда вода прибыла.
    public void Layout()
    {
        float level = DestructibleTerrain.WaterLevel;
        bool has = level > 0f;
        if (_body != null) _body.gameObject.SetActive(has);
        if (_frontRow != null) _frontRow.gameObject.SetActive(has);
        if (_backRow != null) _backRow.gameObject.SetActive(has);
        if (!has) return;

        float mid = DestructibleTerrain.WorldWidth * 0.5f;

        // Грань квады уходит под нижний край ленты — не под постоянную юбку,
        // а под ту, что получилась после качания и раздачи. С постоянной
        // между поджатой лентой и квадой открывалась щель, и вода рвалась
        // вдоль всей кромки.
        float top = level + _frontY - Skirt * _frontS - 0.05f;
        _body.position = new Vector3(mid, top * 0.5f - 20f, 0f);
        _body.localScale = new Vector3(DestructibleTerrain.WorldWidth * 3f, top + 40f, 1f);

        // Пивот полос стоит на средней линии, поэтому раздача по высоте тянет
        // гребни вверх, а провалы вниз — вода вспухает, а не съезжает.
        _frontRow.position = new Vector3(mid + _frontX, level + _frontY, 0f);
        _frontRow.localScale = new Vector3(1f, _frontS, 1f);
        _backRow.position = new Vector3(mid + _backX, level + 0.25f + _backY, 0f);
        _backRow.localScale = new Vector3(1f, _backS, 1f);
    }

    void Update()
    {
        if (DestructibleTerrain.WaterLevel <= 0f) { Layout(); return; }

        float dt = Time.deltaTime;
        _frontX = Wrap(_frontX + SpeedFront * dt);
        _backX = Wrap(_backX + SpeedBack * dt);

        float t = Time.time;
        _frontY = Mathf.Sin(t * 1.13f) * BobAmp;
        _backY = Mathf.Sin(t * 0.79f + 1.9f) * BobAmp * 1.4f;
        _frontS = 1f + Mathf.Sin(t * 0.61f) * SwellAmp;
        _backS = 1f + Mathf.Sin(t * 0.43f + 2.4f) * SwellAmp;

        Layout();
        Glints(dt);
    }

    /// Сдвиг ленты живёт в [0; длина плитки): уехавшая плитка возвращается
    /// на своё место, и шва при этом нет — профиль полосы сшит по краям.
    static float Wrap(float x)
    {
        x %= TileWidth;
        return x < 0f ? x + TileWidth : x;
    }

    /// Блики: короткие светлые чёрточки, зажигающиеся на кромке в поле зрения
    /// камеры. Частиц в проекте нет — это те же короткоживущие спрайты, что
    /// осколки и всплывающие числа.
    void Glints(float dt)
    {
        for (int i = 0; i < _glints.Length; i++)
        {
            var g = _glints[i];
            if (g.Life <= 0f) continue;
            g.Life -= dt;
            if (g.Life <= 0f) { g.T.gameObject.SetActive(false); continue; }

            // Разгорается и гаснет, а не мигает: половина жизни на каждое.
            float t = g.Life / GlintLife;
            float a = 1f - Mathf.Abs(t * 2f - 1f);
            var c = _glintColor;
            c.a *= a;
            g.R.color = c;
            g.T.localScale = new Vector3(g.Span * (0.5f + a * 0.5f), 0.14f, 1f);
        }

        _glintTimer -= dt;
        if (_glintTimer > 0f) return;
        _glintTimer = GlintEvery;

        if (_cam == null) _cam = Camera.main;
        if (_cam == null || !_cam.orthographic) return;

        float half = _cam.orthographicSize * _cam.aspect;
        float cx = _cam.transform.position.x;
        float level = DestructibleTerrain.WaterLevel;
        // Кромка вне кадра бликов не даёт: зажигать их за краем экрана —
        // работа впустую.
        if (_cam.transform.position.y - _cam.orthographicSize > level + 1.5f) return;

        for (int i = 0; i < _glints.Length; i++)
        {
            var g = _glints[i];
            if (g.Life > 0f) continue;
            float x = cx + ((float)_rnd.NextDouble() * 2f - 1f) * half;
            float w = 0.35f + (float)_rnd.NextDouble() * 0.6f;
            g.Span = w;
            g.Life = GlintLife;
            // Чуть ниже гребня: на самой кромке блик пропадал бы в пене,
            // которая ровно такого же цвета.
            g.T.position = new Vector3(x, level - 0.25f - (float)_rnd.NextDouble() * 0.7f, 0f);
            g.T.localScale = new Vector3(w * 0.5f, 0.14f, 1f);
            g.R.color = _glintColor;
            g.T.gameObject.SetActive(true);
            return;
        }
    }

    // --- профиль волны ------------------------------------------------------

    /// Высота волны в долях амплитуды: две гармоники с целым числом периодов,
    /// поэтому `Wave(l, 0) == Wave(l, 1)` и плитки сходятся без шва.
    /// Периодов много намеренно: двенадцать на плитку — это волна в пять
    /// юнитов, вдвое короче экрана в упор. Три-четыре периода на плитку,
    /// с которых начиналась кромка, растягивались на весь кадр и читались
    /// не волной, а наклоном.
    /// Открыто ради теста — он проверяет именно сшивку.
    public static float Wave(int layer, float u)
    {
        const float Tau = Mathf.PI * 2f;
        return layer == 0
            ? 0.55f * Mathf.Sin(Tau * 12f * u) + 0.45f * Mathf.Sin(Tau * 20f * u + 2.1f)
            : 0.60f * Mathf.Sin(Tau * 8f * u + 0.7f) + 0.40f * Mathf.Sin(Tau * 14f * u + 3.4f);
    }

    /// Полоса волны: белая заливка от нижнего края полосы до гребня.
    /// Слой 0 — ближняя волна, 1 — дальняя. Строится один раз на запуск.
    public static Sprite Strip(int layer)
    {
        if (layer == 0 && _front != null) return _front;
        if (layer != 0 && _back != null) return _back;

        float amp = layer == 0 ? AmpFront : AmpBack;
        var pix = new Pix(StripW, StripH);
        for (int x = 0; x < StripW; x++)
        {
            int crest = Crest(layer, x, amp);
            for (int y = 0; y <= crest; y++) pix.Set(x, y, new Color32(255, 255, 255, 255));
        }

        var sprite = pix.ToSprite(StripPPU, new Vector2(0f, Baseline / (float)StripH));
        sprite.name = layer == 0 ? "WaveFront" : "WaveBack";
        if (layer == 0) _front = sprite; else _back = sprite;
        return sprite;
    }

    /// Пена — те же два верхних пикселя гребня ближней волны, отдельным
    /// спрайтом: одним цветом её не высветлить, тень даёт `SpriteRenderer`.
    public static Sprite Foam
    {
        get
        {
            if (_foam != null) return _foam;
            var pix = new Pix(StripW, StripH);
            for (int x = 0; x < StripW; x++)
            {
                int crest = Crest(0, x, AmpFront);
                pix.Set(x, crest, new Color32(255, 255, 255, 255));
                if (crest > 0) pix.Set(x, crest - 1, new Color32(255, 255, 255, 160));
            }
            _foam = pix.ToSprite(StripPPU, new Vector2(0f, Baseline / (float)StripH));
            _foam.name = "WaveFoam";
            return _foam;
        }
    }

    static int Crest(int layer, int x, float amp)
        => Mathf.Clamp(Baseline + Mathf.RoundToInt(Wave(layer, x / (float)StripW) * amp), 1, StripH - 1);
}
