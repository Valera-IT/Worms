using UnityEngine;

/// Позы червя. Кадры к ним рисует WormSprite; здесь они только называются
/// и раскладываются по состояниям.
public enum WormPose { Idle, Walk, Jump, Fall, Land, Hurt, Drown, Chute, Jet, Aim, Rope, Dig, Swing, Bye }

/// Всё, что нужно знать о черве, чтобы выбрать позу. Отдельная структура,
/// а не сам Worm: так выбор позы проверяется тестом без сцены, физики и
/// живого матча — таблицей «состояние → клип».
public struct WormAnimState
{
    public bool Grounded;
    public bool Underwater;
    public bool Chute;
    public bool Jet;
    public bool Frozen;
    public bool Hurt;      // червя только что задело: короткая поза боли
    public bool Landing;    // только что коснулся земли: доигрывается приземление
    public bool Roped;     // висит на верёвке
    public bool Digging;   // работает буром или лампой
    public bool Swinging;  // машет кулаком, битой или толкает
    public bool Saying;    // прощается перед смертью
    public bool Aiming;    // активный червь целится: корпус ведёт за стволом
    public float AimAngle;
    public float SpeedX;
    public float VelY;
}

/// Проигрыватель поз червя: каждый кадр смотрит на состояние, выбирает клип
/// и кладёт один и тот же номер кадра в тело и в лицо.
///
/// Физику не трогает вовсе — только два SpriteRenderer'а на дочернем «Art».
/// Разворот по Facing, поворот при прощании и всё остальное остаётся за Worm.
[DisallowMultipleComponent]
public class WormAnimator : MonoBehaviour
{
    /// С какой скорости вбок червь считается идущим, а не стоящим. То же
    /// число, по которому Worm отбивает шаги, — иначе звук и картинка
    /// разошлись бы на медленном сползании.
    const float WalkSpeed = 0.8f;

    /// Выше этого вверх — это отрыв, ниже — падение.
    const float RiseSpeed = 0.5f;

    /// Где у червя руки и на сколько от них вынесен ствол — в юнитах от центра
    /// тела. Ствол ездит по дуге вокруг этой точки, поэтому целиться он и
    /// корпус начинают вместе. Вынос короткий нарочно: оружие держат у груди,
    /// а не на вытянутой руке, — вперёд торчит только дуло.
    static readonly Vector2 Hand = new Vector2(0.04f, -0.17f);
    const float Reach = 0.24f;

    /// Насколько увеличена иконка в руках. Иконка панели ростом 0,8 юнита —
    /// в руках она читалась щепкой; базука должна быть длиннее самого червя
    /// и лежать у груди, как в оригинале, а не висеть палочкой впереди.
    /// Публичная: контактный лист рисует оружие тем же масштабом.
    public const float HeldScale = 1.55f;

    /// Отдача: на сколько ствол уходит назад в момент выстрела и за сколько
    /// возвращается. Тело от отдачи не двигаем — его уже толкает физика
    /// (Worm.FireProjectile добавляет импульс назад).
    const float Kick = 0.14f;
    const float KickTime = 0.16f;

    /// Сколько держится вспышка от попадания. Короче кадра боли нарочно:
    /// поза говорит «задело», а вспышка — «вот сейчас», и растягивать её
    /// значило бы красить червя белым весь отскок.
    const float FlashTime = 0.14f;

    Worm _worm;
    SpriteRenderer _body, _face, _gun;
    Color _bodyBase = Color.white;   // цвет команды: к нему вспышка и возвращается
    readonly SpriteAnimPlayer _play = new SpriteAnimPlayer();

    float _hurtUntil;
    float _flashLeft;
    float _landUntil;
    float _swingUntil;
    float _digUntil;
    float _byeUntil;
    float _kickLeft;
    float _lean = float.NaN;   // под каким углом нарисована иконка в руках
    bool _wasGrounded = true;

    public WormPose Pose { get; private set; } = WormPose.Idle;
    public int Frame => _play.Frame;
    public SpriteAnim Clip => _play.Clip;

    public static WormAnimator Attach(Worm worm, SpriteRenderer body, SpriteRenderer face, SpriteRenderer gun = null)
    {
        var a = worm.gameObject.AddComponent<WormAnimator>();
        a._worm = worm;
        a._body = body;
        a._face = face;
        a._gun = gun;
        if (body != null) a._bodyBase = body.color;
        a.Apply(WormPose.Idle);
        return a;
    }

    /// Червя задело: показать боль. Длину задаёт сам клип — таймер и картинка
    /// так не расходятся, сколько бы кадров ни нарисовала фаза 13b.
    public void Flinch()
    {
        _hurtUntil = Time.time + ClipFor(WormPose.Hurt).Length;
        _flashLeft = FlashTime;
    }

