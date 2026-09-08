using UnityEngine;

/// Мини-эффекты без систем частиц: короткоживущие спрайты.
public static class Fx
{
    /// Взрыв: вспышка в три кадра, дым, искры и обломки. Круг, который
    /// раздувался и таял, читался отладочным маркером — у него нет ни
    /// сердцевины, ни рваной кромки, ни следа после себя.
    /// soft — попадание одной пули очереди: та же вспышка, но втрое меньше
    /// мусора. Восемь пуль узи иначе выбросили бы за полсекунды три сотни
    /// спрайтов разом — на телефоне это видно счётчиком кадров.
    public static void Explosion(Vector2 pos, float radius, bool soft = false)
    {
        // Кадр вспышки — два юнита шириной, поэтому масштаб задаём от радиуса.
        // Разгорание чуть меньше воронки, затухание чуть больше: огонь выходит
        // за край ямы, как в оригинале.
        var sr = Sprites.Make("Boom", BlastSprite.Flash[0], Color.white, 20, GameManager.Root);
        sr.transform.position = pos;
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        sr.gameObject.AddComponent<Burst>().Init(sr, BlastSprite.Flash, 17f, radius * 1.25f);

        // Дым: несколько клубков из воронки, каждый со своим кадром — иначе
        // из точки поднимался бы столбик одинаковых шариков.
        int puffs = soft ? 2 : Mathf.Clamp(Mathf.RoundToInt(radius * 2.2f), 4, 10);
        for (int i = 0; i < puffs; i++)
            Puff(pos + Random.insideUnitCircle * radius * 0.55f,
                 radius * Random.Range(0.4f, 0.75f), Random.Range(0.7f, 1.5f));

        Sparks(pos, Vector2.up, soft ? 3 : Mathf.Clamp(Mathf.RoundToInt(radius * 3f), 5, 14), 4f + radius * 2.5f);

        int bits = soft ? 5 : 14;
        for (int i = 0; i < bits; i++)
        {
            var d = Random.insideUnitCircle.normalized;
            var p = Sprites.Make("Debris", Sprites.Square, new Color(0.45f, 0.3f, 0.18f), 19, GameManager.Root);
            p.transform.position = pos + d * Random.Range(0.2f, radius * 0.5f);
            p.transform.localScale = Vector3.one * Random.Range(0.12f, 0.3f);
            var fp = p.gameObject.AddComponent<FlyingBit>();
            fp.Velocity = d * Random.Range(4f, 12f) + Vector2.up * 3f;
            fp.Life = Random.Range(0.5f, 1.1f);
        }
    }

    /// Искры: короткие тёплые точки, летящие веером вдоль dir. Ими бьёт
    /// рикошет гранаты и рассыпается взрыв.
    public static void Sparks(Vector2 pos, Vector2 dir, int count = 6, float speed = 8f)
    {
        Vector2 axis = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up;
        for (int i = 0; i < count; i++)
        {
            var d = (axis + Random.insideUnitCircle * 0.8f).normalized;
            var p = Sprites.Make("Spark", BlastSprite.Spark, new Color(1f, 0.86f, 0.55f), 21, GameManager.Root);
            p.transform.position = pos + d * 0.08f;
            p.transform.localScale = Vector3.one * Random.Range(0.55f, 1.15f);
            var fp = p.gameObject.AddComponent<FlyingBit>();
            fp.Velocity = d * Random.Range(speed * 0.45f, speed);
            fp.Life = Random.Range(0.16f, 0.4f);
            fp.Spin = 0f;      // искра — точка, вертеть её нечего
            fp.Fade = true;
        }
    }

    /// Клуб дыма от взрыва: уходит вверх, растёт и редеет, снося по ветру.
    /// Живёт дольше вспышки нарочно — по дыму видно, куда только что попали.
    public static void Puff(Vector2 pos, float size, float life)
    {
        var sr = Sprites.Make("Puff", BlastSprite.Puff[Random.Range(0, BlastSprite.Puff.Length)],
                              Color.white, 18, GameManager.Root);
        sr.transform.position = pos;
        var s = sr.gameObject.AddComponent<SmokePuff>();
        s.Init(sr, size, size * Random.Range(1.6f, 2.3f), life, Random.Range(0.5f, 1.4f));
    }

