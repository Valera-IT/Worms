using System.Collections.Generic;
using UnityEngine;

/// Звуковые эффекты. Клип синтезируется при первом обращении и дальше живёт в кэше,
/// играется через маленький пул AudioSource — так одновременные взрывы не обрывают
/// друг друга. Громкость приходит из настроек (SoundVolume); музыка отдельно, в Music.
public static class Sfx
{
    /// Какие звуки вообще есть. Имена — то, чем их зовут из игры.
    public enum Sound
    {
        Shot, Shotgun, Explosion, Splash, Step, Jump, Bounce, Thud,
        CrateDrop, Pickup, RopeShot, RopeHit, Teleport, TurnStart, Flood, Bye
    }

    /// 0..1 из настроек. Общий AudioListener.volume мы не трогаем: он приглушил бы
    /// и музыку тоже, а у неё свой ползунок.
    public static float Volume = 0.9f;

    const int Voices = 8;

    static readonly Dictionary<Sound, AudioClip> _clips = new Dictionary<Sound, AudioClip>();
    static AudioSource[] _pool;
    static int _next;
    static GameObject _host;

    // --- то, чем пользуется игра -------------------------------------------

    public static void Shot() => Play(Sound.Shot, 0.85f, Random.Range(0.94f, 1.07f));
    public static void Shotgun() => Play(Sound.Shotgun, 0.9f, Random.Range(0.96f, 1.05f));
    public static void Splash() => Play(Sound.Splash, 0.8f, Random.Range(0.9f, 1.12f));
    public static void Step() => Play(Sound.Step, 0.35f, Random.Range(0.85f, 1.2f));
    public static void Jump() => Play(Sound.Jump, 0.5f, Random.Range(0.95f, 1.1f));
    public static void Bounce() => Play(Sound.Bounce, 0.45f, Random.Range(0.9f, 1.15f));
    public static void Thud() => Play(Sound.Thud, 0.75f, Random.Range(0.92f, 1.08f));
    public static void CrateDrop() => Play(Sound.CrateDrop, 0.7f);
    public static void Pickup() => Play(Sound.Pickup, 0.8f);
    public static void RopeShot() => Play(Sound.RopeShot, 0.6f, Random.Range(0.95f, 1.08f));
    public static void RopeHit() => Play(Sound.RopeHit, 0.6f, Random.Range(0.92f, 1.1f));
    public static void Teleport() => Play(Sound.Teleport, 0.8f);
    public static void TurnStart() => Play(Sound.TurnStart, 0.45f);
    public static void Flood() => Play(Sound.Flood, 0.9f);
    public static void Bye() => Play(Sound.Bye, 0.7f, Random.Range(0.92f, 1.12f));

    /// Взрыв: чем крупнее воронка, тем ниже и длиннее. Один клип, разный pitch —
    /// синтезировать по клипу на калибр было бы расточительством ради того же эффекта.
    public static void Explosion(float radius)
    {
        Play(Sound.Explosion, 1f, Mathf.Clamp(3.2f / Mathf.Max(0.6f, radius), 0.7f, 1.6f));
        Rumble(radius);
    }

    /// Вибрация на телефоне — второй канал того же удара. На PC метод пустой.
    static void Rumble(float radius)
    {
        if (radius < 2f) return;
        if (!Application.isMobilePlatform) return;
        if (App.I != null && !App.I.Settings.Vibration) return;
#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate(); // на десктопе тип Handheld отсутствует в игровой сборке
#endif
    }

    // --- воспроизведение ---------------------------------------------------

    public static void Play(Sound s, float volume = 1f, float pitch = 1f)
    {
        if (Volume <= 0.001f) return;
        if (!Application.isPlaying) return;

        var src = NextSource();
        if (src == null) return;

        src.pitch = pitch;
        src.PlayOneShot(Clip(s), Mathf.Clamp01(volume * Volume));
    }

    /// Клип по требованию: синтез стоит миллисекунды, но платить за него
    /// на первом же выстреле в бою незачем — Prewarm зовётся при старте матча.
    public static AudioClip Clip(Sound s)
    {
        if (_clips.TryGetValue(s, out var c) && c != null) return c;
        c = Make(s);
        _clips[s] = c;
        return c;
    }

    /// Прогреть кэш заранее: генерация всех клипов занимает единицы миллисекунд.
    public static void Prewarm()
    {
        foreach (Sound s in System.Enum.GetValues(typeof(Sound))) Clip(s);
    }

    static AudioSource NextSource()
    {
        if (_pool == null || _host == null)
        {
            _host = new GameObject("Audio");
            Object.DontDestroyOnLoad(_host);
            _pool = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var src = _host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;    // мир плоский и целиком в кадре — панорама не нужна
                _pool[i] = src;
            }
        }

