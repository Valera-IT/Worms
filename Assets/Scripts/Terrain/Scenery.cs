using UnityEngine;

/// Задний план карты — небо-градиент, солнце, три хребта с параллаксом,
/// облака, перистая рвань и своды пещеры — и украшения: деревья, пальмы, ели, кактусы и
/// кристаллы.
///
/// Задник это отдельные спрайты за ландшафтом, а украшения — нет: они
/// впечатываются прямо в пиксельную маску (`StampDecor`), как в оригинале,
/// где карта была одной картинкой. Поэтому на дерево можно встать, оно ловит
/// снаряд и разлетается воронкой вместе с землёй.
///
/// Всё рисуется в рантайме на Pix — бинарных ассетов в проекте ноль.
public static class Scenery
{
    /// Порядки отрисовки. Ландшафт — 0, черви — 10, вода — 15.
    const int OrderSky = -30, OrderSun = -29, OrderCirrus = -28, OrderCavern = -28,
              OrderRidgeFarthest = -27, OrderRidgeFar = -26, OrderRidgeNear = -24,
              OrderCloud = -22;

    /// Задник: небо, солнце, хребты, облака. Зовётся после Build ландшафта —
    /// цвета берутся из его стиля.
    public static void Build(Transform root, DestructibleTerrain terrain, int seed)
    {
        var style = terrain.Style;
        var rnd = new System.Random(seed ^ 0x2C3D);

        float w = DestructibleTerrain.WorldWidth;
        float h = DestructibleTerrain.WorldHeight;

        BuildSky(root, style, w, h);

        if (style.Backdrop == BackdropKind.Cavern)
        {
            BuildCavern(root, style, rnd, w, h);
        }
        else
        {
            // Солнце выбирает сторону света: от неё зависит, какие склоны
            // хребтов освещены, а какие уходят в тень.
            float sunX = 0.2f + 0.6f * (float)rnd.NextDouble();
            float light = sunX < 0.5f ? -1f : 1f;

            BuildSun(root, style, sunX, w, h);
            if (style.Clouds) BuildCirrus(root, style, rnd, w, h);

            // Хребтов три, а не два: чем дальше ярус, тем он выше, бледнее,
            // глаже и меньше отстаёт от камеры. На двух глубина не читалась.
            bool forest = style.Decor == DecorKind.Tree || style.Decor == DecorKind.Palm
                       || style.Decor == DecorKind.Pine;
            float foot = Mathf.Max(2f, DestructibleTerrain.WaterLevel - 1f);
            BuildRidge(root, style, rnd, style.RidgeFar, OrderRidgeFarthest, 0.82f, foot + 7f, 44f, 0.45f, light, 0.34f, false);
            BuildRidge(root, style, rnd, style.RidgeFar, OrderRidgeFar, 0.72f, foot + 3f, 34f, 0.60f, light, 0.16f, forest);
            BuildRidge(root, style, rnd, style.RidgeNear, OrderRidgeNear, 0.52f, foot, 22f, 0.78f, light, 0f, forest);
            if (style.Clouds) BuildClouds(root, style, rnd, w, h);
            BuildBirds(root, style, rnd, w, h);
        }
    }

    // ---------- небо ----------

    /// Вертикальный градиент от зенита к горизонту. Слой почти приклеен к
    /// камере (Factor 0,9): небо должно оставаться небом и на краю карты.
    ///
    /// Стопов три, а не два: у зенита плотный цвет, в середине — основной,
    /// у горизонта светлая дымка. На двух стопах небо выглядело заливкой.
    static void BuildSky(Transform root, TerrainStyle style, float w, float h)
    {
        const int TH = 256;
        var pix = new Pix(4, TH);
        var horizon = Color.Lerp(style.Sky, Color.white, 0.34f);
        for (int y = 0; y < TH; y++)
        {
            float t = y / (float)(TH - 1);
            Color c = t < 0.30f
                ? Color.Lerp(horizon, style.Sky, Mathf.SmoothStep(0f, 1f, t / 0.30f))
                : Color.Lerp(style.Sky, style.SkyTop, Mathf.SmoothStep(0f, 1f, (t - 0.30f) / 0.70f));

            // Растянутый на пол-экрана градиент полосит; сбиваем полосы
            // дрожанием на четверть тона. Дрожание только по строкам: колонок
            // всего четыре, и каждая растянута на треть карты — по X это дало
            // бы не шум, а вертикальные плиты поперёк неба.
            float d = ((y & 3) - 1.5f) * 0.004f;
            var cd = new Color(c.r + d, c.g + d, c.b + d, 1f);
            for (int x = 0; x < pix.W; x++) pix.Set(x, y, cd);
        }

        var sr = Sprites.Make("Sky", pix.ToSprite(1f, false, FilterMode.Bilinear), Color.white, OrderSky, root);
        sr.transform.position = new Vector3(w * 0.5f, h * 0.55f, 6f);
        sr.transform.localScale = new Vector3(w * 4f / 4f, h * 2.4f / TH, 1f);
        ParallaxLayer.Attach(sr.gameObject, 0.9f);
    }

    /// Солнце: ядро, тёплый ореол и широкое зарево на полнеба. Одним диском
    /// оно читалось наклейкой — свет должен растекаться по небу.
    static void BuildSun(Transform root, TerrainStyle style, float sunX, float w, float h)
    {
        var pix = new Pix(128, 128);
        var warm = Color.Lerp(style.Sky, new Color(1f, 0.94f, 0.78f), 0.75f);
        pix.Soft(64f, 64f, 62f, new Color32((byte)(warm.r * 255), (byte)(warm.g * 255), (byte)(warm.b * 255), 60), 1f);
        pix.Soft(64f, 64f, 34f, new Color32(255, 248, 226, 110), 0.95f);
        pix.Soft(64f, 64f, 14f, new Color32(255, 252, 238, 240), 0.30f);

        var sr = Sprites.Make("Sun", pix.ToSprite(4.2f, false, FilterMode.Bilinear), Color.white, OrderSun, root);
        sr.transform.position = new Vector3(w * sunX, h * 0.84f, 5.5f);
        ParallaxLayer.Attach(sr.gameObject, 0.88f);
    }

