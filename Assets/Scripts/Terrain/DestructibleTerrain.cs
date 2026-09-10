using System.Collections.Generic;
using UnityEngine;

/// Разрушаемый ландшафт: пиксельная маска + текстура + коллайдеры-чанки,
/// которые пересобираются marching squares только там, где рвануло.
[RequireComponent(typeof(SpriteRenderer))]
public class DestructibleTerrain : MonoBehaviour
{
    public const int PixelsPerUnit = 12;
    public const float WorldWidth = 96f;
    public const float WorldHeight = 48f;

    /// Уровень воды задаёт стиль мира: в пещере её нет, в архипелаге она выше.
    /// Раньше был константой; обращение `DestructibleTerrain.WaterLevel` не изменилось.
    public static float WaterLevel { get; private set; } = 5.5f;

    /// Поднять или опустить воду. Нужно внезапной смерти: она заливает карту
    /// раунд за раундом, в том числе там, где воды изначально не было (пещера).
    public static void SetWaterLevel(float level) => WaterLevel = level;

    /// Минимальный просвет над поверхностью, при котором на неё можно встать (в пикселях).
    /// Чуть выше червя: диаметр его коллайдера — 12 пикселей.
    const int MinHeadroom = 14;

    /// Просвет, нужный именно под высадку: полтора роста червя. Больше, чем
    /// MinHeadroom, — под навесом червю мало «пролезть», он должен ещё и
    /// прыгать, а раньше весь просвет был небом и вопрос не стоял.
    const int SpawnHeadroom = 26;

    /// Шаг выборки маски для коллайдера (в пикселях). Больше — дешевле, но грубее.
    const int ColliderStep = 4;

    /// Сторона чанка коллайдера в пикселях. Кратна ColliderStep.
    const int ChunkPixels = 128;
    const int ChunkSamples = ChunkPixels / ColliderStep;

    /// Замер по фазам: включается на время бенчмарка, в бою не нужен.
    /// Куда докладывать о каждом изменении ландшафта. Ставит сетевой хост:
    /// воронки, коридоры и балки рассылаются клиентам как есть, поэтому карта
    /// у всех сходится до пикселя, а не «примерно».
    public static System.Action<TerrainOp> OpSink;

    public static bool Profile;
    public static float LastMaskMs, LastPaintMs, LastTexMs, LastColliderMs;
    public static int LastChunksRebuilt, LastDirtyPixels;

    public int W { get; private set; }
    public int H { get; private set; }

    /// Стиль мира: палитра, вода, небо. Задаётся при Build и дальше не меняется.
    public TerrainStyle Style { get; private set; }

    bool[] _solid;
    bool[] _grass;   // трава ставится один раз при генерации: воронки остаются голыми
    bool[] _crust;   // светлая корка по всему контуру суши, тоже один раз
    /// Копоть вокруг воронок: не признак, а плотность 0…255. Одним битом
    /// она красила кромку сплошной чёрной каймой ровной ширины — кольцом
    /// вокруг ямы, а не следом огня. Плотностью же копоть садится на землю
    /// рваным пятном и сходит на нет к краю.
    byte[] _scorch;

    // Украшения (деревья, кактусы, камни) не спрайты за ландшафтом, а его часть:
    // их пиксели впечатаны в маску и в цвет, поэтому дерево держит червя,
    // ловит снаряд и рвётся воронкой наравне с землёй — как в оригинале.
    Color32[] _decal;
    bool[] _hasDecal;

    // Мост через пролив состоит из двух разных вещей. Настил — порода со своим
    // цветом, но, в отличие от кроны дерева, это пол: по нему ходят, на нём
    // стоят, и высота поверхности считается по нему. Канаты и стойки — только
    // цвет: породы под ними нет, коллайдер их не видит, и червь проходит
    // сквозь перила, а не спотыкается о них.
    bool[] _deck;
    bool[] _paint;
    Texture2D _tex;
    Color32[] _pixels;
    Color32[] _block;   // буфер под частичный SetPixels32, растёт по мере надобности
    SpriteRenderer _sr;

    // Коллайдер разрезан на чанки: взрыв трогает один-четыре, а не всю карту.
    PolygonCollider2D[] _chunks;
    int _chunksX, _chunksY;
    int _samplesX, _samplesY;   // всего узлов сетки выборки по осям
    bool[] _chunkGrid;          // переиспользуемая сетка одного чанка

    static readonly Color32 Empty = new Color32(0, 0, 0, 0);

    /// Граница сплошной породы в пикселях — считается из RockBand стиля,
    /// плюс размах, на который её ведёт шум.
    int _rockBelow;
    float _rockWobble;

