using System;
using UnityEngine;

/// Осциллятор, шум и фильтры, из которых собираются все звуки игры. Клипы
/// считаются в рантайме по формуле — бинарных ассетов в проекте по-прежнему нет,
/// ровно как со спрайтами, ландшафтом и интерфейсом.
public static class Synth
{
    public const int Rate = 44100;

    /// Считает клип по функции голоса: на входе время от начала в секундах,
    /// на выходе сэмпл. Амплитуду подбирать руками не нужно — результат
    /// нормируется, иначе один голос шепчет, а другой хрипит на динамике телефона.
    public static AudioClip Build(string name, float seconds, Func<float, float> voice)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
        var data = new float[n];
        float dt = 1f / Rate;

        float peak = 0f;
        for (int i = 0; i < n; i++)
        {
            float s = voice(i * dt);
            data[i] = s;
            float a = s < 0f ? -s : s;
            if (a > peak) peak = a;
        }

        if (peak > 1e-4f)
        {
            float k = 0.92f / peak;
            for (int i = 0; i < n; i++) data[i] *= k;
        }

        // Обрыв волны на последнем сэмпле слышен как щелчок — гасим хвост.
        int fade = Mathf.Min(n, Rate / 300);
        for (int i = 0; i < fade; i++) data[n - 1 - i] *= i / (float)fade;

        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// Огибающая: линейная атака за attack и экспоненциальный спад с постоянной tau.
    public static float Env(float t, float attack, float tau)
    {
        float a = attack <= 0f ? 1f : Mathf.Clamp01(t / attack);
        return a * Mathf.Exp(-t / tau);
    }

    /// Белый шум на своём генераторе: клипы не должны зависеть от состояния
    /// Random движка, иначе один и тот же взрыв звучит по-разному от запуска к запуску.
    public struct Noise
    {
        uint _s;
        public Noise(uint seed) { _s = seed == 0u ? 1u : seed; }

        public float Next()
        {
            _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5;
            return (_s & 0xFFFFFFu) / 8388608f - 1f;   // -1..1
        }
    }

    /// Однополюсный фильтр низких частот: сырой шум звенит и режет ухо.
    /// k — доля новой выборки за шаг, 0..1: меньше — глуше.
    public struct LowPass
    {
        float _y;
        public float Step(float x, float k) { _y += (x - _y) * k; return _y; }
    }

    /// Однополюсный фильтр высоких: убирает бубнёж из шумовых голосов.
    public struct HighPass
    {
        float _prev, _y;
        public float Step(float x, float k) { _y = k * (_y + x - _prev); _prev = x; return _y; }
    }

    /// Резонатор — двухполюсный полосовой фильтр. Однополюсные выше умеют
    /// только глушить и осветлять; чтобы из пилы получился гласный звук, нужна
    /// именно горбатая характеристика: она поднимает узкую полосу вокруг
    /// форманты и оставляет остальное позади. Частоту можно вести по времени —
    /// на этом держатся переходы между слогами в Voice.
    /// q — добротность: чем выше, тем уже горб и «гуще» гласная.
    public struct Reso
    {
        float _y1, _y2;

        public float Step(float x, float freq, float q)
        {
            if (freq < 20f || freq > Rate * 0.45f) return 0f;
            float w = 2f * Mathf.PI * freq / Rate;
            float r = Mathf.Exp(-w / (2f * Mathf.Max(0.5f, q)));
            float y = (1f - r) * x + 2f * r * Mathf.Cos(w) * _y1 - r * r * _y2;
            _y2 = _y1; _y1 = y;
            return y;
        }
    }

    /// Осциллятор с накоплением фазы. Частоту можно вести по времени — sin(2π·f(t)·t)
    /// для свипа не годится: там частота мгновенно расходится с задуманной.
    public struct Osc
    {
        float _p;

        public float Sin(float freq)
        {
            Advance(freq);
            return Mathf.Sin(_p * Mathf.PI * 2f);
        }

        /// Пила без сглаживания — для мягких голосов её достаточно, дальше низкие частоты.
        public float Saw(float freq)
        {
            Advance(freq);
            return _p * 2f - 1f;
        }

        void Advance(float freq)
        {
            _p += freq / Rate;
            _p -= Mathf.Floor(_p);
        }
    }
}
