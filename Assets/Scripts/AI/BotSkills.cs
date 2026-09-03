/// Что на самом деле означает сложность. Раньше она была только дрожанием рук:
/// лёгкий бот считал ту же задачу теми же средствами и просто мазал. Это плохая
/// сложность — она не меняет игру, а портит её случайностью, и по лёгкому боту
/// не видно, чего он не умеет.
///
/// Теперь сложность — набор умений. Лёгкий бот стреляет из того, что не
/// кончается, не прячется после выстрела и не ходит за припасами; средний
/// пользуется всем арсеналом и бережёт себя; трудный вдобавок помнит промахи,
/// ведёт ракету и умеет верёвку. Дрожание осталось, но стало последней из
/// разниц, а не единственной.
public struct BotSkills
{
    // --- тонкость перебора ---
    public float AngleStep;     // шаг по углу в грубом проходе, градусы
    public float ChargeStep;    // шаг по силе в грубом проходе, доля
    public float TuneStep;      // шаг точного прохода по углу, градусы
    public float AngleJitter;   // разброс наведения, градусы
    public float ChargeJitter;  // разброс набора силы, доля

    // --- чем воюет ---
    public bool Drops;          // динамит, мина, овца
    public bool Strikes;        // налёт
    public bool Homing;         // самонаводящаяся ракета
    public bool Teleport;       // перенос к цели
    public bool Rope;           // верёвка: качнуться на соседний остров

    // --- как себя ведёт ---
    public bool Crates;         // сходит за аптечкой
    public bool Climbs;         // вылезает из воды
    public bool Flees;          // уходит от своей воронки в окне отхода
    public bool Memory;         // помнит, чем уже мазал с этого места

    /// Насколько тонко бот выбирает цель: добить раненого, накрыть кучу,
    /// столкнуть в воду. 0 — считает только урон, 1 — считает всё.
    public float Focus;

    public static BotSkills For(BotDifficulty d) => d switch
    {
        BotDifficulty.Easy => new BotSkills
        {
            AngleStep = 14f, ChargeStep = 0.14f, TuneStep = 2.4f,
            AngleJitter = 5.5f, ChargeJitter = 0.09f,
            Drops = false, Strikes = false, Homing = false, Teleport = false, Rope = false,
            Crates = false, Climbs = true, Flees = false, Memory = false,
            Focus = 0f
        },
        BotDifficulty.Hard => new BotSkills
        {
            AngleStep = 10f, ChargeStep = 0.11f, TuneStep = 1.5f,
            AngleJitter = 0.5f, ChargeJitter = 0.012f,
            Drops = true, Strikes = true, Homing = true, Teleport = true, Rope = true,
            Crates = true, Climbs = true, Flees = true, Memory = true,
            Focus = 1f
        },
        _ => new BotSkills
        {
            AngleStep = 10f, ChargeStep = 0.11f, TuneStep = 2.4f,
            AngleJitter = 1.8f, ChargeJitter = 0.035f,
            Drops = true, Strikes = true, Homing = false, Teleport = true, Rope = false,
            Crates = true, Climbs = true, Flees = true, Memory = true,
            Focus = 0.6f
        }
    };
}