    /// Замах: кулак, бита и толчок бьют мгновенно, а показать удар надо целиком.
    /// Длину, как и у боли, задаёт сам клип.
    public void Swing()
    {
        _swingUntil = Time.time + ClipFor(WormPose.Swing).Length;
    }

    /// Рытьё: бур и лампа режут породу одним вызовом, поэтому позу держим
    /// столько, сколько длится звук работы, а не один кадр.
    public void Digs(float seconds = 0.45f)
    {
        _digUntil = Time.time + seconds;
    }

    /// Отдача ствола на выстреле.
    public void Recoil()
    {
        _kickLeft = KickTime;
    }

    /// Прощание перед смертью. Возвращает длину клипа: по ней Worm и отмеряет
    /// паузу, так что картинка и ход не разъезжаются.
    public float Farewell()
    {
        var clip = ClipFor(WormPose.Bye);
        _byeUntil = Time.time + clip.Length;
        return clip.Length;
    }

    /// Червь ещё прощается: пока это так, аниматор работает и на мёртвом черве.
    public bool SayingBye => Time.time < _byeUntil;

    /// Что держат в руках и под каким углом это нарисовано (WeaponIcons.Lean).
    /// Пустой спрайт убирает ствол вовсе — так ходят с верёвкой, с телепортом
    /// и после выстрела.
    public void Hold(Sprite sprite, float lean = float.NaN)
    {
        if (_gun == null) return;
        _gun.sprite = sprite;
        _lean = lean;
    }

    void LateUpdate()
    {
        if (_worm == null) return;
        // Мёртвый червь ещё прощается — это единственное, что ему осталось.
        if (_worm.IsDead && !SayingBye) return;

        bool grounded = _worm.OnGround;
        // Приземление ловим по смене признака, а не по скорости: у падения на
        // склон вертикальная скорость гасится раньше, чем червь встаёт.
        if (grounded && !_wasGrounded) _landUntil = Time.time + ClipFor(WormPose.Land).Length;
        _wasGrounded = grounded;

        var v = _worm.Velocity;
        var s = new WormAnimState
        {
            Grounded = grounded,
            Underwater = _worm.Underwater,
            Chute = _worm.Chuting,
            Jet = _worm.Jetting,
            Frozen = _worm.Frozen,
            Hurt = Time.time < _hurtUntil,
            Landing = Time.time < _landUntil,
            Roped = _worm.Roped,
            Digging = Time.time < _digUntil,
            Swinging = Time.time < _swingUntil,
            Saying = SayingBye,
            Aiming = _worm.Aiming,
            AimAngle = _worm.AimAngle,
            SpeedX = v.x,
            VelY = v.y,
        };

        var pose = PoseFor(s);
        Apply(pose);

        // Прицел — единственная поза, которую не проигрывают: её кадр выбирает
        // угол. Остальные идут по времени.
        if (pose == WormPose.Aim) _play.Show(ClipFor(pose), WormSprite.AimStep(s.AimAngle));
        else _play.Tick(Time.deltaTime);

        if (_body != null) _body.sprite = _play.BodyFrame;
        if (_face != null) _face.sprite = _play.FaceFrame;

        Flash();
        Gun(s, pose);
    }

    /// Вспышка от попадания: тело на мгновение выбеливается и возвращается
    /// к цвету команды. Красим только тело — лицо и так белое, и подсвечивать
    /// в нём нечего. Цвет пишем через SpriteRenderer, а не в кадр: кадры общие
    /// на всех червей, и белили бы они сразу всю команду.
    void Flash()
    {
        if (_body == null) return;
        if (_flashLeft <= 0f) return;

        _flashLeft = Mathf.Max(0f, _flashLeft - Time.deltaTime);
        float k = _flashLeft / FlashTime;
        _body.color = Color.Lerp(_bodyBase, Color.white, k * 0.85f);
    }