        var pick = _pool[_next];
        _next = (_next + 1) % Voices;
        return pick;
    }

    // --- синтез ------------------------------------------------------------

    static AudioClip Make(Sound s) => s switch
    {
        Sound.Shot => MakeShot(),
        Sound.Shotgun => MakeShotgun(),
        Sound.Explosion => MakeExplosion(),
        Sound.Splash => MakeSplash(),
        Sound.Step => MakeStep(),
        Sound.Jump => MakeJump(),
        Sound.Bounce => MakeBounce(),
        Sound.Thud => MakeThud(),
        Sound.CrateDrop => MakeCrateDrop(),
        Sound.Pickup => MakePickup(),
        Sound.RopeShot => MakeRopeShot(),
        Sound.RopeHit => MakeRopeHit(),
        Sound.Teleport => MakeTeleport(),
        Sound.TurnStart => MakeTurnStart(),
        Sound.Bye => MakeBye(),
        _ => MakeFlood()
    };

    /// Пуск ракеты: низкий тон, съезжающий вниз, поверх шипящий выхлоп.
    static AudioClip MakeShot()
    {
        var n = new Synth.Noise(11);
        var lp = new Synth.LowPass();
        var osc = new Synth.Osc();
        return Synth.Build("sfx_shot", 0.34f, t =>
        {
            float body = osc.Sin(Mathf.Lerp(240f, 70f, Mathf.Clamp01(t / 0.18f)));
            float air = lp.Step(n.Next(), 0.22f);
            return (body * 0.55f + air * 0.7f) * Synth.Env(t, 0.004f, 0.09f);
        });
    }

    /// Дробовик: короткий жёсткий щелчок, шум почти не фильтруем.
    static AudioClip MakeShotgun()
    {
        var n = new Synth.Noise(23);
        var hp = new Synth.HighPass();
        var lp = new Synth.LowPass();
        var osc = new Synth.Osc();
        return Synth.Build("sfx_shotgun", 0.30f, t =>
        {
            float crack = hp.Step(n.Next(), 0.85f) * Synth.Env(t, 0.001f, 0.035f);
            float boom = lp.Step(osc.Sin(Mathf.Lerp(180f, 60f, Mathf.Clamp01(t / 0.1f))), 0.5f)
                       * Synth.Env(t, 0.002f, 0.07f);
            return crack * 0.9f + boom * 0.6f;
        });
    }

    /// Взрыв: удар низом, затем длинный шумовой хвост, который постепенно глохнет —
    /// фильтр закрывается по времени, поэтому конец звучит дальше, чем начало.
    static AudioClip MakeExplosion()
    {
        var n = new Synth.Noise(37);
        var lp = new Synth.LowPass();
        var osc = new Synth.Osc();
        return Synth.Build("sfx_boom", 1.05f, t =>
        {
            float k = Mathf.Lerp(0.55f, 0.05f, Mathf.Clamp01(t / 0.6f));
            float roar = lp.Step(n.Next(), k) * Synth.Env(t, 0.003f, 0.28f);
            float thud = osc.Sin(Mathf.Lerp(120f, 32f, Mathf.Clamp01(t / 0.25f)))
                       * Synth.Env(t, 0.002f, 0.16f);
            return roar * 1.0f + thud * 0.8f;
        });
    }

    /// Всплеск: шум с быстрым подъёмом и «булькающим» спадом частоты фильтра.
    static AudioClip MakeSplash()
    {
        var n = new Synth.Noise(53);
        var lp = new Synth.LowPass();
        var hp = new Synth.HighPass();
        return Synth.Build("sfx_splash", 0.55f, t =>
        {
            float k = Mathf.Lerp(0.8f, 0.12f, Mathf.Clamp01(t / 0.35f));
            float body = lp.Step(n.Next(), k);
            float drops = hp.Step(body, 0.6f) * Mathf.Sin(t * 60f) * 0.5f;
            return (body + drops) * Synth.Env(t, 0.008f, 0.14f);
        });
    }

    /// Шаг: очень короткий глухой щелчок. Играется часто, поэтому и тихий, и сухой.
    static AudioClip MakeStep()
    {
        var n = new Synth.Noise(71);
        var lp = new Synth.LowPass();
        return Synth.Build("sfx_step", 0.07f, t =>
            lp.Step(n.Next(), 0.3f) * Synth.Env(t, 0.001f, 0.014f));
    }

    static AudioClip MakeJump()
    {
        var osc = new Synth.Osc();
        return Synth.Build("sfx_jump", 0.18f, t =>
            osc.Sin(Mathf.Lerp(240f, 620f, Mathf.Clamp01(t / 0.12f))) * Synth.Env(t, 0.004f, 0.06f));
    }

    /// Отскок гранаты: деревянный «тюк» — два обертона и короткий спад.
    static AudioClip MakeBounce()
    {
        var a = new Synth.Osc();
        var b = new Synth.Osc();
        return Synth.Build("sfx_bounce", 0.16f, t =>
            (a.Sin(420f) * 0.7f + b.Sin(631f) * 0.3f) * Synth.Env(t, 0.001f, 0.035f));
    }

    /// Червь приложился о землю: низкий тон, съезжающий вниз, и глухой шлепок
    /// шума поверх. Отскок гранаты рядом звучит деревянно и высоко — падение
    /// должно читаться как удар телом, а не как «тюк».
    static AudioClip MakeThud()
    {
        var osc = new Synth.Osc();
        var noise = new Synth.Noise(9137);
        return Synth.Build("sfx_thud", 0.26f, t =>
            osc.Sin(Mathf.Lerp(150f, 62f, Mathf.Clamp01(t / 0.12f))) * Synth.Env(t, 0.002f, 0.12f) * 0.8f
            + noise.Next() * Synth.Env(t, 0.001f, 0.05f) * 0.35f);
    }

    /// Ящик пошёл вниз: тихий двойной сигнал, чтобы взгляд успел найти парашют.
    static AudioClip MakeCrateDrop()
    {
        var osc = new Synth.Osc();
        return Synth.Build("sfx_crate", 0.55f, t =>
        {
            float f = t < 0.22f ? 660f : 880f;
            float e = t < 0.22f ? Synth.Env(t, 0.01f, 0.07f) : Synth.Env(t - 0.25f, 0.01f, 0.09f);
            return osc.Sin(f) * Mathf.Max(0f, e);
        });
    }

    /// Подбор припаса: восходящее трезвучие.
    static AudioClip MakePickup()
    {
        var osc = new Synth.Osc();
        return Synth.Build("sfx_pickup", 0.42f, t =>
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(t / 0.1f), 0, 2);
            float f = step == 0 ? 523f : step == 1 ? 659f : 784f;
            return osc.Sin(f) * Synth.Env(t - step * 0.1f, 0.005f, 0.09f);
        });
    }

    /// Выстрел гарпуна: короткий «вжик» вверх по частоте.
    static AudioClip MakeRopeShot()
    {
        var n = new Synth.Noise(97);
        var hp = new Synth.HighPass();
        return Synth.Build("sfx_rope", 0.22f, t =>
            hp.Step(n.Next(), Mathf.Lerp(0.5f, 0.95f, Mathf.Clamp01(t / 0.15f)))
            * Synth.Env(t, 0.01f, 0.06f));
    }

    /// Гарпун вошёл в породу.
    static AudioClip MakeRopeHit()
    {
        var n = new Synth.Noise(113);
        var lp = new Synth.LowPass();
        var osc = new Synth.Osc();
        return Synth.Build("sfx_ropehit", 0.16f, t =>
            (lp.Step(n.Next(), 0.4f) * 0.6f + osc.Sin(180f) * 0.5f) * Synth.Env(t, 0.001f, 0.03f));
    }

    /// Телепорт: свип вниз (исчез) и свип вверх (появился) через паузу.
    static AudioClip MakeTeleport()
    {
        var a = new Synth.Osc();
        var b = new Synth.Osc();
        return Synth.Build("sfx_teleport", 0.6f, t =>
        {
            float outp = a.Sin(Mathf.Lerp(900f, 140f, Mathf.Clamp01(t / 0.22f))) * Synth.Env(t, 0.005f, 0.1f);
            float inp = t < 0.3f ? 0f
                      : b.Sin(Mathf.Lerp(140f, 1100f, Mathf.Clamp01((t - 0.3f) / 0.22f))) * Synth.Env(t - 0.3f, 0.005f, 0.1f);
            return outp + inp;
        });
    }

    /// Начало хода: мягкий короткий сигнал.
    static AudioClip MakeTurnStart()
    {
        var osc = new Synth.Osc();
        return Synth.Build("sfx_turn", 0.22f, t => osc.Sin(880f) * Synth.Env(t, 0.01f, 0.05f));
    }

    /// Прощание червя: три ноты вниз, будто «ну-и-всё». Голоса в проекте нет,
    /// а короткая нисходящая фраза читается как реплика, а не как сигнал.
    static AudioClip MakeBye()
    {
        var osc = new Synth.Osc();
        return Synth.Build("sfx_bye", 0.5f, t =>
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(t / 0.14f), 0, 2);
            float f = step == 0 ? 700f : step == 1 ? 560f : 420f;
            // Лёгкое вибрато — от него нота звучит голосом, а не пищалкой.
            return osc.Sin(f * (1f + Mathf.Sin(t * 42f) * 0.03f)) * Synth.Env(t - step * 0.14f, 0.008f, 0.09f);
        });
    }

    /// Потоп: низкий гудок с биением — сирена, а не бип.
    static AudioClip MakeFlood()
    {
        var a = new Synth.Osc();
        var b = new Synth.Osc();
        return Synth.Build("sfx_flood", 1.6f, t =>
        {
            float e = Mathf.Min(1f, t / 0.15f) * Mathf.Min(1f, (1.6f - t) / 0.4f);
            return (a.Sin(92f) + b.Sin(97f)) * 0.5f * e;
        });
    }
}
