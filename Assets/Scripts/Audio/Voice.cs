using System.Collections.Generic;
using UnityEngine;

/// Голос червей. Червь коротко отзывается на выстрел, попадание, промах и
/// смерть — четырнадцать фраз на банк, у каждой команды банк свой, как в
/// оригинале: слова те же, а голос другой. Ассетов, как и везде в проекте, нет:
/// фраза синтезируется из собственного текста — гласные дают форманты, согласные
/// приступы, знак в конце ведёт тон вверх или вниз. Получается не речь, а
/// мультяшное бормотание с правильным ритмом; текст при этом всплывает над
/// червём, поэтому фразу и слышно, и видно.
public static class Voice
{
    /// На что червь отзывается. Больше поводов не заводим: реплика на каждый
    /// шаг превратила бы бой в болтовню.
    public enum Line { Fire, Hit, Miss, Die }

    /// Банк голоса: тон, сдвиг формант (он же «размер рта»), темп речи и
    /// сипение. Четырёх хватает на четыре команды — пятой в матче не бывает.
    public struct Bank
    {
        public string Name;
        public float Pitch;     // основной тон, Гц
        public float Formant;   // множитель формант: ниже — крупнее челюсть
        public float Rate;      // темп: выше — короче слоги
        public float Rasp;      // доля шума в связках
    }

    public static readonly Bank[] Banks =
    {
        new Bank { Name = "бас",     Pitch = 98f,  Formant = 0.86f, Rate = 1.00f, Rasp = 0.10f },
        new Bank { Name = "тенор",   Pitch = 152f, Formant = 1.00f, Rate = 1.06f, Rasp = 0.05f },
        new Bank { Name = "пискля",  Pitch = 236f, Formant = 1.22f, Rate = 1.14f, Rasp = 0.03f },
        new Bank { Name = "сипатый", Pitch = 124f, Formant = 0.94f, Rate = 0.92f, Rasp = 0.30f }
    };

    /// Четырнадцать фраз банка. Слова короткие: метка живёт меньше секунды и
    /// над головой должна читаться целиком, а бормотание длиннее секунды
    /// перестаёт быть репликой и начинается монолог.
    static readonly string[][] Phrases =
    {
        new[] { "Получай!", "Ну держись!", "От винта!", "Летит!" },      // Fire
        new[] { "Есть!", "Так тебе!", "Прямо в лоб!", "Ага!" },          // Hit
        new[] { "Мимо…", "Ой…", "Ну куда же ты" },                       // Miss
        new[] { "Прощай!", "Ну всё…", "Мама!" }                          // Die
    };

    /// Сколько всего фраз в банке — цифру спрашивает проверка содержимого.
    public static int PhraseCount
    {
        get { int n = 0; foreach (var set in Phrases) n += set.Length; return n; }
    }

    public static int BankCount => Banks.Length;
    public static int VariantsOf(Line line) => Phrases[(int)line].Length;
    public static string Text(Line line, int variant) => Phrases[(int)line][variant % VariantsOf(line)];

    /// Цвет реплики над головой: тёплый, чтобы не путаться с числами урона.
    static readonly Color Ink = new Color(1f, 0.92f, 0.6f);

    /// Пауза перед фразой. Выстрел заглушил бы её начало, поэтому реплика на
    /// выстрел выходит из-под грохота, а не поверх него; итог боя и прощание
    /// звучат сразу — там перебивать нечего.
    static float DelayOf(Line line) => line == Line.Fire ? 0.30f : line == Line.Hit || line == Line.Miss ? 0.12f : 0f;

    // --- воспроизведение ---------------------------------------------------

    static readonly Dictionary<int, AudioClip> _clips = new Dictionary<int, AudioClip>();
    static AudioSource _src;
    static GameObject _host;

    /// Реплику червя. Возвращает, сколько секунд она будет занимать вместе с
    /// паузой перед ней: ход обязан дождаться своей реплики, иначе Hush на
    /// старте следующего хода срежет её на полуслове.
    public static float Say(Worm w, Line line)
    {
        if (w == null || (w.IsDead && line != Line.Die)) return 0f;

        int bank = w.Team != null ? w.Team.VoiceBank : 0;
        int variant = Random.Range(0, VariantsOf(line));
        string text = Text(line, variant);

        Fx.FloatingText((Vector2)w.transform.position + Vector2.up * 0.9f, text, Ink);
        return Play(bank, line, variant);
    }

