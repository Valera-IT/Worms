using UnityEngine;

/// Верёвка-ниндзя. Гарпун мгновенно уходит по направлению прицела и цепляется
/// только за ландшафт; дальше червь висит на DistanceJoint2D и раскачивается.
/// Ход после верёвки не заканчивается — тем она и отличается от оружия: зацепился,
/// перелетел на соседний островок и оттуда уже стреляешь.
public class Rope : MonoBehaviour
{
    /// Длину подбирали по карте: мир 96×48, и с 22 юнитами до потолка пещеры
    /// из середины зала было не дотянуться.
    public const float MaxLength = 30f;

    const float MinLength = 1.4f;
    const float ReelSpeed = 7f;      // юнитов в секунду при подтягивании
    const float SwingForce = 34f;    // сила раскачки вбок
    const float ReleaseKick = 3.5f;  // подскок при отцепе

    public bool Attached => _joint != null;
    public Vector2 Anchor => _anchor;
    public float Length => _joint != null ? _joint.distance : 0f;

    Rigidbody2D _rb;
    DistanceJoint2D _joint;
    SpriteRenderer _line;
    Vector2 _anchor;
    float _swing;

    /// Верёвка живёт на самом черве — так она сама исчезает вместе с ним.
    public static Rope Of(Worm worm)
    {
        var r = worm.GetComponent<Rope>();
        return r != null ? r : worm.gameObject.AddComponent<Rope>();
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
    }

    void OnDestroy()
    {
        if (_line != null) Destroy(_line.gameObject);
    }

    /// Бросок. Возвращает false, если гарпун ушёл в пустоту — тогда и патрон цел.
    public bool Throw(Vector2 dir)
    {
        Release();
        Sfx.RopeShot();

        Vector2 from = transform.position;
        var hits = Physics2D.RaycastAll(from, dir.normalized, MaxLength);

        float best = float.MaxValue;
        Vector2 point = default;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i].collider;
            // Цепляемся строго за породу: червь и ящик — не опора.
            if (c == null || c.GetComponentInParent<DestructibleTerrain>() == null) continue;
            if (hits[i].distance < best) { best = hits[i].distance; point = hits[i].point; found = true; }
        }

        if (!found || best < 0.7f) return false;

        _anchor = SnapIntoRock(point, dir.normalized);
        best = Vector2.Distance(from, _anchor);
        _joint = gameObject.AddComponent<DistanceJoint2D>();
        _joint.autoConfigureDistance = false;
        _joint.autoConfigureConnectedAnchor = false;
        _joint.connectedBody = null;          // цепляемся за точку мира, а не за тело
        _joint.connectedAnchor = _anchor;
        _joint.distance = Mathf.Max(MinLength, best);
        _joint.maxDistanceOnly = true;        // верёвка тянет, но не отталкивает
        _joint.enableCollision = false;

        EnsureLine();
        Sfx.RopeHit();
        return true;
    }

    /// Точку попадания даёт коллайдер, а его marching squares строит с шагом в
    /// несколько пикселей — кромка проходит мимо твёрдых пикселей маски. Опору же
    /// FixedUpdate проверяет по самой маске и отцепил бы верёвку в тот же кадр,
    /// поэтому якорь загоняем внутрь породы по направлению броска.
    static Vector2 SnapIntoRock(Vector2 point, Vector2 dir)
    {
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        if (terrain == null || terrain.IsSolidWorld(point)) return point;

        const int MaxSteps = 6;   // с запасом к шагу коллайдера в 4 пикселя
        float px = 1f / DestructibleTerrain.PixelsPerUnit;
        for (int i = 1; i <= MaxSteps; i++)
        {
            Vector2 p = point + dir * (i * px);
            if (terrain.IsSolidWorld(p)) return p;
        }
        return point;
    }

    /// Управление на весу: вбок — раскачка, вверх-вниз — длина, прыжок — отцеп.
    /// Ходьба и прыжок с земли на это время отключены: Worm отдаёт ввод сюда целиком.
    public void Control(IGameInput input)
    {
        if (_joint == null) return;

        _swing = Mathf.Clamp(input.Move, -1f, 1f);

        float reel = input.AimAxis;
        if (Mathf.Abs(reel) > 0.01f)
            _joint.distance = Mathf.Clamp(_joint.distance - reel * ReelSpeed * Time.deltaTime, MinLength, MaxLength);

        if (input.JumpPressed) Release(true);
    }

    void FixedUpdate()
    {
        if (_joint == null) return;

        // Опору могло снести взрывом — висеть в воздухе на дырке нечестно.
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        if (terrain != null && !terrain.IsSolidWorld(_anchor)) { Release(); return; }

        if (Mathf.Abs(_swing) > 0.01f)
            _rb.AddForce(new Vector2(_swing * SwingForce, 0f), ForceMode2D.Force);
    }

    public void Release(bool kick = false)
    {
        _swing = 0f;
        if (_joint != null)
        {
            Destroy(_joint);
            _joint = null;
            if (kick && _rb != null) _rb.AddForce(Vector2.up * ReleaseKick, ForceMode2D.Impulse);
        }
        if (_line != null) _line.enabled = false;
    }

    void LateUpdate()
    {
        if (_line == null) return;
        if (_joint == null) { _line.enabled = false; return; }

        Vector2 from = transform.position;
        Vector2 d = _anchor - from;
        float len = d.magnitude;

        _line.enabled = true;
        _line.transform.position = from + d * 0.5f;
        _line.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        _line.transform.localScale = new Vector3(len, 0.07f, 1f);
    }

    void EnsureLine()
    {
        if (_line != null) { _line.enabled = true; return; }
        // Вне иерархии червя: иначе поворот и растяжение достались бы и телу.
        _line = Sprites.Make("RopeLine", Sprites.Square, new Color(0.86f, 0.83f, 0.76f), 8, GameManager.Root);
    }
}