    public void Build(int seed) => Build(seed, TerrainKind.Island);

    public void Build(int seed, TerrainKind kind)
    {
        Style = TerrainStyle.For(TerrainStyle.Resolve(kind, seed));
        WaterLevel = Style.WaterLevel;

        W = Mathf.RoundToInt(WorldWidth * PixelsPerUnit);
        H = Mathf.RoundToInt(WorldHeight * PixelsPerUnit);

        _sr = GetComponent<SpriteRenderer>();

        _solid = new bool[W * H];
        _grass = new bool[W * H];
        _crust = new bool[W * H];
        _scorch = new byte[W * H];
        _decal = new Color32[W * H];
        _hasDecal = new bool[W * H];
        _deck = new bool[W * H];
        _paint = new bool[W * H];
        _pixels = new Color32[W * H];
        _block = new Color32[0];

        BuildRamps();
        _rockBelow = Mathf.RoundToInt(Style.RockBand * H);
        _rockWobble = Style.RockBand > 0.08f ? H * 0.10f : 0f;
        Generate(seed);

        // Мосты — раньше украшений: дерево не должно вырасти посреди настила,
        // а FlatSpot узнаёт о настиле как об обычной поверхности.
        Bridges.Build(this, seed);

        // Украшения впечатываются после травы: они её перекрывают, а не наоборот.
        Scenery.StampDecor(this, seed);

        _tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _tex.filterMode = FilterMode.Point;
        _tex.wrapMode = TextureWrapMode.Clamp;
        RepaintRect(0, 0, W - 1, H - 1);
        _tex.SetPixels32(_pixels);
        _tex.Apply();

        _sr.sprite = Sprite.Create(_tex, new Rect(0, 0, W, H), Vector2.zero, PixelsPerUnit, 0, SpriteMeshType.FullRect);
        _sr.sortingOrder = 0;
        transform.position = Vector3.zero;

        SetupChunks();
    }

    /// Четыре ступени яркости земли и породы плюс их обводка. Считаются один
    /// раз при Build: стиль после этого не меняется.
    Color32[] _dirtRamp, _rockRamp, _dirtSeam, _rockSeam;

    void BuildRamps()
    {
        Color a = Style.DirtA, b = Style.DirtB, edge = Style.DirtEdge, hi = Style.DirtHi, rock = Style.Rock;

        _dirtRamp = new[]
        {
            (Color32)Color.Lerp(edge, b, 0.45f),
            (Color32)b,
            (Color32)a,
            (Color32)Color.Lerp(a, hi, 0.6f)
        };

        _rockRamp = new[]
        {
            (Color32)Color.Lerp(edge, rock, 0.5f),
            (Color32)rock,
            (Color32)Color.Lerp(rock, hi, 0.22f),
            (Color32)Color.Lerp(rock, hi, 0.45f)
        };

        _dirtSeam = new Color32[_dirtRamp.Length];
        _rockSeam = new Color32[_rockRamp.Length];
        for (int i = 0; i < _dirtRamp.Length; i++)
        {
            _dirtSeam[i] = (Color32)Color.Lerp(_dirtRamp[i], edge, 0.7f);
            _rockSeam[i] = (Color32)Color.Lerp(_rockRamp[i], edge, 0.55f);
        }
    }

    void Generate(int seed)
    {
        TerrainShape.Fill(_solid, W, H, Style, seed);
        BakeGrass();
        BakeCrust();
    }

    /// Трава (мох, снежная шапка) кладётся один раз при генерации — на первую
    /// поверхность под открытым просветом. Воронки после взрыва остаются голыми.
    void BakeGrass()
    {
        for (int x = 0; x < W; x++)
        {
            int gap = 0;
            for (int y = H - 1; y >= 0; y--)
            {
                if (!_solid[y * W + x]) { gap++; continue; }

                // Кровля потолка и стены пещеры просвета над собой не имеют —
                // проходим их насквозь и красим первый настоящий пол.
                if (gap < MinHeadroom) { gap = 0; continue; }

                for (int d = 0; d < Style.GrassDepth && y - d >= 0; d++)
                {
                    if (!_solid[(y - d) * W + x]) break;
                    _grass[(y - d) * W + x] = true;
                }
                // Не break: ниже могут быть ещё этажи — пол каверны, полка под
                // навесом. Их кромку красим так же, иначе многоярусный мир
                // выглядит как земля с чёрными дырами.
                gap = 0;
            }
        }
    }