    /// Та же реплика без червя — ею пользуются проверки и прогрев.
    public static float Play(int bank, Line line, int variant)
    {
        if (!Application.isPlaying) return 0f;

        var clip = Clip(bank, line, variant);
        if (clip == null) return 0f;

        float delay = DelayOf(line);
        if (Sfx.Volume <= 0.001f) return delay + clip.length;

        var src = Source();
        if (src == null) return 0f;

        // Новая реплика перебивает прежнюю, а не ложится на неё: два червя,
        // заговорившие разом, звучат кашей, и разобрать нельзя ни одного.
        src.Stop();
        src.clip = clip;
        src.volume = Mathf.Clamp01(0.85f * Sfx.Volume);
        if (delay > 0f) src.PlayDelayed(delay); else src.Play();
        return delay + clip.length;
    }

    /// Замолчать. Зовётся на старте хода и при сносе матча: реплика, доигранная
    /// в чужой ход, звучит так, будто её сказал соперник. Stop снимает и
    /// отложенный запуск, поэтому фраза, ещё не начавшаяся, тоже не выйдет.
    public static void Hush()
    {
        if (_src != null) _src.Stop();
    }

    static AudioSource Source()
    {
        if (_src != null && _host != null) return _src;
        _host = new GameObject("Voice");
        Object.DontDestroyOnLoad(_host);
        _src = _host.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;
        return _src;
    }

    public static AudioClip Clip(int bank, Line line, int variant)
    {
        bank = Mathf.Clamp(bank, 0, Banks.Length - 1);
        variant %= VariantsOf(line);
        int key = (bank * 8 + (int)line) * 8 + variant;
        if (_clips.TryGetValue(key, out var c) && c != null) return c;
        c = Render(Text(line, variant), Banks[bank], key);
        _clips[key] = c;
        return c;
    }

    /// Прогреть банк команды: четырнадцать фраз считаются единицы миллисекунд,
    /// но платить за них в тот самый кадр, когда червь выстрелил, незачем.
    public static void Prewarm(int bank)
    {
        for (int l = 0; l < Phrases.Length; l++)
            for (int v = 0; v < Phrases[l].Length; v++)
                Clip(bank, (Line)l, v);
    }

    // --- синтез ------------------------------------------------------------

    /// Слог: пара формант гласной, длительность тела и приступ — согласный,
    /// с которого слог начинается.
    struct Syl
    {
        public float F1, F2;
        public float Len;
        public char Onset;   // 'p' взрывной, 's' щелевой, 'm' сонорный, ' ' нет
    }

    const float Body = 0.115f;     // тело обычного слога, с
    const float MaxLen = 1.1f;     // длиннее фраза уже не реплика

    /// Гласные русского и их форманты. Мягкие («я», «ю», «е», «ё») отличаются
    /// от парных твёрдых поднятой второй формантой — на слух это и есть их «й».
    static bool Vowel(char c, out float f1, out float f2)
    {
        switch (c)
        {
            case 'а': f1 = 700f; f2 = 1150f; return true;
            case 'я': f1 = 690f; f2 = 1750f; return true;
            case 'о': f1 = 480f; f2 = 900f; return true;
            case 'ё': f1 = 480f; f2 = 1500f; return true;
            case 'у': f1 = 320f; f2 = 700f; return true;
            case 'ю': f1 = 320f; f2 = 1600f; return true;
            case 'ы': f1 = 350f; f2 = 1480f; return true;
            case 'э': f1 = 550f; f2 = 1750f; return true;
            case 'е': f1 = 470f; f2 = 1950f; return true;
            case 'и': f1 = 300f; f2 = 2300f; return true;
            default: f1 = 0f; f2 = 0f; return false;
        }
    }

    /// Класс согласной. Различать все звуки незачем: на слух реплику держат
    /// ритм и гласные, а согласные дают только характер приступа.
    static char Onset(char c)
    {
        if ("пбтдкгчц".IndexOf(c) >= 0) return 'p';
        if ("сзшщжфх".IndexOf(c) >= 0) return 's';
        if ("мнлрвй".IndexOf(c) >= 0) return 'm';
        return ' ';
    }

    static float OnsetLen(char c) => c switch { 'p' => 0.045f, 's' => 0.065f, 'm' => 0.040f, _ => 0f };

    /// Разбор фразы на слоги: копим согласные до ближайшей гласной, гласная
    /// закрывает слог. Пробел и дефис слог не начинают — они уже кончились
    /// гласной, — но добавляют паузу; её даёт удлинённый хвост предыдущего слога.
    static List<Syl> Parse(string text, Bank b)
    {
        var syls = new List<Syl>();
        char onset = ' ';

        for (int i = 0; i < text.Length; i++)
        {
            char c = char.ToLowerInvariant(text[i]);
            if (Vowel(c, out float f1, out float f2))
            {
                syls.Add(new Syl { F1 = f1, F2 = f2, Len = Body / b.Rate, Onset = onset });
                onset = ' ';
                continue;
            }
            if (c == ' ' || c == '-')
            {
                if (syls.Count > 0)
                {
                    var last = syls[syls.Count - 1];
                    last.Len += 0.05f;
                    syls[syls.Count - 1] = last;
                }
                onset = ' ';
                continue;
            }
            char cls = Onset(c);
            if (cls != ' ') onset = cls;
        }

        // Ударный конец: последний слог тянется — на нём и слышно, куда пошёл тон.
        if (syls.Count > 0)
        {
            var last = syls[syls.Count - 1];
            last.Len *= 1.7f;
            syls[syls.Count - 1] = last;
        }
        return syls;
    }

