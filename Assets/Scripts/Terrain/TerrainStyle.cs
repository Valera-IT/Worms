using UnityEngine;

/// Чем зарастает поверхность мира. Рисует эти украшения Scenery.
public enum DecorKind { Tree, Palm, Pine, Cactus, Crystal }

/// Что за спиной: открытое небо с горами и облаками или своды пещеры.
public enum BackdropKind { Sky, Cavern }

/// Всё, чем один тип мира отличается от другого: форма (её строит TerrainShape),
/// палитра породы и травы, уровень воды, цвет неба и воды.
/// Раньше палитра лежала статикой в RepaintColumns, а вода была константой.
public class TerrainStyle
{
    public TerrainKind Kind;
    public string Name;

    // Палитра. Порода мешается с землёй по шуму, трава запекается один раз.
    public Color32 DirtA, DirtB, Rock, Grass, GrassDark, Scorch;

    /// Толщина верхнего слоя травы (мха, снега) в пикселях.
    public int GrassDepth = 5;

    /// Доля высоты карты, ниже которой всё — сплошная порода.
    /// Граница не прямая: её ведёт шум, иначе видно линейку поперёк карты.
    public float RockBand = 0.035f;

    /// Порог шума для вкраплений породы в земле. 1 — вкраплений нет.
    public float RockNoise = 0.74f;

    /// Уровень воды в юнитах. Ноль и меньше — воды в этом мире нет.
    public float WaterLevel = 5.5f;
    public bool HasWater => WaterLevel > 0f;

    public Color Sky, Water;

    // ---------- задник и украшения (их ставит Scenery) ----------

    /// Небо — градиент от зенита к горизонту: плоская заливка выдавала прототип.
    public Color SkyTop;

    /// Два хребта на заднем плане: дальний бледнее и движется медленнее.
    public Color RidgeFar, RidgeNear;

    public BackdropKind Backdrop = BackdropKind.Sky;
    public DecorKind Decor = DecorKind.Tree;

    /// Сколько украшений ставить на карту шириной в мир и есть ли облака.
    /// Считается вместе с подлеском: деревьев из этого числа меньше половины,
    /// остальное — кусты, папоротники и цветы, которых в кадре должно быть
    /// заметно больше, чем стволов.
    public int DecorCount = 26;
    public bool Clouds = true;

    // Оттенки для фактуры породы: тёмная кромка комков и блик на их макушке.
    public Color32 DirtEdge, DirtHi;

    /// Светлая корка по всему контуру суши — не только сверху, но и по бокам,
    /// под навесами и по своду каверн. На картах оригинала она обводит массив
    /// целиком, и именно она делает силуэт читаемым.
    /// Ноль в альфе — цвет считается из палитры.
    public Color32 Crust;

    /// Толщина корки в пикселях. Ноль — корки в этом мире нет.
    public int CrustDepth = 3;

    /// Цвет корки: заданный стилем или светлый оттенок земли.
    public Color32 CrustColor => Crust.a > 0 ? Crust : (Color32)Color.Lerp(DirtA, DirtHi, 0.75f);

    /// Случайный тип раскрывается из того же сида, что и рельеф, —
    /// «новая карта» каждый раз даёт другой мир.
    public static TerrainKind Resolve(TerrainKind kind, int seed)
    {
        if (kind != TerrainKind.Random) return kind;
        return (TerrainKind)(new System.Random(seed ^ 0x5EED).Next(0, 5));
    }