    public static void Splash(Vector2 pos) => Splash(pos, new Color(0.4f, 0.7f, 1f), 10, 0.25f);

    public static void Splash(Vector2 pos, Color color, int count, float scale)
    {
        for (int i = 0; i < count; i++)
        {
            var d = (Vector2.up + Random.insideUnitCircle * 0.9f).normalized;
            var p = Sprites.Make("Drop", Sprites.Circle, color, 19, GameManager.Root);
            p.transform.position = pos;
            p.transform.localScale = Vector3.one * scale * Random.Range(0.6f, 1.2f);
            var fp = p.gameObject.AddComponent<FlyingBit>();
            fp.Velocity = d * Random.Range(3f, 8f);
            fp.Life = Random.Range(0.4f, 0.9f);
        }
    }

    /// Дымный след ракеты: тот же клуб, только светлее, мельче и почти без
    /// подъёма — он должен лежать на траектории, а не всплывать с неё.
    /// Порядок ниже снаряда — дым идёт за ракетой, а не поверх неё.
    public static void Smoke(Vector2 pos, float size)
    {
        var sr = Sprites.Make("Smoke", BlastSprite.Puff[Random.Range(0, BlastSprite.Puff.Length)],
                              new Color(0.97f, 0.97f, 0.99f), 3, GameManager.Root);
        sr.transform.position = pos;
        var s = sr.gameObject.AddComponent<SmokePuff>();
        s.Init(sr, size, size * 2.2f, 0.9f, 0.25f);
    }

    /// Пузыри тонущего червя: поднимаются к поверхности и там лопаются.
    public static void Bubbles(Vector2 pos, int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            var p = Sprites.Make("Bubble", Sprites.Circle, new Color(0.78f, 0.92f, 1f, 0.65f), 19, GameManager.Root);
            p.transform.position = pos + Random.insideUnitCircle * 0.45f;
            p.transform.localScale = Vector3.one * Random.Range(0.09f, 0.22f);
            var r = p.gameObject.AddComponent<Riser>();
            r.Speed = Random.Range(1.6f, 3.2f);
            r.Life = Random.Range(0.6f, 1.4f);
        }
    }

    public static void FloatingText(Vector2 pos, string text, Color color)
    {
        var go = new GameObject("FloatText");
        GameManager.Attach(go);
        go.transform.position = pos;
        var ft = go.AddComponent<FloatingLabel>();
        ft.Text = text;
        ft.Color = color;
    }
}

public class FadeOut : MonoBehaviour
{
    SpriteRenderer _sr;
    float _from, _to, _dur, _t;

    public void Init(SpriteRenderer sr, float from, float to, float dur)
    {
        _sr = sr; _from = from; _to = to; _dur = dur;
        sr.transform.localScale = Vector3.one * from;
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _dur);
        transform.localScale = Vector3.one * Mathf.Lerp(_from, _to, k);
        var c = _sr.color; c.a = 1f - k; _sr.color = c;
        if (k >= 1f) Destroy(gameObject);
    }
}

public class FlyingBit : MonoBehaviour
{
    public Vector2 Velocity;
    public float Life = 1f;
    /// Комок земли кувыркается, искра — нет: у точки вращать нечего.
    public float Spin = 240f;
    /// Гаснуть к концу жизни, а не пропадать разом. Обломкам это ни к чему —
    /// они падают и их не видно, — а искра обязана истаять.
    public bool Fade;

    SpriteRenderer _sr;
    float _alpha;
    float _t;

    void Start()
    {
        if (!Fade) return;
        _sr = GetComponent<SpriteRenderer>();
        if (_sr != null) _alpha = _sr.color.a;
    }

    void Update()
    {
        _t += Time.deltaTime;
        Velocity += Physics2D.gravity * Time.deltaTime;
        transform.position += (Vector3)(Velocity * Time.deltaTime);
        if (Spin != 0f) transform.Rotate(0, 0, Spin * Time.deltaTime);
        if (_sr != null)
        {
            var c = _sr.color;
            c.a = _alpha * (1f - Mathf.Clamp01(_t / Life));
            _sr.color = c;
        }
        if (_t >= Life) Destroy(gameObject);
    }
}