    /// Где стоит ствол при таком угле: точка в юнитах от центра тела, вперёд
    /// по прицелу. Формула вынесена наружу, чтобы контактный лист рисовал
    /// оружие ровно там же, где его ставит игра, а не «примерно там».
    public static Vector2 GunOffset(float angleDeg, float back = 0f)
    {
        float a = angleDeg * Mathf.Deg2Rad;
        return Hand + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Reach - back);
    }

    /// Ствол в руках. Висит на том же «Art», что тело и лицо, поэтому разворот
    /// по Facing достаётся ему даром: зеркалится вся ветка целиком.
    void Gun(WormAnimState s, WormPose pose)
    {
        if (_gun == null) return;

        // Оружие видно, только когда червь его держит: в полёте, на верёвке,
        // под водой и в прощании руки заняты другим.
        bool show = _gun.sprite != null && s.Aiming && !s.Saying && !s.Roped
                    && !s.Underwater && !s.Chute && !s.Jet
                    && (pose == WormPose.Aim || pose == WormPose.Idle || pose == WormPose.Walk);
        if (_gun.enabled != show) _gun.enabled = show;
        if (!show) return;

        if (_kickLeft > 0f) _kickLeft = Mathf.Max(0f, _kickLeft - Time.deltaTime);
        float back = Kick * (_kickLeft / KickTime);

        _gun.transform.localPosition = GunOffset(s.AimAngle, back);
        _gun.transform.localScale = Vector3.one * HeldScale;
        // Ненаводимое (граната, динамит, овца) висит ровно: вертеть за прицелом
        // осмысленно только то, у чего есть ствол.
        float rot = float.IsNaN(_lean) ? 0f : s.AimAngle - _lean;
        _gun.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
    }

    void Apply(WormPose pose)
    {
        Pose = pose;
        _play.Play(ClipFor(pose));
    }

    /// Состояние → поза. Порядок здесь — это приоритет: вода важнее купола,
    /// купол важнее падения, падение важнее боли. Замороженному червю
    /// (улёгшемуся в покой) остаётся только стойка: шаг у него означал бы,
    /// что состояние определено неверно.
    public static WormPose PoseFor(WormAnimState s)
    {
        // Прощание перекрывает всё: червь уже мёртв, и остальные признаки
        // (опора, вода, купол) про него больше ничего не значат.
        if (s.Saying) return WormPose.Bye;
        if (s.Underwater) return WormPose.Drown;
        if (s.Chute) return WormPose.Chute;
        if (s.Jet) return WormPose.Jet;
        // Верёвка важнее полёта: висящий червь не падает, он висит.
        if (s.Roped) return WormPose.Rope;
        if (!s.Grounded) return s.VelY > RiseSpeed ? WormPose.Jump : WormPose.Fall;
        if (s.Hurt) return WormPose.Hurt;
        if (s.Swinging) return WormPose.Swing;
        if (s.Landing) return WormPose.Land;
        if (s.Digging) return WormPose.Dig;
        if (s.Frozen) return WormPose.Idle;
        if (Mathf.Abs(s.SpeedX) > WalkSpeed) return WormPose.Walk;
        // Прицел ниже ходьбы: с оружием в руках червь всё равно сначала идёт.
        return s.Aiming ? WormPose.Aim : WormPose.Idle;
    }

    /// Кадры в секунду и петля у каждой позы свои: стойка дышит медленно,
    /// шаг идёт под темп ходьбы, отрыв, приземление и боль проходят один раз
    /// и застывают на последнем кадре, пока состояние не сменится.
    public static float FpsOf(WormPose p)
    {
        switch (p)
        {
            case WormPose.Walk: return 14f;
            case WormPose.Jump: return 10f;
            case WormPose.Land: return 12f;
            case WormPose.Hurt: return 8f;
            case WormPose.Fall: return 6f;
            case WormPose.Drown: return 6f;
            case WormPose.Chute: return 4f;
            case WormPose.Jet: return 10f;
            case WormPose.Swing: return 14f;
            case WormPose.Dig: return 16f;
            case WormPose.Bye: return 7.3f;   // четыре кадра ≈ 0,55 с прощания
            case WormPose.Rope: return 3f;
            case WormPose.Aim: return 1f;     // кадр выбирает угол, а не время
            default: return 5f;   // стойка: дыхание и редкое моргание
        }
    }

    /// Одиночными идут те позы, из которых червь обязан выйти сам: отрыв,
    /// приземление, боль, удар и прощание. Прицел не петля по другой причине —
    /// его вообще не проигрывают, кадр там выбирает угол.
    public static bool LoopsOf(WormPose p)
        => p != WormPose.Jump && p != WormPose.Land && p != WormPose.Hurt
        && p != WormPose.Swing && p != WormPose.Bye && p != WormPose.Aim;

    static SpriteAnim[] _clips;

    /// Клип позы. Строится один раз на запуск: кадры у всех червей общие,
    /// цвет команды даёт SpriteRenderer, поэтому набор не множится на команды.
    public static SpriteAnim ClipFor(WormPose pose)
    {
        int n = System.Enum.GetValues(typeof(WormPose)).Length;
        if (_clips == null) _clips = new SpriteAnim[n];

        // Клип держим, пока живы его кадры: см. WormSprite.Frames — в
        // редакторе спрайты умирают раньше статического кэша.
        int i = (int)pose;
        if (_clips[i] != null && _clips[i].Body[0] != null) return _clips[i];

        WormSprite.Frames(pose, out var body, out var face);
        return _clips[i] = new SpriteAnim(pose.ToString(), body, face, FpsOf(pose), LoopsOf(pose));
    }
}
