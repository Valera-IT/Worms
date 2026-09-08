using System.Collections.Generic;
using UnityEngine;

/// Общая логика взрыва: воронка в ландшафте, урон и отбрасывание червей,
/// детонация задетых ящиков.
public static class Combat
{
    /// Ящики, попавшие под взрыв, собираем сюда и рвём после основного цикла:
    /// каждый из них зовёт Detonate заново, а менять список во время обхода нельзя.
    static readonly List<Crate> _chain = new List<Crate>();

    /// soft — попадание одной пули очереди: воронка и урон те же, но без звука,
    /// тряски и задержки камеры. Иначе восемь пуль узи давали бы восемь взрывов
    /// разом: сплошной хрип в динамике и трясущаяся камера на полсекунды.
    public static void Detonate(Vector2 pos, float radius, float damage, bool soft = false)
    {
        GameManager.I.Terrain.Explode(pos, radius);
        Fx.Explosion(pos, radius, soft);
        // Итог выстрела бота: первый взрыв после нажатия на спуск — его.
        BotMemory.NoteBlast(pos);
        if (!soft)
        {
            Sfx.Explosion(radius);
            GameManager.I.Cam.Shake(Mathf.Clamp(radius * 0.22f, 0.15f, 1.2f));
            // Даём досмотреть эффект: вспышка проходит за три кадра (~0,18 с),
            // обломки живут до 1,1 с, дым — до полутора секунд.
            // Чем крупнее воронка, тем дольше пауза; цепные взрывы окно продлевают.
            GameManager.I.Cam.Hold(pos, Mathf.Clamp(0.9f + radius * 0.15f, 1.1f, 2f));
        }

        var worms = GameManager.I.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead) continue;

            Vector2 wp = w.transform.position;
            float dist = Vector2.Distance(wp, pos);
            float hitRange = radius + 0.6f;
            if (dist > hitRange) continue;

            float t = 1f - Mathf.Clamp01(dist / hitRange);
            float dmg = damage * t;

            Vector2 dir = dist < 0.05f ? Vector2.up : (wp - pos).normalized;
            dir = (dir + Vector2.up * 0.35f).normalized;
            w.Knockback(dir * (6f + dmg * 0.32f));
            w.TakeDamage(dmg);
        }

        _chain.Clear();
        for (int i = 0; i < Crate.All.Count; i++)
        {
            var c = Crate.All[i];
            if (c == null) continue;
            if (Vector2.Distance(c.transform.position, pos) <= radius + 0.5f) _chain.Add(c);
        }
        // Копию списка держим локально: Blow вызывает Detonate, а тот снова читает Crate.All.
        var chained = _chain.Count > 0 ? _chain.ToArray() : null;
        if (chained == null) return;
        for (int i = 0; i < chained.Length; i++)
            if (chained[i] != null) chained[i].Blow();
    }
}