    /// Корка по контуру: твёрдый пиксель, рядом с которым в пределах
    /// CrustDepth есть пустота. В отличие от травы она не знает, где верх, —
    /// поэтому обводит и навесы, и бока, и свод каверны. Печётся один раз при
    /// генерации: воронка оставляет голый срез, как в оригинале.
    void BakeCrust()
    {
        int d = Style.CrustDepth;
        if (d <= 0) return;

        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            for (int x = 0; x < W; x++)
            {
                int i = row + x;
                if (!_solid[i] || _grass[i]) continue;

                bool edge = false;
                for (int oy = -d; oy <= d && !edge; oy++)
                {
                    int ny = y + oy;
                    // За краем карты пустоты нет: иначе по низу и бокам
                    // текстуры шла бы кайма, которой в мире не видно.
                    if (ny < 0 || ny >= H) continue;
                    int nrow = ny * W;
                    for (int ox = -d; ox <= d; ox++)
                    {
                        int nx = x + ox;
                        if (nx < 0 || nx >= W) continue;
                        if (ox * ox + oy * oy > d * d) continue;   // круглая кромка, не квадратная
                        if (!_solid[nrow + nx]) { edge = true; break; }
                    }
                }

                if (edge) _crust[i] = true;
            }
        }
    }

    /// Впечатать нарисованный кодом спрайт в ландшафт: непрозрачные пиксели
    /// становятся породой с собственным цветом. `cx` — центр по горизонтали,
    /// `by` — низ спрайта, оба в пикселях карты; `scale` целочисленно не
    /// округляется, выборка ближайшего соседа.
    public void StampSprite(Pix sprite, int cx, int by, float scale, bool flip)
    {
        int sw = Mathf.RoundToInt(sprite.W * scale);
        int sh = Mathf.RoundToInt(sprite.H * scale);
        if (sw <= 0 || sh <= 0) return;

        int x0 = cx - sw / 2;

        for (int y = 0; y < sh; y++)
        {
            int ty = by + y;
            if (ty < 0 || ty >= H) continue;
            int srcY = Mathf.Clamp(Mathf.FloorToInt(y / scale), 0, sprite.H - 1);

            for (int x = 0; x < sw; x++)
            {
                int tx = x0 + x;
                if (tx < 0 || tx >= W) continue;

                int srcX = Mathf.Clamp(Mathf.FloorToInt(x / scale), 0, sprite.W - 1);
                if (flip) srcX = sprite.W - 1 - srcX;

                var c = sprite.Get(srcX, srcY);
                if (c.a < 128) continue;   // полупрозрачную кромку в маску не берём

                int i = ty * W + tx;
                _solid[i] = true;
                _grass[i] = false;
                _decal[i] = c;
                _hasDecal[i] = true;
            }
        }
    }

    /// Доска настила: порода со своим цветом, по которой ходят. Зовётся из
    /// Bridges на этапе генерации, до текстуры и коллайдеров, поэтому ничего
    /// не перерисовывает — как и StampSprite.
    public void StampDeck(int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = y * W + x;
        _solid[i] = true;
        _grass[i] = false;
        _scorch[i] = 0;
        _decal[i] = c;
        _hasDecal[i] = true;
        _deck[i] = true;
    }

    /// Канат: только цвет, без породы. Коллайдер его не видит, поэтому перила
    /// и стойки не мешают ни ходьбе, ни прыжку — но взрыв их рвёт.
    /// В породу не пишем: канат за ней всё равно не виден.
    public void StampRope(int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        int i = y * W + x;
        if (_solid[i]) return;
        _decal[i] = c;
        _hasDecal[i] = true;
        _paint[i] = true;
    }

    /// Настил ли это — для тестов и аудита мостов.
    public bool IsDeckPixel(int x, int y)
        => x >= 0 && y >= 0 && x < W && y < H && _deck[y * W + x];

    // ---------- преобразования координат ----------

    public Vector2 PixelToWorld(int px, int py) => new Vector2(px / (float)PixelsPerUnit, py / (float)PixelsPerUnit);
    public int WorldToPixelX(float wx) => Mathf.RoundToInt(wx * PixelsPerUnit);
    public int WorldToPixelY(float wy) => Mathf.RoundToInt(wy * PixelsPerUnit);

    public bool IsSolidPixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        return _solid[y * W + x];
    }

    public bool IsSolidWorld(Vector2 p) => IsSolidPixel(WorldToPixelX(p.x), WorldToPixelY(p.y));

    /// Высота поверхности, на которую можно встать (в мировых координатах),
    /// или -1 если такой нет. Сканирует сверху вниз и берёт первую твёрдую
    /// поверхность под просветом не ниже червя — иначе в пещере вернулась бы
    /// кровля потолка и червь заспавнился бы внутри камня.
    public float SurfaceHeightWorld(float wx)
    {
        int x = Mathf.Clamp(WorldToPixelX(wx), 0, W - 1);
        int gap = 0;
        for (int y = H - 1; y >= 0; y--)
        {
            int i = y * W + x;
            // Крона и ствол — порода, но не земля: ходят и высаживаются по
            // грунту под ними, иначе червь стоял бы на верхушке дерева.
            // Настил моста — исключение: он для того и положен, чтобы по нему
            // шли, и червь (а с ним и бот, считающий дорогу) обязан его видеть.
            if (!_solid[i] || (_hasDecal[i] && !_deck[i])) { gap++; continue; }
            if (gap >= MinHeadroom) return (y + 1) / (float)PixelsPerUnit;
            gap = 0;
        }
        return -1f;
    }

    /// Все площадки колонки сверху вниз, а не одна верхняя: полка под
    /// навесом, пол каверны, плита в воздухе. Ради них всё и затевалось —
    /// иначе высадка знает только силуэт и вся команда стоит в линию.
    public void LedgesAt(float wx, List<float> into, int minGap = MinHeadroom)
    {
        into.Clear();
        int x = Mathf.Clamp(WorldToPixelX(wx), 0, W - 1);
        int gap = 0;
        for (int y = H - 1; y >= 0; y--)
        {
            int i = y * W + x;
            if (!_solid[i] || (_hasDecal[i] && !_deck[i])) { gap++; continue; }
            if (gap >= minGap) into.Add((y + 1) / (float)PixelsPerUnit);
            gap = 0;
        }
    }

    /// Есть ли рядом площадка на той же высоте — так проверяется, что червю
    /// есть где стоять, а не что он на острие скалы.
    bool HasLedgeNear(float wx, float y, List<float> buf, float tol)
    {
        LedgesAt(wx, buf, SpawnHeadroom);
        for (int i = 0; i < buf.Count; i++)
            if (Mathf.Abs(buf[i] - y) <= tol) return true;
        return false;
    }

    /// Все пригодные точки высадки по всей карте: каждый этаж каждой колонки.
    /// tol — на сколько сосед по горизонтали может отличаться по высоте.
    /// Прежние 1,2 юнита на плече 0,8 — это склон под пятьдесят градусов: червь
    /// на такой «площадке» съезжал вниз и уходил в воду ещё до первого хода.
    public List<Vector2> CollectSpawnPoints(float step = 1f, float tol = 0.35f)
    {
        var points = new List<Vector2>();
        var ledges = new List<float>();
        var buf = new List<float>();

        for (float x = 6f; x < WorldWidth - 6f; x += step)
        {
            LedgesAt(x, ledges, SpawnHeadroom);
            for (int i = 0; i < ledges.Count; i++)
            {
                float y = ledges[i];
                if (y < WaterLevel + 1.5f) continue;
                if (DecorBlocks(x, y)) continue;

                // Плёнка в один-два пикселя — не пол: кляксы и перекос
                // оставляют такие перепонки, а червь на них проваливается.
                int px = Mathf.Clamp(WorldToPixelX(x), 0, W - 1);
                int py = WorldToPixelY(y) - 1;
                bool thick = true;
                for (int d = 0; d < 4 && thick; d++) thick = IsSolidPixel(px, py - d);
                if (!thick) continue;

                // Ровно должно быть под всем червём и на шаг в обе стороны:
                // площадка шириной в один пиксель посреди ската не годится.
                bool flat = true;
                for (float d = -0.8f; d <= 0.8f && flat; d += 0.4f)
                    if (d != 0f) flat = HasLedgeNear(x + d, y, buf, tol);
                if (flat) points.Add(new Vector2(x, y + 0.8f));
            }
        }
        return points;
    }

    /// Мешает ли украшение встать на грунт в точке wx: смотрим ровно тот
    /// объём, который займёт червь, — полметра в стороны и два роста вверх.
    /// Ствол в этом объёме мешает, крона выше — уже нет.
    ///
    /// Прежние 0,7 в стороны и 2,4 юнита вверх — это два с половиной роста и
    /// шаг в каждую сторону: при нынешней густоте подлеска такая мерка
    /// закрывала больше половины всех полок на карте. Верхний ярус пропадал
    /// весь, каждой колонке доставалось не больше одной точки высадки, и
    /// многоярусные карты снова раскладывали команды в линию по берегу.
    public bool DecorBlocks(float wx, float groundY, float halfWidth = 0.55f)
    {
        int x0 = Mathf.Clamp(WorldToPixelX(wx - halfWidth), 0, W - 1);
        int x1 = Mathf.Clamp(WorldToPixelX(wx + halfWidth), 0, W - 1);
        int y0 = Mathf.Clamp(WorldToPixelY(groundY) - 2, 0, H - 1);
        int y1 = Mathf.Clamp(WorldToPixelY(groundY + 1.9f), 0, H - 1);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (_hasDecal[y * W + x]) return true;
        return false;
    }

    /// Сколько всего контуров сейчас в коллайдерах — для тестов и отладки.
    public int ColliderPathCount
    {
        get
        {
            int n = 0;
            if (_chunks != null)
                foreach (var c in _chunks) if (c != null) n += c.pathCount;
            return n;
        }
    }

    // ---------- разрушение ----------

    public void Explode(Vector2 worldPos, float worldRadius)
    {
        // У сетевого клиента своих воронок нет: он рвёт землю только там, где
        // её порвал хост, — иначе карты разъехались бы за первый же ход.
        if (NetSim.Mirror) return;
        OpSink?.Invoke(TerrainOp.Blast(worldPos, worldRadius));

        long t0Tick = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        int cx = WorldToPixelX(worldPos.x);
        int cy = WorldToPixelY(worldPos.y);
        int r = Mathf.CeilToInt(worldRadius * PixelsPerUnit);

        int pad = r + Mathf.Max(2, r / 6);
        int x0 = Mathf.Max(0, cx - pad), x1 = Mathf.Min(W - 1, cx + pad);
        int y0 = Mathf.Max(0, cy - pad), y1 = Mathf.Min(H - 1, cy + pad);
        if (x0 > x1 || y0 > y1) return;

        int r2 = r * r;
        // Копоть уходит заметно дальше воронки: у взрыва есть след, а не
        // окантовка. Ширина считается от радиуса, но снизу подпёрта — у
        // пулевых дырок кайма в один пиксель не читалась бы вовсе.
        int rim = r + Mathf.Max(3, r / 3);
        int rim2 = rim * rim;
        float band = Mathf.Max(1f, rim - r);

        // Копим точную границу изменённого — она обычно уже области сканирования.
        int dx0 = int.MaxValue, dy0 = int.MaxValue, dx1 = int.MinValue, dy1 = int.MinValue;
        bool holePunched = false;   // дырка меняет коллайдер, одна лишь копоть — нет

        for (int y = y0; y <= y1; y++)
        {
            int dy = y - cy;
            int row = y * W;
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx;
                int d2 = dx * dx + dy * dy;
                if (d2 > rim2) continue;

                int i = row + x;
                bool touched = false;
                if (d2 <= r2)
                {
                    if (_solid[i]) { _solid[i] = false; _grass[i] = false; _scorch[i] = 0; _deck[i] = false; touched = true; holePunched = true; }
                    // Канат породой не был — и коллайдер от него не меняется,
                    // но взрыв его рвёт, иначе перила висят над пустотой.
                    if (_paint[i]) { _paint[i] = false; _hasDecal[i] = false; touched = true; }
                }
                else if (_solid[i])
                {
                    // Плотность падает от края ямы наружу, а шум рвёт кромку:
                    // ровное кольцо выдавало бы циркуль. Слабую копоть поверх
                    // сильной не кладём — иначе второй взрыв рядом осветлял бы
                    // след первого.
                    float t = (Mathf.Sqrt(d2) - r) / band;
                    float n = Mathf.PerlinNoise(x * 0.16f + 3.1f, y * 0.16f + 8.7f);
                    float f = (1f - t) * (0.55f + 0.95f * n);
                    if (f <= 0.12f) continue;

                    byte v = (byte)(Mathf.Min(1f, f) * 255f);
                    if (v <= _scorch[i]) continue;
                    _scorch[i] = v;
                    touched = true;
                }

                if (!touched) continue;
                if (x < dx0) dx0 = x;
                if (x > dx1) dx1 = x;
                if (y < dy0) dy0 = y;
                if (y > dy1) dy1 = y;
            }
        }

        if (dx1 < dx0) return;   // ничего не изменилось

        long t1Tick = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        RepaintRect(dx0, dy0, dx1, dy1);

        long t2Tick = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        UploadRect(dx0, dy0, dx1, dy1);

        long t3Tick = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

        int rebuilt = 0;
        if (holePunched) rebuilt = RebuildChunksIn(dx0, dy0, dx1, dy1);

        if (Profile)
        {
            long t4Tick = System.Diagnostics.Stopwatch.GetTimestamp();
            float ms = 1000f / System.Diagnostics.Stopwatch.Frequency;
            LastMaskMs = (t1Tick - t0Tick) * ms;
            LastPaintMs = (t2Tick - t1Tick) * ms;
            LastTexMs = (t3Tick - t2Tick) * ms;
            LastColliderMs = (t4Tick - t3Tick) * ms;
            LastChunksRebuilt = rebuilt;
            LastDirtyPixels = (dx1 - dx0 + 1) * (dy1 - dy0 + 1);
        }
    }

    /// Рез вдоль отрезка: убирает породу в капсуле радиусом worldRadius.
    /// Бур и паяльная лампа копают коридор, а не воронку, — кругом такой
    /// проход пришлось бы набивать десятком взрывов, и каждый стоил бы своей
    /// пересборки коллайдера. Копоти по краям нет: это рез, а не взрыв.
    /// Возвращает true, если хоть один пиксель убран.
    public bool Dig(Vector2 from, Vector2 to, float worldRadius)
    {
        if (NetSim.Mirror) return false;
        OpSink?.Invoke(TerrainOp.Dug(from, to, worldRadius));

        float ax = from.x * PixelsPerUnit, ay = from.y * PixelsPerUnit;
        float bx = to.x * PixelsPerUnit, by = to.y * PixelsPerUnit;
        float r = worldRadius * PixelsPerUnit;

        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, bx) - r) - 1);
        int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(ax, bx) + r) + 1);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, by) - r) - 1);
        int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(ay, by) + r) + 1);
        if (x0 > x1 || y0 > y1) return false;

        float ex = bx - ax, ey = by - ay;
        float len2 = ex * ex + ey * ey;
        float r2 = r * r;

        int dx0 = int.MaxValue, dy0 = int.MaxValue, dx1 = int.MinValue, dy1 = int.MinValue;

        for (int y = y0; y <= y1; y++)
        {
            int row = y * W;
            for (int x = x0; x <= x1; x++)
            {
                int i = row + x;
                if (!_solid[i]) continue;

                // Расстояние до отрезка: проекция, зажатая в его концы.
                float t = len2 > 0.0001f ? ((x - ax) * ex + (y - ay) * ey) / len2 : 0f;
                t = Mathf.Clamp01(t);
                float px = ax + ex * t - x, py = ay + ey * t - y;
                if (px * px + py * py > r2) continue;

                _solid[i] = false;
                _grass[i] = false;
                _scorch[i] = 0;
                _hasDecal[i] = false;
                _deck[i] = false;

                if (x < dx0) dx0 = x;
                if (x > dx1) dx1 = x;
                if (y < dy0) dy0 = y;
                if (y > dy1) dy1 = y;
            }
        }

        if (dx1 < dx0) return false;

        RepaintRect(dx0, dy0, dx1, dy1);
        UploadRect(dx0, dy0, dx1, dy1);
        RebuildChunksIn(dx0, dy0, dx1, dy1);
        return true;
    }

    /// Балка: прямоугольник породы под углом, вписанный в маску тем же слоем
    /// украшений, что и деревья, — поэтому она держит червя, ловит снаряд и
    /// рвётся воронкой наравне с землёй, а цветом остаётся металлом.
    /// Возвращает true, если балка встала.
    public bool StampBeam(Vector2 center, float angleDeg, float length, float thickness, Color32 color)
    {
        if (NetSim.Mirror) return false;
        OpSink?.Invoke(TerrainOp.Beam(center, angleDeg, length, thickness, color));

        float cx = center.x * PixelsPerUnit, cy = center.y * PixelsPerUnit;
        float half = length * 0.5f * PixelsPerUnit;
        float halfT = thickness * 0.5f * PixelsPerUnit;
        float a = angleDeg * Mathf.Deg2Rad;
        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);

        int reach = Mathf.CeilToInt(half + halfT) + 1;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx) - reach);
        int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx) + reach);
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy) - reach);
        int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy) + reach);
        if (x0 > x1 || y0 > y1) return false;

        // Тёмная кромка по длинной стороне — иначе балка сливается с породой.
        var edge = (Color32)Color.Lerp(color, Color.black, 0.45f);

        int dx0 = int.MaxValue, dy0 = int.MaxValue, dx1 = int.MinValue, dy1 = int.MinValue;

        for (int y = y0; y <= y1; y++)
        {
            int row = y * W;
            for (int x = x0; x <= x1; x++)
            {
                // В систему координат балки: вдоль и поперёк.
                float ox = x - cx, oy = y - cy;
                float along = ox * ca + oy * sa;
                float across = -ox * sa + oy * ca;
                if (Mathf.Abs(along) > half || Mathf.Abs(across) > halfT) continue;

                int i = row + x;
                _solid[i] = true;
                _grass[i] = false;
                _scorch[i] = 0;
                _decal[i] = Mathf.Abs(across) > halfT - 1.2f ? edge : color;
                _hasDecal[i] = true;

                if (x < dx0) dx0 = x;
                if (x > dx1) dx1 = x;
                if (y < dy0) dy0 = y;
                if (y > dy1) dy1 = y;
            }
        }

        if (dx1 < dx0) return false;

        RepaintRect(dx0, dy0, dx1, dy1);
        UploadRect(dx0, dy0, dx1, dy1);
        RebuildChunksIn(dx0, dy0, dx1, dy1);
        return true;
    }

    /// Перекрашивает прямоугольник маски в буфер пикселей.
    void RepaintRect(int x0, int y0, int x1, int y1)
    {
        x0 = Mathf.Max(0, x0); x1 = Mathf.Min(W - 1, x1);
        y0 = Mathf.Max(0, y0); y1 = Mathf.Min(H - 1, y1);

        for (int y = y0; y <= y1; y++)
        {
            int row = y * W;
            for (int x = x0; x <= x1; x++)
            {
                int i = row + x;
                // Канат моста — единственное, что рисуется в пустоте: породы
                // под ним нет, поэтому и Shade его не касается.
                if (!_solid[i]) { _pixels[i] = _paint[i] ? _decal[i] : Empty; continue; }

                _pixels[i] = Shade(x, y, i);
            }
        }
    }

    /// Цвет одного пикселя породы. Чистая функция от координат и масок —
    /// поэтому воронка перекрашивает свой прямоугольник и шов не виден.
    ///
    /// Земля собрана из комков: шум двух частот разбивается на четыре ступени
    /// яркости, а по границе ступеней идёт тёмная линия. Получаются валуны с
    /// обводкой, как в спрайтовых «Червяках»; плоская двухцветная заливка,
    /// что была раньше, вблизи читалась как помехи телевизора.
    Color32 Shade(int x, int y, int i)
    {
        var c = Ground(x, y, i);
        byte soot = _scorch[i];
        if (soot == 0) return c;

        // Копоть не только красит в цвет гари, но и гасит яркость. Одного
        // Style.Scorch мало: на острове он (58,42,30), а тёмный комок земли —
        // (75,48,27), и полная копоть терялась бы в фактуре. Затемнение же
        // выводит её из любой палитры, оставляя цвету стиля тон.
        float k = soot / 255f;
        var burnt = Color.Lerp(c, Style.Scorch, k) * Mathf.Lerp(1f, 0.55f, k);
        burnt.a = 1f;   // умножение съело бы и альфу — порода стала бы прозрачной
        return burnt;
    }

    /// Цвет породы без копоти: трава, корка, декали, комки земли и камня.
    Color32 Ground(int x, int y, int i)
    {
        // Ствол и крона — такая же порода, только со своим цветом.
        if (_hasDecal[i]) return _decal[i];

        if (_crust[i])
        {
            // Корка тоже с фактурой: тёмная жилка по шуму, иначе кромка
            // читается как нарисованный поверх контур.
            float c = Mathf.PerlinNoise(x * 0.11f + 7.3f, y * 0.11f + 21.9f);
            return c > 0.62f ? (Color32)Color.Lerp(Style.CrustColor, Style.DirtEdge, 0.35f)
                             : Style.CrustColor;
        }

        if (_grass[i])
        {
            // Трава тоже комковатая: пятна тёмной зелени вместо шахматной сетки.
            float g = Mathf.PerlinNoise(x * 0.09f + 13.7f, y * 0.09f + 4.1f);
            return g > 0.58f ? Style.GrassDark : Style.Grass;
        }

        bool rock = y < _rockBelow + _rockWobble * (Mathf.PerlinNoise(x * 0.017f, 91.3f) - 0.5f)
                 || Mathf.PerlinNoise(x * 0.05f, y * 0.05f) > Style.RockNoise;

        float n = 0.62f * Mathf.PerlinNoise(x * 0.030f, y * 0.030f)
                + 0.38f * Mathf.PerlinNoise(x * 0.082f + 31.7f, y * 0.082f + 12.3f);

        var ramp = rock ? _rockRamp : _dirtRamp;
        var seam = rock ? _rockSeam : _dirtSeam;
        float t = Mathf.Clamp01(Mathf.InverseLerp(0.28f, 0.72f, n)) * (ramp.Length - 0.001f);
        int band = (int)t;

        // Ступенька между уровнями — тёмная обводка комка.
        return (t - band) < 0.13f && band > 0 ? seam[band] : ramp[band];
    }

    /// Заливает в текстуру только изменённый прямоугольник.    /// Заливает в текстуру только изменённый прямоугольник.
    void UploadRect(int x0, int y0, int x1, int y1)
    {
        int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
        int need = bw * bh;
        if (_block.Length < need) _block = new Color32[Mathf.NextPowerOfTwo(need)];

        for (int y = 0; y < bh; y++)
            System.Array.Copy(_pixels, (y0 + y) * W + x0, _block, y * bw, bw);

        _tex.SetPixels32(x0, y0, bw, bh, _block);
        _tex.Apply(false);
    }

    // ---------- коллайдер ----------

    void SetupChunks()
    {
        _samplesX = W / ColliderStep + 1;
        _samplesY = H / ColliderStep + 1;
        _chunksX = Mathf.CeilToInt((_samplesX - 1) / (float)ChunkSamples);
        _chunksY = Mathf.CeilToInt((_samplesY - 1) / (float)ChunkSamples);
        _chunkGrid = new bool[(ChunkSamples + 1) * (ChunkSamples + 1)];
        _chunks = new PolygonCollider2D[_chunksX * _chunksY];

        // Старый одиночный коллайдер на самом объекте больше не нужен.
        var legacy = GetComponent<PolygonCollider2D>();
        if (legacy != null)
        {
            if (Application.isPlaying) Destroy(legacy); else DestroyImmediate(legacy);
        }

        for (int cy = 0; cy < _chunksY; cy++)
        for (int cx = 0; cx < _chunksX; cx++)
        {
            var go = new GameObject($"Chunk_{cx}_{cy}");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            _chunks[cy * _chunksX + cx] = go.AddComponent<PolygonCollider2D>();
            RebuildChunk(cx, cy);
        }
    }

    /// Пересобирает чанки, задетые прямоугольником пикселей. Возвращает их число.
    int RebuildChunksIn(int px0, int py0, int px1, int py1)
    {
        // Узлы сетки, которые могли поменяться.
        int sA = px0 / ColliderStep, sB = Mathf.Min(_samplesX - 1, px1 / ColliderStep + 1);
        int tA = py0 / ColliderStep, tB = Mathf.Min(_samplesY - 1, py1 / ColliderStep + 1);

        // Чанк cx держит узлы [cx*S, cx*S+S] — граничный узел общий с соседом.
        int cx0 = Mathf.Max(0, Mathf.CeilToInt(sA / (float)ChunkSamples) - 1);
        int cx1 = Mathf.Min(_chunksX - 1, sB / ChunkSamples);
        int cy0 = Mathf.Max(0, Mathf.CeilToInt(tA / (float)ChunkSamples) - 1);
        int cy1 = Mathf.Min(_chunksY - 1, tB / ChunkSamples);

        int n = 0;
        for (int cy = cy0; cy <= cy1; cy++)
        for (int cx = cx0; cx <= cx1; cx++)
        {
            RebuildChunk(cx, cy);
            n++;
        }
        return n;
    }

    void RebuildChunk(int cx, int cy)
    {
        var poly = _chunks[cy * _chunksX + cx];
        if (poly == null) return;

        int s0 = cx * ChunkSamples, s1 = Mathf.Min(_samplesX - 1, s0 + ChunkSamples);
        int t0 = cy * ChunkSamples, t1 = Mathf.Min(_samplesY - 1, t0 + ChunkSamples);
        int gw = s1 - s0 + 1, gh = t1 - t0 + 1;
        if (gw < 2 || gh < 2) { poly.pathCount = 0; return; }

        for (int t = t0; t <= t1; t++)
        {
            int py = Mathf.Min(t * ColliderStep, H - 1) * W;
            int dst = (t - t0) * gw;
            for (int s = s0; s <= s1; s++)
                _chunkGrid[dst + (s - s0)] = _solid[py + Mathf.Min(s * ColliderStep, W - 1)];
        }

        float cell = ColliderStep / (float)PixelsPerUnit;
        var origin = new Vector2(s0 * cell, t0 * cell);
        var loops = MarchingSquares.Build(_chunkGrid, gw, gh, origin, cell, cell * cell * 0.02f);

        poly.pathCount = loops.Count;
        for (int i = 0; i < loops.Count; i++)
            poly.SetPath(i, loops[i].ToArray());
    }

    /// Подходящая точка для высадки червя: ровная площадка выше уровня воды.
    public bool FindSpawnPoint(float wx, out Vector2 spawn)
    {
        spawn = default;
        float h = SurfaceHeightWorld(wx);
        if (h < WaterLevel + 1.5f) return false;
        if (DecorBlocks(wx, h)) return false;

        // Проверяем, что рядом нет обрыва.
        for (float d = -0.8f; d <= 0.8f; d += 0.4f)
        {
            float hn = SurfaceHeightWorld(wx + d);
            if (hn < WaterLevel + 1f || Mathf.Abs(hn - h) > 1.2f) return false;
        }

        spawn = new Vector2(wx, h + 0.8f);
        return true;
    }
}