/// Одиночный проход по кадрам с ростом: вспышка взрыва. От Flipbook отличается
/// тем, что не петля и убирает себя, досмотрев последний кадр, — держать
/// объект дальше незачем.
public class Burst : MonoBehaviour
{
    SpriteRenderer _sr;
    Sprite[] _frames;
    float _fps, _scale, _t;

    /// scale — во сколько юнитов разворачивается кадр в два юнита шириной.
    /// Первый кадр меньше воронки, последний больше: огонь разгорается
    /// изнутри и выходит за её край.
    public void Init(SpriteRenderer sr, Sprite[] frames, float fps, float scale)
    {
        _sr = sr; _frames = frames; _fps = fps; _scale = scale;
        sr.sprite = frames[0];
        sr.transform.localScale = Vector3.one * (scale * 0.72f);
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t * _fps / _frames.Length);
        int i = Mathf.Min(_frames.Length - 1, Mathf.FloorToInt(_t * _fps));
        _sr.sprite = _frames[i];
        transform.localScale = Vector3.one * (_scale * Mathf.Lerp(0.72f, 1.15f, k));
        if (k >= 1f) Destroy(gameObject);
    }
}

/// Клуб дыма: всплывает, расходится и редеет. Гравитация ему не нужна, ветер —
/// нужен: дым сносит туда же, куда и снаряды, и по нему читается его сила.
public class SmokePuff : MonoBehaviour
{
    SpriteRenderer _sr;
    float _from, _to, _life, _rise, _t;
    float _drift;

    static readonly Color Warm = new Color(0.82f, 0.78f, 0.74f);
    static readonly Color Cold = new Color(0.62f, 0.63f, 0.68f);

    public void Init(SpriteRenderer sr, float from, float to, float life, float rise)
    {
        _sr = sr; _from = from; _to = to; _life = life; _rise = rise;
        _drift = Random.Range(-0.25f, 0.25f);
        sr.transform.localScale = Vector3.one * from;
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);

        // Растёт быстро в начале и почти стоит в конце, а редеет наоборот:
        // так клуб расходится, а не выключается.
        transform.localScale = Vector3.one * Mathf.Lerp(_from, _to, Mathf.Sqrt(k));

        float wind = GameManager.I != null ? GameManager.I.Wind : 0f;
        var p = transform.position;
        p.y += _rise * (1f - k * 0.6f) * Time.deltaTime;
        p.x += (wind * 0.9f + _drift) * Time.deltaTime;
        transform.position = p;

        var c = Color.Lerp(Warm, Cold, k);
        c.a = Mathf.Pow(1f - k, 1.6f) * 0.75f;
        _sr.color = c;

        if (k >= 1f) Destroy(gameObject);
    }
}

/// Всплывающее число урона. Двигается и живёт само, а рисует его метку `Hud`
/// в общей панели UI Toolkit — здесь остаётся только положение и прозрачность.
public class FloatingLabel : MonoBehaviour
{
    public string Text;
    public Color Color = Color.white;
    public float Life = 1.2f;
    float _t;

    public float Alpha => 1f - Mathf.Clamp01(_t / Life);

    // Регистрируемся в Start, а не OnEnable: к этому моменту Fx уже проставил Text и Color.
    void Start()
    {
        if (Hud.I != null) Hud.I.RegisterFloater(this);
    }

    void OnDisable()
    {
        if (Hud.I != null) Hud.I.UnregisterFloater(this);
    }

    void Update()
    {
        _t += Time.deltaTime;
        transform.position += Vector3.up * (1.6f * Time.deltaTime);
        if (_t >= Life) Destroy(gameObject);
    }
}

/// Пузырёк: идёт вверх, слегка виляя, и исчезает на поверхности воды или по
/// истечении жизни. Гравитация ему не нужна — тем и отличается от FlyingBit.
public class Riser : MonoBehaviour
{
    public float Speed = 2f;
    public float Life = 1f;
    float _t;
    float _phase;

    void Start() => _phase = Random.value * 10f;

    void Update()
    {
        _t += Time.deltaTime;
        var p = transform.position;
        p.y += Speed * Time.deltaTime;
        p.x += Mathf.Sin(_phase + _t * 7f) * 0.5f * Time.deltaTime;
        transform.position = p;
        if (_t >= Life || p.y > DestructibleTerrain.WaterLevel) Destroy(gameObject);
    }
}