    /// Хребет с объёмом: силуэт из ребристого шума, освещённые и теневые
    /// склоны, зернистая порода, снежные шапки с рваной границей, лесная
    /// опушка по гребню и дымка у подошвы.
    ///
    /// `light` — с какой стороны солнце (-1 слева, +1 справа), `haze` — на
    /// сколько слой уже растворён в небе: дальние хребты бледнее ближних.
    static void BuildRidge(Transform root, TerrainStyle style, System.Random rnd,
                           Color color, int order, float parallax, float footY,
                           float heightUnits, float roughness, float light,
                           float haze, bool forest)
    {
        const int TW = 1024, TH = 256;
        var pix = new Pix(TW, TH);

        float o1 = (float)rnd.NextDouble() * 100f;
        float o2 = (float)rnd.NextDouble() * 100f;
        float o3 = (float)rnd.NextDouble() * 100f;

        var sky = style.Sky;
        var body = Color.Lerp(color, sky, haze);
        var hi = Color.Lerp(body, Color.white, 0.34f);
        var lo = Color.Lerp(body, Color.Lerp(color, Color.black, 0.65f), 0.7f);
        var cap = Color.Lerp(body, Color.white, 0.80f);
        var wood = Color.Lerp(new Color(0.13f, 0.24f, 0.16f), sky, Mathf.Min(0.88f, haze + 0.52f));

        bool snowCaps = style.Kind == TerrainKind.Snow;
        bool mesa = style.Kind == TerrainKind.Canyon;   // каньон растёт столовыми горами

        // Профиль считаем отдельно и в дробных числах: тень на склоне зависит
        // от соседних колонок, а на целых пикселях она вышла бы лесенкой.
        var tops = new float[TW];
        for (int x = 0; x < TW; x++)
        {
            float nx = x / (float)TW;
            // Ребристый шум (1 - |2n-1|) даёт острые гребни вместо холмов;
            // возведение в квадрат углубляет седловины между вершинами.
            float r1 = Ridged(nx * 3.1f + o1, o1 * 0.13f);
            float f = 0.22f
                    + roughness * (0.52f * r1 * r1
                                 + 0.26f * Ridged(nx * 8.3f + o2, o2 * 0.31f)
                                 + 0.09f * Mathf.PerlinNoise(nx * 23f + o3, o3 * 0.7f));
            // Столовая гора: высота встаёт ступенями, между ними — обрыв.
            // Ступеней много и они мелкие: на семи все столовые горы выходили
            // одной высоты, и хребет читался забором.
            if (mesa) f = Mathf.Floor(f * 11f) / 11f + 0.010f * Mathf.PerlinNoise(nx * 60f, o3);
            tops[x] = Mathf.Clamp(f * TH, 6f, TH - 8f);
        }

        for (int x = 0; x < TW; x++)
        {
            int top = Mathf.RoundToInt(tops[x]);
            // Наклон меряем по четырём колонкам: по соседним он скачет от
            // пикселя к пикселю и рисует вертикальные полосы вместо склона.
            float slope = (tops[Mathf.Min(x + 6, TW - 1)] - tops[Mathf.Max(x - 6, 0)]) / 12f;
            // Склон, повёрнутый к солнцу, светлеет; отвернувшийся уходит в тень.
            float litSide = Mathf.Clamp(-slope * light * 1.3f, -1f, 1f);

            // Снег ложится по высоте, а не отступом от гребня: иначе он идёт
            // ровной лентой вдоль всего силуэта, включая подножия.
            float snowLine = TH * 0.60f + TH * 0.07f * (Mathf.PerlinNoise(x * 0.012f + o2, 3.7f) - 0.5f);
            float treeLine = 3f + 7f * Mathf.PerlinNoise(x * 0.07f + o3, 8.1f);

            for (int y = 0; y <= top; y++)
            {
                float d = top - y;                                   // глубина от гребня

                var c = litSide >= 0f ? Color.Lerp(body, hi, litSide)
                                      : Color.Lerp(body, lo, -litSide);

                // Распадки: шум вытянут по вертикали, поэтому ложится
                // промоинами сверху вниз, как на настоящем склоне.
                float gully = Mathf.PerlinNoise(x * 0.05f + o2, y * 0.012f) - 0.5f;
                float grain = Mathf.PerlinNoise(x * 0.22f + o3, y * 0.22f) - 0.5f;
                float m = 1f + gully * 0.18f + grain * 0.06f;
                // Осадочные слои: столовая гора без них — просто плита.
                if (mesa) m += 0.09f * Mathf.Sin(y * 0.45f + Mathf.PerlinNoise(x * 0.004f + o1, 1f) * 6f);
                c = new Color(c.r * m, c.g * m, c.b * m, 1f);

                // Выходы голой породы под гребнем. Порог мягкий и вклад
                // слабый: на резком пороге пятна шума читаются плитами.
                float bare = Mathf.PerlinNoise(x * 0.11f + 13f, y * 0.16f + o1);
                float bareT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.60f, 0.85f, bare))
                            * Mathf.Clamp01(1f - d / 40f);
                if (bareT > 0f) c = Color.Lerp(c, litSide >= 0f ? hi : lo, bareT * 0.45f);

                if (snowCaps && y > snowLine)
                {
                    float t = Mathf.SmoothStep(0f, 1f, (y - snowLine) / (TH * 0.10f));
                    c = Color.Lerp(c, litSide >= 0f ? cap : Color.Lerp(cap, lo, 0.35f), t);
                }

                if (d < 2f) c = Color.Lerp(c, Color.white, 0.25f);    // подсвеченный гребень
                if (forest && d < treeLine) c = Color.Lerp(c, wood, 0.35f);

                // Подошва тонет в дымке: без неё хребет стоял вырезанным из бумаги.
                float mist = Mathf.Clamp01(1f - y / (TH * 0.34f));
                c = Color.Lerp(c, sky, mist * mist * (0.16f + haze * 0.5f));

                pix.Set(x, y, c);

                // Гребень идёт лесенкой: тексель втрое крупнее экранного
                // пикселя. Верхний красим с дробной непрозрачностью — край
                // сглаживается билинейным фильтром.
                if (y == top)
                {
                    float frac = Mathf.Clamp01(tops[x] - top + 0.5f);
                    pix.Set(x, y + 1, new Color(c.r, c.g, c.b, frac));
                }
            }
        }

        if (forest) StampTreeLine(pix, tops, style, rnd, wood, sky, haze);

        float ppu = TW / (DestructibleTerrain.WorldWidth * 2.2f);
        var sr = Sprites.Make("Ridge", pix.ToSprite(ppu, true, FilterMode.Bilinear), Color.white, order, root);
        sr.transform.position = new Vector3(DestructibleTerrain.WorldWidth * 0.5f, footY, 4f);
        sr.transform.localScale = new Vector3(1f, heightUnits / (TH / ppu), 1f);
        ParallaxLayer.Attach(sr.gameObject, parallax);
    }

    /// Лес по гребню: не кайма вдоль силуэта, а частокол крошечных деревьев,
    /// торчащих в небо. Именно он выдаёт масштаб — по нему видно, что хребет
    /// далеко и велик.
    static void StampTreeLine(Pix pix, float[] tops, TerrainStyle style, System.Random rnd,
                              Color wood, Color sky, float haze)
    {
        var dark = Color.Lerp(wood, Color.black, 0.2f);
        var lit = Color.Lerp(wood, Color.white, 0.16f);
        bool conifer = style.Decor == DecorKind.Pine;
        float o = (float)rnd.NextDouble() * 100f;

        for (int x = 4; x < pix.W - 4; x += 2 + rnd.Next(3))
        {
            int top = Mathf.RoundToInt(tops[x]);
            // Выше границы леса деревья не растут, у самой подошвы их не видно.
            if (top > pix.H * (conifer ? 0.55f : 0.72f) || top < 12) continue;
            // Лес растёт куртинами: сплошной частокол вдоль всего хребта
            // читается зелёной гусеницей, а не склоном в деревьях.
            if (Mathf.PerlinNoise(x * 0.014f + o, 2.5f) < 0.45f) continue;

            int hgt = 2 + rnd.Next(4);
            int wid = conifer ? Mathf.Max(1, hgt / 4) : Mathf.Max(1, hgt / 3);
            var c = (float)rnd.NextDouble() < 0.5 ? dark : lit;

            for (int dy = 0; dy < hgt; dy++)
            {
                // Ель сужается к верхушке, лиственное — шар на короткой ножке.
                float t = dy / (float)hgt;
                int half = conifer
                    ? Mathf.CeilToInt(wid * (1f - t))
                    : Mathf.CeilToInt(wid * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 0.85f + 0.15f)));
                for (int dx = -half; dx <= half; dx++)
                    pix.Set(x + dx, top + dy, c);
            }
        }
    }

    /// Ребристый шум: складка вместо холма. Из него собираются гребни.
    static float Ridged(float x, float y)
    {
        float n = Mathf.PerlinNoise(x, y) * 2f - 1f;
        return 1f - Mathf.Abs(n);
    }

    /// Кучевое облако: плоское основание, клубящийся верх, светящаяся
    /// макушка и синеватое подбрюшье. Раньше это была горсть белых кругов.
    static Pix CloudPix(System.Random rnd, Color sky)
    {
        const int W = 160, H = 80;
        float baseY = 22f;
        var den = new float[W * H];

        int puffs = 7 + rnd.Next(6);
        for (int p = 0; p < puffs; p++)
        {
            float t = (p + 0.5f) / puffs;
            // К краям облако ниже: середина вздымается, концы стелются.
            float bell = Mathf.Sin(t * Mathf.PI);
            float cx = 22f + (W - 44f) * t + Rng(rnd, -8f, 8f);
            float cy = baseY + bell * Rng(rnd, 8f, 26f);
            float r = 11f + bell * Rng(rnd, 6f, 18f);

            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + r));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x - cx) / r, dy = (y - cy) / r;
                float q = 1f - (dx * dx + dy * dy);
                if (q > 0f) den[y * W + x] += q * q;
            }
        }

        var pix = new Pix(W, H);
        var under = Color.Lerp(sky, Color.white, 0.52f);
        float o = (float)rnd.NextDouble() * 100f;

        // Плотность после маски основания и клочьев по краю — по ней считаем
        // и прозрачность, и свет.
        var k = new float[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float v = den[y * W + x];
            // Основание кучевого облака срезано ровно — на нём оно и лежит.
            v *= Mathf.Clamp01((y - baseY + 5f) / 6f);
            // Клочья по краю: без шума край выходит циркульным.
            v *= 0.70f + 0.6f * Mathf.PerlinNoise(x * 0.11f + o, y * 0.11f);
            k[y * W + x] = v;
        }

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float v = k[y * W + x];
            // Порог широкий: на узком облако выходило плитой с резаным краем.
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.95f, v));
            if (a <= 0.01f) continue;

            // Свет сверху: там, где над пикселем облака уже нет, клуб освещён;
            // под нависающим клубом — тень. Отсюда и объём.
            float above = y + 6 < H ? k[(y + 6) * W + x] : 0f;
            float lit = Mathf.Clamp01((v - above) * 1.4f + 0.30f);
            float up = Mathf.Clamp01((y - baseY) / 30f);

            var c = Color.Lerp(under, Color.white, Mathf.SmoothStep(0f, 1f, lit * 0.75f + up * 0.35f));
            pix.Set(x, y, new Color(c.r, c.g, c.b, a * 0.95f));
        }
        return pix;
    }

    static void BuildClouds(Transform root, TerrainStyle style, System.Random rnd, float w, float h)
    {
        int n = 6 + rnd.Next(4);
        for (int i = 0; i < n; i++)
        {
            var pix = CloudPix(rnd, style.Sky);
            float far = (float)rnd.NextDouble();                  // 0 — ближе, 1 — дальше
            float scale = 1.0f - 0.45f * far;

            var sr = Sprites.Make("Cloud", pix.ToSprite(17f, false, FilterMode.Bilinear),
                                  new Color(1f, 1f, 1f, 0.88f - 0.35f * far), OrderCloud, root);
            sr.transform.position = new Vector3(
                w * (float)rnd.NextDouble(),
                h * (0.58f + 0.38f * (float)rnd.NextDouble()), 4.5f);
            sr.transform.localScale = Vector3.one * scale;
            // Дрейф медленный и разный: облака не должны маршировать строем.
            ParallaxLayer.Attach(sr.gameObject, 0.78f + 0.12f * far,
                new Vector2(0.20f + 0.45f * (1f - far), 0f), w * 1.5f);
        }
    }

    /// Перистая рвань под самым зенитом: широкие полупрозрачные мазки,
    /// от которых верх неба перестаёт быть пустым.
    static void BuildCirrus(Transform root, TerrainStyle style, System.Random rnd, float w, float h)
    {
        const int W = 256, H = 48;
        var pix = new Pix(W, H);
        float o = (float)rnd.NextDouble() * 100f;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            // Шум растянут по горизонтали в двадцать раз — оттуда и волокна.
            float n = 0.65f * Mathf.PerlinNoise(x * 0.018f + o, y * 0.34f)
                    + 0.35f * Mathf.PerlinNoise(x * 0.06f + o * 2f, y * 0.9f);
            float edge = Mathf.Sin(Mathf.PI * y / (H - 1f));       // к краям полосы сходят на нет
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.78f, n)) * edge * 0.5f;
            if (a > 0.01f) pix.Set(x, y, new Color(1f, 1f, 1f, a));
        }

        // Полоса должна лежать под зенитом, а не накрывать полкарты: считаем
        // масштаб от высоты мира, иначе растянутый шум идёт плитами по небу.
        float ppu = W / (w * 1.8f);
        var sr = Sprites.Make("Cirrus", pix.ToSprite(ppu, false, FilterMode.Bilinear),
                              new Color(1f, 1f, 1f, 0.8f), OrderCirrus, root);
        sr.transform.position = new Vector3(w * 0.5f, h * 0.92f, 5f);
        sr.transform.localScale = new Vector3(1f, h * 0.28f / (H / ppu), 1f);
        ParallaxLayer.Attach(sr.gameObject, 0.88f, new Vector2(0.12f, 0f), w * 1.8f);
    }

    /// Стайка птиц: три-пять галочек под облаками. Мелочь, но небо перестаёт
    /// быть пустой заливкой, а параллакс получает ещё один ярус.
    static void BuildBirds(Transform root, TerrainStyle style, System.Random rnd, float w, float h)
    {
        int flocks = 1 + rnd.Next(2);
        for (int f = 0; f < flocks; f++)
        {
            var pix = new Pix(48, 24);
            var ink = Color.Lerp(style.RidgeNear, style.Sky, 0.35f);
            int n = 3 + rnd.Next(3);
            for (int i = 0; i < n; i++)
            {
                float bx = Rng(rnd, 6f, 42f), by = Rng(rnd, 5f, 19f), r = Rng(rnd, 2.2f, 3.6f);
                pix.Line(bx - r, by, bx, by + r * 0.55f, 1.1f, ink);
                pix.Line(bx, by + r * 0.55f, bx + r, by, 1.1f, ink);
            }

            var sr = Sprites.Make("Birds", pix.ToSprite(7f, false, FilterMode.Bilinear),
                                  new Color(1f, 1f, 1f, 0.75f), OrderCloud, root);
            sr.transform.position = new Vector3(w * Rng(rnd, 0.15f, 0.85f), h * Rng(rnd, 0.6f, 0.85f), 4.4f);
            ParallaxLayer.Attach(sr.gameObject, 0.74f, new Vector2(Rng(rnd, 0.6f, 1.2f), 0f), w * 1.5f);
        }
    }

    /// Своды пещеры: пузырчатая порода на месте неба — то, чем пещера
    /// отличается от улицы сильнее всего. Пузыри мелкие (два-три юнита):
    /// на крупных стена читалась как размазанное пятно, а не как порода.
    static void BuildCavern(Transform root, TerrainStyle style, System.Random rnd, float w, float h)
    {
        const int TW = 1024, TH = 512;
        float ppu = TW / (w * 1.6f);          // сколько текселей в юните
        var pix = new Pix(TW, TH);
        float o = (float)rnd.NextDouble() * 100f;

        // Задник держим тёмным: на ярком фоне не видно ни червей, ни снарядов.
        var deep = (Color)style.Sky;
        var wall = Color.Lerp(style.Sky, style.RidgeFar, 0.55f);
        var rim = Color.Lerp(style.Sky, style.RidgeNear, 0.7f);
        var lit = Color.Lerp(style.Sky, style.RidgeNear, 0.95f);

        // Период шума — примерно три юнита: столько и занимает один пузырь.
        float f1 = 1f / (3f * ppu);
        for (int y = 0; y < TH; y++)
        for (int x = 0; x < TW; x++)
        {
            float n = 0.7f * Mathf.PerlinNoise(x * f1 + o, y * f1 * 1.15f + o)
                    + 0.3f * Mathf.PerlinNoise(x * f1 * 2.7f + 41f, y * f1 * 2.7f + 17f);
            float d = n - 0.5f;
            Color c = d < 0f ? deep : wall;
            if (Mathf.Abs(d) < 0.03f) c = rim;                        // ободок пузыря
            if (d > 0.13f) c = lit;                                   // блик на перемычке

            // Своды глубже — темнее: свет сюда попадает только снизу.
            float depth = Mathf.Clamp01((y - TH * 0.35f) / (TH * 0.65f));
            c = Color.Lerp(c, deep, depth * 0.55f);
            // Зерно, чтобы порода не выглядела залитой.
            float grain = Mathf.PerlinNoise(x * 0.4f + 3f, y * 0.4f) - 0.5f;
            float m = 1f + grain * 0.10f;
            pix.Set(x, y, new Color(c.r * m, c.g * m, c.b * m, 1f));
        }

        var sr = Sprites.Make("Cavern", pix.ToSprite(ppu, false, FilterMode.Bilinear),
                              Color.white, OrderCavern, root);
        sr.transform.position = new Vector3(w * 0.5f, h * 0.55f, 5f);
        sr.transform.localScale = new Vector3(1f, (h * 1.5f) / (TH / ppu), 1f);
        ParallaxLayer.Attach(sr.gameObject, 0.65f);
    }

    // ---------- украшения, впечатанные в ландшафт ----------

    /// Ставит украшения в маску ландшафта. Зовётся из `DestructibleTerrain.Build`
    /// после генерации формы и травы, до текстуры и коллайдеров, — поэтому
    /// дерево с самого начала часть карты, а не декорация поверх неё.
    public static void StampDecor(DestructibleTerrain terrain, int seed)
    {
        var style = terrain.Style;
        var rnd = new System.Random(seed ^ 0x7EE5);
        float w = DestructibleTerrain.WorldWidth;
        int ppu = DestructibleTerrain.PixelsPerUnit;

        // Заготовки двух ярусов: крупные — деревья своего типа, мелкие —
        // подлесок. В кадре оригинала зелени куда больше, чем стволов, и
        // именно она не даёт склону выглядеть насыпанным шумом.
        var trees = new Pix[3];
        for (int i = 0; i < trees.Length; i++) trees[i] = DecorPix(style.Decor, rnd);

        var plants = new Pix[5];
        for (int i = 0; i < plants.Length; i++) plants[i] = PlantPix(style, rnd);

        var small = style.Decor == DecorKind.Crystal ? StonePix(style, rnd) : TuftPix(style, rnd);

        int placed = 0, attempts = 0;
        float lastX = -99f;

        while (placed < style.DecorCount && attempts++ < style.DecorCount * 20)
        {
            float x = 3f + (float)rnd.NextDouble() * (w - 6f);
            // Шаг между украшениями меньше прежнего: подлесок должен стоять
            // группами, а не по одному кусту на десять метров.
            if (Mathf.Abs(x - lastX) < 2.1f) continue;
            if (!FlatSpot(terrain, x, out float y)) continue;

            lastX = x;
            placed++;

            int roll = rnd.Next(10);
            bool tiny = roll < 2;
            var pix = tiny ? small
                    : roll < 6 ? plants[rnd.Next(plants.Length)]
                               : trees[rnd.Next(trees.Length)];

            // Разброс размера и зеркальность: иначе видно, что заготовок четыре.
            float scale = tiny ? 0.8f + 0.4f * (float)rnd.NextDouble()
                               : 0.75f + 0.55f * (float)rnd.NextDouble();

            // Подошву топим на пару пикселей в грунт, чтобы ствол не висел.
            terrain.StampSprite(pix, terrain.WorldToPixelX(x), terrain.WorldToPixelY(y) - 3,
                                scale, rnd.Next(2) == 0);
        }

        // Раз в несколько карт среди зелени попадается ракета, а рядом с ней —
        // горшок с цветком. Одна вещь, сделанная руками, говорит про мир
        // больше, чем ещё десять кустов: сразу видно, что тут кто-то был.
        if (style.Decor != DecorKind.Crystal && rnd.Next(3) == 0)
            StampProp(terrain, rnd, Rocket(rnd), PotPlant(rnd));
    }

    /// Ставит редкую пару «ракета и горшок» на одну площадку: врозь они
    /// теряются, вместе читаются как чей-то брошенный лагерь.
    static void StampProp(DestructibleTerrain terrain, System.Random rnd, Pix prop, Pix mate)
    {
        float w = DestructibleTerrain.WorldWidth;
        for (int attempt = 0; attempt < 60; attempt++)
        {
            float x = 5f + (float)rnd.NextDouble() * (w - 10f);
            if (!FlatSpot(terrain, x, out float y)) continue;

            float side = rnd.Next(2) == 0 ? 1f : -1f;
            float mx = x + side * Rng(rnd, 1.6f, 2.6f);
            if (!FlatSpot(terrain, mx, out float my)) continue;

            terrain.StampSprite(prop, terrain.WorldToPixelX(x), terrain.WorldToPixelY(y) - 3,
                                Rng(rnd, 0.85f, 1.15f), side < 0f);
            terrain.StampSprite(mate, terrain.WorldToPixelX(mx), terrain.WorldToPixelY(my) - 2,
                                Rng(rnd, 0.8f, 1.05f), rnd.Next(2) == 0);
            return;
        }
    }

    /// Ровная площадка под украшение: три пробы подряд на одной высоте и
    /// заметно выше воды. На отвесной стене дерево смотрелось бы приклеенным.
    static bool FlatSpot(DestructibleTerrain terrain, float x, out float y)
    {
        y = terrain.SurfaceHeightWorld(x);
        if (y < 0f) return false;
        if (y < DestructibleTerrain.WaterLevel + 0.8f) return false;
        // На настиле моста пальме расти не с чего.
        if (terrain.IsDeckPixel(terrain.WorldToPixelX(x), terrain.WorldToPixelY(y) - 1)) return false;

        float l = terrain.SurfaceHeightWorld(x - 0.9f);
        float r = terrain.SurfaceHeightWorld(x + 0.9f);
        if (l < 0f || r < 0f) return false;
        return Mathf.Abs(l - y) < 0.9f && Mathf.Abs(r - y) < 0.9f;
    }

    static Pix DecorPix(DecorKind kind, System.Random rnd) => kind switch
    {
        DecorKind.Palm => Palm(rnd),
        DecorKind.Pine => Pine(rnd),
        DecorKind.Cactus => Cactus(rnd),
        DecorKind.Crystal => Crystal(rnd),
        _ => Tree(rnd)
    };

    /// Подлесок под тип мира: в пещере растут грибы, в пустыне — суккуленты
    /// и сухие кусты, в остальных мирах — кусты, папоротники и цветы.
    static Pix PlantPix(TerrainStyle style, System.Random rnd) => style.Decor switch
    {
        DecorKind.Crystal => rnd.Next(2) == 0 ? Mushroom(rnd, true) : StonePix(style, rnd),
        DecorKind.Cactus => rnd.Next(2) == 0 ? Bush(rnd, new Color32(96, 118, 62, 255)) : Flowers(rnd),
        DecorKind.Pine => rnd.Next(2) == 0 ? Bush(rnd, new Color32(46, 104, 66, 255)) : Fern(rnd),
        _ => rnd.Next(3) switch
        {
            0 => Bush(rnd, new Color32(58, 132, 48, 255)),
            1 => Fern(rnd),
            _ => rnd.Next(3) == 0 ? Mushroom(rnd, false) : Flowers(rnd)
        }
    };

    static float Rng(System.Random rnd, float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    /// Лиственное дерево: короткий ствол с парой веток и плотная широкая крона.
    static Pix Tree(System.Random rnd)
    {
        var pix = new Pix(64, 84);
        var bark = new Color32(84, 56, 34, 255);
        var barkLit = new Color32(118, 82, 50, 255);
        var leaf = new Color32(58, 132, 48, 255);
        var leafLit = new Color32(96, 180, 72, 255);

        float cx = 32f, trunk = Rng(rnd, 22f, 30f);
        pix.Line(cx, 0f, cx + Rng(rnd, -2f, 2f), trunk, 8f, bark);
        pix.Line(cx - 2.5f, 4f, cx - 2.5f, trunk - 4f, 3f, barkLit);
        pix.Line(cx, trunk - 8f, cx - 13f, trunk + 8f, 4.5f, bark);
        pix.Line(cx, trunk - 4f, cx + 14f, trunk + 10f, 4.5f, bark);

        // Крона — три ряда перекрывающихся пятен: снизу шире, кверху сходится.
        // Редкие разбросанные шарики читались как кусты на палке.
        float baseY = trunk + 6f;
        for (int row = 0; row < 3; row++)
        {
            float y = baseY + row * 9f;
            float spread = 20f - row * 6f;
            int n = 5 - row;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0f : (i / (float)(n - 1) - 0.5f) * 2f;
                pix.Disc(cx + t * spread + Rng(rnd, -2f, 2f), y + Rng(rnd, -2f, 2f),
                         Rng(rnd, 10f, 13f) - row, leaf);
            }
        }

        // Блики по левому верху кроны — оттуда светит солнце на заднике.
        for (int i = 0; i < 5; i++)
            pix.Disc(cx + Rng(rnd, -14f, 4f), baseY + Rng(rnd, 8f, 22f), Rng(rnd, 4f, 7f), leafLit);

        pix.Outline(new Color32(28, 40, 22, 255));
        return pix;
    }

    /// Пальма: изогнутый ствол, веер листьев и пара кокосов.
    static Pix Palm(System.Random rnd)
    {
        var pix = new Pix(56, 76);
        var bark = new Color32(122, 92, 52, 255);
        var leaf = new Color32(52, 148, 74, 255);
        var leafLit = new Color32(96, 190, 100, 255);

        float bend = Rng(rnd, -8f, 8f);
        float topX = 28f + bend, topY = 52f;
        for (int i = 0; i <= 20; i++)
        {
            float t = i / 20f;
            float x = Mathf.Lerp(28f, topX, t * t);
            pix.Disc(x, t * topY, 3.2f - t * 1.1f, bark);
        }

        for (int i = 0; i < 7; i++)
        {
            float a = Mathf.PI * (0.08f + 0.84f * i / 6f) + Rng(rnd, -0.07f, 0.07f);
            float len = Rng(rnd, 16f, 23f);
            float ex = topX + Mathf.Cos(a) * len;
            float ey = topY + Mathf.Sin(a) * len * 0.75f - 6f;
            pix.Line(topX, topY, (topX + ex) * 0.5f, topY + 7f, 3.4f, leaf);
            pix.Line((topX + ex) * 0.5f, topY + 7f, ex, ey, 2.6f, i % 2 == 0 ? leafLit : leaf);
        }

        pix.Disc(topX - 3f, topY - 3f, 2.4f, new Color32(96, 68, 40, 255));
        pix.Disc(topX + 3f, topY - 5f, 2.4f, new Color32(96, 68, 40, 255));

        pix.Outline(new Color32(24, 52, 30, 255));
        return pix;
    }

    /// Ель: три яруса треугольников со снежными кромками.
    static Pix Pine(System.Random rnd)
    {
        var pix = new Pix(44, 80);
        var bark = new Color32(74, 52, 36, 255);
        var needle = new Color32(34, 92, 62, 255);
        var snow = new Color32(238, 246, 252, 255);

        float cx = 22f;
        pix.Rect((int)cx - 3, 0, 6, 18, bark);

        int tiers = 3 + rnd.Next(2);
        float y = 12f;
        float half = Rng(rnd, 16f, 20f);
        for (int t = 0; t < tiers; t++)
        {
            float top = y + Rng(rnd, 17f, 21f);
            for (float yy = y; yy <= top; yy += 1f)
            {
                float k = 1f - (yy - y) / (top - y);
                float hw = half * k;
                for (float xx = cx - hw; xx <= cx + hw; xx += 1f) pix.Set((int)xx, (int)yy, needle);
                // Снег лежит на нижней кромке яруса, как на полке.
                if (yy < y + 2.5f)
                    for (float xx = cx - hw; xx <= cx + hw; xx += 1f) pix.Set((int)xx, (int)yy, snow);
            }
            y += Rng(rnd, 11f, 14f);
            half *= 0.78f;
        }

        pix.Outline(new Color32(20, 44, 34, 255));
        return pix;
    }

    /// Кактус-канделябр: столб и одна-две руки.
    static Pix Cactus(System.Random rnd)
    {
        var pix = new Pix(44, 60);
        var body = new Color32(66, 122, 60, 255);
        var lit = new Color32(104, 162, 78, 255);
        var spine = new Color32(226, 220, 160, 255);

        float cx = 22f, top = Rng(rnd, 34f, 48f);
        pix.Line(cx, 2f, cx, top, 9f, body);
        pix.Line(cx - 2f, 4f, cx - 2f, top - 3f, 2.4f, lit);

        int arms = 1 + rnd.Next(2);
        for (int i = 0; i < arms; i++)
        {
            float dir = (i == 0) ? -1f : 1f;
            float ay = Rng(rnd, 14f, 26f);
            float reach = Rng(rnd, 9f, 13f);
            pix.Line(cx, ay, cx + dir * reach, ay, 6f, body);
            pix.Line(cx + dir * reach, ay, cx + dir * reach, ay + Rng(rnd, 8f, 15f), 6f, body);
        }

        for (int i = 0; i < 14; i++)
            pix.Set((int)(cx + Rng(rnd, -4f, 4f)), (int)Rng(rnd, 4f, top - 2f), spine);

        pix.Outline(new Color32(26, 52, 30, 255));
        return pix;
    }

    /// Пещерный кристалл: три светящихся осколка в ореоле.
    static Pix Crystal(System.Random rnd)
    {
        var pix = new Pix(56, 76);

        for (int i = 0; i < 3; i++)
        {
            float bx = 28f + Rng(rnd, -13f, 13f);
            float top = Rng(rnd, 26f, 54f);
            float half = Rng(rnd, 4f, 7f);
            var face = new Color32(96, 176, 226, 255);
            var lit = new Color32(170, 232, 255, 255);

            for (float yy = 0; yy <= top; yy += 1f)
            {
                float k = 1f - yy / top;
                float hw = Mathf.Max(1f, half * (0.35f + 0.65f * k));
                for (float xx = bx - hw; xx <= bx + hw; xx += 1f)
                    pix.Set((int)xx, (int)yy, xx < bx ? lit : face);
            }
        }

        // Ореола у кристалла нет: полупрозрачные пиксели в маску не берутся,
        // а светиться сквозь породу нечему — украшение теперь сама порода.
        pix.Outline(new Color32(24, 58, 90, 255));
        return pix;
    }

    /// Куст: горсть перекрывающихся пятен с бликом сверху и парой веточек
    /// снизу. Цвет приходит снаружи — один и тот же куст служит и хвойному
    /// лесу, и пустыне, меняя только листву.
    static Pix Bush(System.Random rnd, Color32 leaf)
    {
        var pix = new Pix(40, 34);
        var lit = (Color32)Color.Lerp((Color)leaf, Color.white, 0.32f);
        var stem = new Color32(84, 62, 38, 255);

        for (int i = 0; i < 3; i++)
            pix.Line(20f + Rng(rnd, -5f, 5f), 0f, 20f + Rng(rnd, -7f, 7f), Rng(rnd, 6f, 11f), 1.8f, stem);

        for (int i = 0; i < 7; i++)
            pix.Disc(20f + Rng(rnd, -12f, 12f), Rng(rnd, 8f, 20f), Rng(rnd, 6f, 9f), leaf);
        for (int i = 0; i < 4; i++)
            pix.Disc(20f + Rng(rnd, -9f, 6f), Rng(rnd, 14f, 24f), Rng(rnd, 3f, 5f), lit);

        pix.Outline(new Color32(24, 38, 20, 255));
        return pix;
    }

    /// Папоротник: несколько дуг из мелких перьев. Растёт куда угодно, а в
    /// кадре читается как трава в человеческий рост — чего пучкам не хватает.
    static Pix Fern(System.Random rnd)
    {
        var pix = new Pix(36, 34);
        var frond = new Color32(52, 122, 56, 255);
        var lit = new Color32(92, 168, 78, 255);

        int n = rnd.Next(4, 7);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * (0.15f + 0.7f * i / (n - 1f)) + Rng(rnd, -0.08f, 0.08f);
            float len = Rng(rnd, 13f, 22f);
            var c = i % 2 == 0 ? frond : lit;
            float px = 18f, py = 2f;
            for (int k = 1; k <= 8; k++)
            {
                float t = k / 8f;
                // Дуга: перо к концу заваливается наружу и мельчает.
                float nx = 18f + Mathf.Cos(a) * len * t;
                float ny = 2f + Mathf.Sin(a) * len * t * (1.35f - 0.5f * t);
                pix.Line(px, py, nx, ny, 2.6f - 1.4f * t, c);
                px = nx; py = ny;
            }
        }

        pix.Outline(new Color32(22, 44, 24, 255));
        return pix;
    }

    /// Цветы: три-пять стеблей с венчиками. Единственное пятно чистого цвета
    /// среди зелени — глаз цепляется за него и склон перестаёт быть фоном.
    static Pix Flowers(System.Random rnd)
    {
        var pix = new Pix(28, 26);
        var stem = new Color32(64, 132, 58, 255);
        Color32[] palette =
        {
            new Color32(232, 96, 104, 255), new Color32(244, 196, 72, 255),
            new Color32(236, 240, 248, 255), new Color32(176, 118, 224, 255)
        };
        var petal = palette[rnd.Next(palette.Length)];
        var heart = new Color32(250, 214, 96, 255);

        int n = rnd.Next(3, 6);
        for (int i = 0; i < n; i++)
        {
            float bx = 14f + Rng(rnd, -8f, 8f);
            float tx = bx + Rng(rnd, -3f, 3f), ty = Rng(rnd, 11f, 20f);
            pix.Line(bx, 0f, tx, ty, 1.7f, stem);
            pix.Disc(bx + (tx - bx) * 0.5f + Rng(rnd, -3f, 3f), ty * 0.5f, 2.4f, stem);

            for (int k = 0; k < 5; k++)
            {
                float a = Mathf.PI * 2f * k / 5f;
                pix.Disc(tx + Mathf.Cos(a) * 2.6f, ty + Mathf.Sin(a) * 2.6f, 2.1f, petal);
            }
            pix.Disc(tx, ty, 1.4f, heart);
        }

        pix.Outline(new Color32(26, 44, 26, 220));
        return pix;
    }

    /// Гриб: ножка и шляпка в крапинку. В пещере светится, наверху — обычная
    /// поганка под деревьями.
    static Pix Mushroom(System.Random rnd, bool glowing)
    {
        var pix = new Pix(24, 22);
        var stem = glowing ? new Color32(196, 214, 232, 255) : new Color32(226, 214, 186, 255);
        var cap = glowing ? new Color32(96, 214, 226, 255) : new Color32(196, 74, 62, 255);
        var dot = glowing ? new Color32(226, 250, 255, 255) : new Color32(240, 234, 216, 255);

        float cx = 12f, top = Rng(rnd, 9f, 13f);
        pix.Line(cx, 0f, cx + Rng(rnd, -1.5f, 1.5f), top, 3.4f, stem);
        pix.Disc(cx, top, Rng(rnd, 6f, 8f), cap);
        pix.Rect(Mathf.RoundToInt(cx) - 7, 0, 14, Mathf.RoundToInt(top) - 1, Pix.Clear);
        pix.Line(cx, 0f, cx, top, 3.4f, stem);
        for (int i = 0; i < 3; i++)
            pix.Disc(cx + Rng(rnd, -4f, 4f), top + Rng(rnd, 0f, 4f), Rng(rnd, 1f, 1.8f), dot);

        pix.Outline(new Color32(40, 30, 26, 255));
        return pix;
    }

    /// Ракета: жестяной корпус, окно, красный нос и три ноги. Стоит редко и
    /// поодиночке — это не украшение склона, а находка.
    static Pix Rocket(System.Random rnd)
    {
        var pix = new Pix(34, 72);
        var hull = new Color32(206, 210, 220, 255);
        var shade = new Color32(150, 156, 172, 255);
        var nose = new Color32(198, 62, 52, 255);
        var glass = new Color32(96, 176, 226, 255);

        float cx = 17f, legs = 9f, body = 46f;

        // Ноги врастопырку — ракета стоит, а не воткнута в грунт.
        pix.Line(cx - 2f, legs + 2f, cx - 9f, 0f, 3f, shade);
        pix.Line(cx + 2f, legs + 2f, cx + 9f, 0f, 3f, shade);
        pix.Line(cx, legs + 2f, cx, 0f, 3f, shade);

        pix.Rect(Mathf.RoundToInt(cx) - 7, Mathf.RoundToInt(legs), 14, Mathf.RoundToInt(body), hull);
        pix.Rect(Mathf.RoundToInt(cx) + 2, Mathf.RoundToInt(legs), 5, Mathf.RoundToInt(body), shade);

        // Нос — сходящиеся к макушке ряды.
        for (int i = 0; i <= 12; i++)
        {
            float t = i / 12f;
            int half = Mathf.RoundToInt(7f * (1f - t * t));
            pix.Rect(Mathf.RoundToInt(cx) - half, Mathf.RoundToInt(legs + body) + i, half * 2 + 1, 1, nose);
        }

        // Стабилизаторы по бокам корпуса.
        pix.Line(cx - 7f, legs + 12f, cx - 12f, legs + 1f, 3.2f, nose);
        pix.Line(cx + 7f, legs + 12f, cx + 12f, legs + 1f, 3.2f, nose);

        pix.Disc(cx, legs + body * 0.62f, 4.2f, new Color32(70, 78, 96, 255));
        pix.Disc(cx, legs + body * 0.62f, 3f, glass);
        pix.Disc(cx - 1f, legs + body * 0.62f + 1f, 1.2f, new Color32(206, 236, 255, 255));

        // Пара заклёпочных поясов: без них корпус — просто серый прямоугольник.
        pix.Rect(Mathf.RoundToInt(cx) - 7, Mathf.RoundToInt(legs + body * 0.25f), 14, 2, shade);
        pix.Rect(Mathf.RoundToInt(cx) - 7, Mathf.RoundToInt(legs + body * 0.85f), 14, 2, shade);

        pix.Outline(new Color32(38, 40, 50, 255));
        return pix;
    }

    /// Горшок с цветком — спутник ракеты: обожжённая глина, земля и стебель.
    static Pix PotPlant(System.Random rnd)
    {
        var pix = new Pix(28, 40);
        var clay = new Color32(186, 106, 66, 255);
        var clayLit = new Color32(216, 142, 96, 255);
        var soil = new Color32(74, 54, 40, 255);
        var stem = new Color32(64, 132, 58, 255);
        var petal = rnd.Next(2) == 0 ? new Color32(232, 96, 104, 255) : new Color32(244, 196, 72, 255);

        // Горшок: книзу уже, сверху — венчик.
        for (int y = 0; y < 15; y++)
        {
            int half = Mathf.RoundToInt(Mathf.Lerp(6f, 9f, y / 14f));
            pix.Rect(14 - half, y, half * 2, 1, y % 7 == 0 ? clayLit : clay);
        }
        pix.Rect(3, 15, 22, 3, clayLit);
        pix.Rect(5, 17, 18, 2, soil);

        float top = Rng(rnd, 28f, 34f);
        pix.Line(14f, 18f, 14f + Rng(rnd, -3f, 3f), top, 2f, stem);
        pix.Disc(11f, 24f, 3.2f, stem);
        pix.Disc(17f, 27f, 2.8f, stem);
        for (int k = 0; k < 5; k++)
        {
            float a = Mathf.PI * 2f * k / 5f;
            pix.Disc(14f + Mathf.Cos(a) * 3f, top + Mathf.Sin(a) * 3f, 2.4f, petal);
        }
        pix.Disc(14f, top, 1.6f, new Color32(250, 214, 96, 255));

        pix.Outline(new Color32(48, 30, 24, 255));
        return pix;
    }

    /// Пучок травы — мелочь между крупными украшениями.
    static Pix TuftPix(TerrainStyle style, System.Random rnd)
    {
        var pix = new Pix(20, 16);
        var c = (Color32)Color.Lerp((Color)style.Grass, Color.black, 0.25f);
        for (int i = 0; i < 6; i++)
        {
            float bx = 10f + Rng(rnd, -5f, 5f);
            pix.Line(bx, 0f, bx + Rng(rnd, -4f, 4f), Rng(rnd, 6f, 13f), 1.6f, c);
        }
        pix.Outline(new Color32(20, 30, 16, 200));
        return pix;
    }

    /// Валун — мелочь для пещеры, где трава не растёт.
    static Pix StonePix(TerrainStyle style, System.Random rnd)
    {
        var pix = new Pix(22, 16);
        var body = (Color32)Color.Lerp((Color)style.Rock, Color.white, 0.18f);
        var lit = (Color32)Color.Lerp((Color)style.Rock, Color.white, 0.42f);
        pix.Disc(11f, 6f, 7f, body);
        pix.Disc(8f, 8f, 3.5f, lit);
        pix.Disc(15f, 5f, 3f, body);
        pix.Rect(2, 0, 18, 4, body);
        pix.Outline((Color32)Color.Lerp((Color)style.Rock, Color.black, 0.55f));
        return pix;
    }
}
