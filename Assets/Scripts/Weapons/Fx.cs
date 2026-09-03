using UnityEngine;

/// Мини-эффекты без систем частиц: короткоживущие спрайты.
public static class Fx
{
    public static void Explosion(Vector2 pos, float radius)
    {
        var sr = Sprites.Make("Boom", Sprites.Circle, new Color(1f, 0.85f, 0.35f, 0.95f), 20, GameManager.Root);
        sr.transform.position = pos;
        sr.gameObject.AddComponent<FadeOut>().Init(sr, radius * 0.6f, radius * 2.3f, 0.35f);

        for (int i = 0; i < 14; i++)
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
    float _t;

    void Update()
    {
        _t += Time.deltaTime;
        Velocity += Physics2D.gravity * Time.deltaTime;
        transform.position += (Vector3)(Velocity * Time.deltaTime);
        transform.Rotate(0, 0, 240f * Time.deltaTime);
        if (_t >= Life) Destroy(gameObject);
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
