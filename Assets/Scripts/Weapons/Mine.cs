using UnityEngine;

/// Мина: лежит на карте между ходами и рвётся, когда рядом проходит червь.
/// В отличие от снаряда, она не держит ход — иначе матч встал бы навсегда.
public class Mine : MonoBehaviour
{
    Weapon _weapon;
    SpriteRenderer _sr;
    float _arm = 3f;          // три секунды взвода: столько свой червь бежит прочь
    float _countdown = -1f;   // пошёл отсчёт после того, как её задели

    const float Trigger = 1.6f;
    const float Delay = 0.9f;

    public static Mine Drop(Weapon w, Vector2 pos)
    {
        var go = new GameObject("Mine");
        GameManager.Attach(go);
        go.transform.position = pos;

        var sr = Sprites.Make("Body", WeaponIcons.Sprite(w.Kind), Color.white, 6, go.transform);
        sr.transform.localScale = Vector3.one * 0.7f;

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.22f;
        col.sharedMaterial = new PhysicsMaterial2D("MineMat") { bounciness = 0f, friction = 0.9f };

        var rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var m = go.AddComponent<Mine>();
        m._weapon = w;
        m._sr = sr;
        return m;
    }

    void Update()
    {
        if (GameManager.I == null) return;

        if (_arm > 0f) { _arm -= Time.deltaTime; return; }

        if (_countdown < 0f)
        {
            if (!SomeoneClose()) return;
            _countdown = Delay;
            Sfx.Bounce();
        }

        _countdown -= Time.deltaTime;
        _sr.color = Mathf.Repeat(_countdown, 0.16f) < 0.08f ? Color.red : Color.white;
        if (_countdown <= 0f) Blow();
    }

    bool SomeoneClose()
    {
        var worms = GameManager.I.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead) continue;
            if (Vector2.Distance(w.transform.position, transform.position) <= Trigger) return true;
        }
        return false;
    }

    void Blow()
    {
        var pos = transform.position;
        Destroy(gameObject);
        Combat.Detonate(pos, _weapon.BlastRadius, _weapon.Damage);
    }
}