    public static TerrainStyle For(TerrainKind kind) => kind switch
    {
        TerrainKind.Cave => new TerrainStyle
        {
            Kind = kind,
            Name = "Пещера",
            DirtA = new Color32(74, 70, 78, 255),
            DirtB = new Color32(60, 57, 65, 255),
            Rock = new Color32(46, 44, 52, 255),
            Grass = new Color32(72, 112, 68, 255),        // мох
            GrassDark = new Color32(52, 84, 52, 255),
            Scorch = new Color32(28, 26, 30, 255),
            GrassDepth = 3,
            RockBand = 0.05f,
            RockNoise = 0.72f,
            WaterLevel = -4f,                              // топиться негде
            Sky = new Color(0.05f, 0.05f, 0.07f),
            Water = new Color(0.10f, 0.14f, 0.22f, 0.75f)            ,
            SkyTop = new Color(0.10f, 0.05f, 0.14f),
            RidgeFar = new Color(0.20f, 0.11f, 0.26f),
            RidgeNear = new Color(0.30f, 0.14f, 0.34f),
            Backdrop = BackdropKind.Cavern,
            Decor = DecorKind.Crystal,
            DecorCount = 24,
            Clouds = false,
            DirtEdge = new Color32(34, 32, 40, 255),
            DirtHi = new Color32(96, 92, 102, 255),
            Crust = new Color32(158, 100, 190, 255),       // холодное свечение по кромке
            CrustDepth = 4
        },

        TerrainKind.Archipelago => new TerrainStyle
        {
            Kind = kind,
            Name = "Архипелаг",
            DirtA = new Color32(214, 186, 130, 255),
            DirtB = new Color32(196, 166, 112, 255),
            Rock = new Color32(150, 128, 96, 255),
            Grass = new Color32(84, 176, 96, 255),
            GrassDark = new Color32(56, 132, 74, 255),
            Scorch = new Color32(96, 78, 52, 255),
            GrassDepth = 4,
            RockBand = 0.02f,
            RockNoise = 0.82f,
            WaterLevel = 8f,                               // воды больше, островки ниже
            Sky = new Color(0.58f, 0.80f, 0.94f),
            Water = new Color(0.16f, 0.66f, 0.70f, 0.72f)            ,
            SkyTop = new Color(0.24f, 0.55f, 0.88f),
            RidgeFar = new Color(0.46f, 0.66f, 0.80f),
            RidgeNear = new Color(0.28f, 0.50f, 0.62f),
            Decor = DecorKind.Palm,
            DecorCount = 18,
            DirtEdge = new Color32(150, 118, 74, 255),
            DirtHi = new Color32(236, 214, 168, 255),
            Crust = new Color32(252, 236, 190, 255),       // выбеленный солнцем песок
            CrustDepth = 6
        },

        TerrainKind.Canyon => new TerrainStyle
        {
            Kind = kind,
            Name = "Каньон",
            DirtA = new Color32(198, 112, 58, 255),
            DirtB = new Color32(172, 92, 46, 255),
            Rock = new Color32(138, 76, 44, 255),
            Grass = new Color32(140, 156, 70, 255),        // редкая сухая трава
            GrassDark = new Color32(108, 124, 54, 255),
            Scorch = new Color32(78, 44, 26, 255),
            GrassDepth = 3,
            RockBand = 0.24f,                              // дно пропастей — голый камень
            RockNoise = 0.72f,
            WaterLevel = 4f,
            Sky = new Color(0.86f, 0.72f, 0.55f),
            Water = new Color(0.24f, 0.42f, 0.62f, 0.75f)            ,
            SkyTop = new Color(0.58f, 0.44f, 0.52f),
            RidgeFar = new Color(0.70f, 0.45f, 0.34f),      // столовые горы держат цвет породы:
            RidgeNear = new Color(0.52f, 0.28f, 0.20f),     // на сером они тонули в дымке
            Decor = DecorKind.Cactus,
            DecorCount = 24,
            DirtEdge = new Color32(112, 56, 30, 255),
            DirtHi = new Color32(224, 146, 84, 255),
            Crust = new Color32(255, 178, 66, 255),        // раскалённая кромка
            CrustDepth = 6
        },

        TerrainKind.Snow => new TerrainStyle
        {
            Kind = kind,
            Name = "Снежные холмы",
            DirtA = new Color32(126, 138, 156, 255),
            DirtB = new Color32(108, 120, 140, 255),
            Rock = new Color32(84, 96, 116, 255),
            Grass = new Color32(240, 246, 252, 255),       // снежная шапка
            GrassDark = new Color32(214, 228, 244, 255),
            Scorch = new Color32(96, 100, 112, 255),
            GrassDepth = 14,                               // шапка толстая
            RockBand = 0.06f,
            RockNoise = 0.70f,
            WaterLevel = 5.5f,
            Sky = new Color(0.74f, 0.82f, 0.90f),
            Water = new Color(0.30f, 0.52f, 0.68f, 0.72f)            ,
            SkyTop = new Color(0.42f, 0.55f, 0.76f),
            RidgeFar = new Color(0.62f, 0.70f, 0.84f),
            RidgeNear = new Color(0.40f, 0.50f, 0.68f),
            Decor = DecorKind.Pine,
            DecorCount = 24,
            DirtEdge = new Color32(70, 80, 98, 255),
            DirtHi = new Color32(168, 182, 202, 255),
            Crust = new Color32(230, 242, 252, 255),       // лёд по срезу
            CrustDepth = 4
        },

        _ => new TerrainStyle
        {
            Kind = TerrainKind.Island,
            Name = "Остров",
            DirtA = new Color32(126, 84, 48, 255),
            DirtB = new Color32(101, 66, 38, 255),
            Rock = new Color32(78, 62, 52, 255),
            Grass = new Color32(96, 168, 62, 255),
            GrassDark = new Color32(64, 122, 44, 255),
            Scorch = new Color32(58, 42, 30, 255),
            GrassDepth = 5,
            RockBand = 0.035f,
            RockNoise = 0.74f,
            WaterLevel = 5.5f,
            Sky = new Color(0.55f, 0.74f, 0.93f),
            Water = new Color(0.2f, 0.45f, 0.8f, 0.75f)            ,
            SkyTop = new Color(0.20f, 0.48f, 0.86f),
            RidgeFar = new Color(0.44f, 0.60f, 0.78f),
            RidgeNear = new Color(0.26f, 0.44f, 0.58f),
            Decor = DecorKind.Tree,
            DecorCount = 22,
            DirtEdge = new Color32(64, 40, 22, 255),
            DirtHi = new Color32(158, 112, 66, 255),
            Crust = new Color32(214, 166, 100, 255),       // светлый срез земли
            CrustDepth = 7
        }
    };
}
