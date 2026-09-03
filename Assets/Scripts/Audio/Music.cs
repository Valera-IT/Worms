using UnityEngine;

/// Фоновая музыка — тоже из кода: петля из четырёх аккордов на мягких голосах,
/// каждый со своим вдохом и выдохом, поэтому шва на стыке петли не слышно.
/// Громкость берётся из настроек (MusicVolume) и живёт отдельно от эффектов.
public static class Music
{
    const float ChordSeconds = 4f;

    /// Ля минор и соседи: минор без резких ходов не мешает думать над выстрелом.
    static readonly float[][] Chords =
    {
        new[] { 220.00f, 261.63f, 329.63f },   // Am
        new[] { 174.61f, 220.00f, 261.63f },   // F
        new[] { 196.00f, 246.94f, 293.66f },   // G
        new[] { 164.81f, 196.00f, 246.94f }    // Em
    };

    static AudioSource _src;
    static AudioClip _clip;
    static float _volume = 0.6f;

    /// 0..1 из настроек. Пэд играет тише эффектов: он фон, а не событие.
    public static float Volume
    {
        get => _volume;
        set
        {
            _volume = Mathf.Clamp01(value);
            if (_src != null) _src.volume = _volume * 0.35f;
        }
    }

    /// Поднять источник и запустить петлю. Зовётся один раз из App.
    public static void Ensure()
    {
        if (!Application.isPlaying) return;
        if (_src != null) { _src.volume = _volume * 0.35f; return; }

        var go = new GameObject("Music");
        Object.DontDestroyOnLoad(go);

        _src = go.AddComponent<AudioSource>();
        _src.clip = _clip != null ? _clip : (_clip = Build());
        _src.loop = true;
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;
        _src.volume = _volume * 0.35f;
        _src.Play();
    }

    /// Клип петли. Считается один раз — 16 секунд моно, около трёх мегабайт.
    public static AudioClip Build()
    {
        float total = Chords.Length * ChordSeconds;

        var saw = new Synth.Osc[3];
        var sub = new Synth.Osc[3];
        var lp = new Synth.LowPass();
        var lfo = new Synth.Osc();

        return Synth.Build("music_pad", total, t =>
        {
            int ci = Mathf.Clamp((int)(t / ChordSeconds), 0, Chords.Length - 1);
            float local = t - ci * ChordSeconds;

            // Вдох и выдох внутри аккорда: к стыку голос уходит в ноль,
            // поэтому петля замыкается без щелчка.
            float e = Mathf.Min(local / 0.9f, (ChordSeconds - local) / 1.1f);
            e = Mathf.Clamp01(e);
            e *= e;

            var ch = Chords[ci];
            float mix = 0f;
            for (int i = 0; i < ch.Length; i++)
            {
                mix += saw[i].Saw(ch[i]) * 0.5f;
                mix += sub[i].Sin(ch[i] * 0.5f) * 0.35f;
            }

            // Медленно открывающийся фильтр — иначе пила скребёт по верхам.
            float k = 0.06f + 0.05f * (lfo.Sin(0.08f) * 0.5f + 0.5f);
            return lp.Step(mix / ch.Length, k) * e;
        });
    }
}