    /// Куда фраза ведёт тон: восклицание вверх, многоточие вниз, всё прочее —
    /// в спокойное завершение.
    static float Contour(string text)
    {
        if (text.EndsWith("!")) return 1.32f;
        if (text.EndsWith("…") || text.EndsWith("...")) return 0.70f;
        return 0.90f;
    }

    static AudioClip Render(string text, Bank b, int seed)
    {
        var syls = Parse(text, b);
        if (syls.Count == 0) syls.Add(new Syl { F1 = 700f, F2 = 1150f, Len = 0.2f, Onset = ' ' });

        float total = 0f;
        for (int i = 0; i < syls.Count; i++) total += OnsetLen(syls[i].Onset) + syls[i].Len;
        total = Mathf.Min(total, MaxLen);

        float rise = Contour(text);

        var noise = new Synth.Noise((uint)(seed * 2654435761u + 17u));
        var glottis = new Synth.Osc();
        var r1 = new Synth.Reso();
        var r2 = new Synth.Reso();
        var burst = new Synth.Reso();
        var hp = new Synth.HighPass();

        // Форманты ведём плавно от слога к слогу: скачком они звучат как
        // перебор тонов, а переход между ними и есть то, что ухо читает речью.
        float f1 = syls[0].F1 * b.Formant, f2 = syls[0].F2 * b.Formant;
        int idx = 0;
        float segStart = 0f;

        return Synth.Build("voice_" + b.Name, total, t =>
        {
            while (idx < syls.Count - 1 && t >= segStart + OnsetLen(syls[idx].Onset) + syls[idx].Len)
            {
                segStart += OnsetLen(syls[idx].Onset) + syls[idx].Len;
                idx++;
            }

            var s = syls[idx];
            float on = OnsetLen(s.Onset);
            float local = t - segStart;
            float prog = total > 0f ? Mathf.Clamp01(t / total) : 0f;
            // Лёгкое дрожание тона: ровный тон звучит пилой, а не голосом.
            float f0 = b.Pitch * Mathf.Lerp(1f, rise, prog) * (1f + 0.035f * Mathf.Sin(t * 2f * Mathf.PI * 5.5f));

            f1 = Mathf.Lerp(f1, s.F1 * b.Formant, 0.0016f);
            f2 = Mathf.Lerp(f2, s.F2 * b.Formant, 0.0016f);

            if (local < on)
            {
                switch (s.Onset)
                {
                    case 'p':
                        // Взрывной: сначала смычка — тишина, потом щелчок в
                        // области второй форманты. Без паузы «п» неотличимо от «ф».
                        if (local < on * 0.62f) return 0f;
                        return burst.Step(noise.Next(), f2, 3.5f)
                             * Synth.Env(local - on * 0.62f, 0.001f, 0.012f) * 0.9f;
                    case 's':
                        // Щелевой: шипение, растущее к гласной.
                        return hp.Step(noise.Next(), 0.72f) * Mathf.Min(1f, local / 0.012f) * 0.4f;
                    default:
                        // Сонорный: тот же голос, но нос закрыт — вторая
                        // форманта приглушена, громкость вполсилы.
                        float m = glottis.Saw(f0) * 0.8f + noise.Next() * b.Rasp;
                        return (r1.Step(m, f1 * 0.75f, 8f) + r2.Step(m, f2, 6f) * 0.18f)
                             * Mathf.Min(1f, local / 0.01f) * 0.5f;
                }
            }

            float body = local - on;
            float len = Mathf.Max(0.01f, s.Len);
            float env = Mathf.Min(1f, body / 0.014f) * Mathf.Min(1f, (len - body) / 0.035f + 0.15f);
            env = Mathf.Clamp01(env);

            float src = glottis.Saw(f0) * 0.85f + noise.Next() * b.Rasp;
            return (r1.Step(src, f1, 8f) + r2.Step(src, f2, 10f) * 0.55f) * env;
        });
    }

    /// Сбросить кэш клипов: пользуются проверки, которые гоняют банки подряд.
    public static void Forget() => _clips.Clear();
}
