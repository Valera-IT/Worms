using UnityEngine;

/// Телепорт. Дистанцию задаёт набор силы, направление — прицел: одно и то же
/// движение на клавиатуре, стике и пальце, отдельного режима выбора точки не нужно.
/// Внутрь камня не пускает — ищет ближайший просвет над целью.
public static class Teleport
{
    public const float MinRange = 5f;
    public const float MaxRange = 26f;

    /// Максимальный подъём при поиске просвета над целью, в юнитах.
    const float LiftLimit = 9f;

    /// Переносит червя. false — точка не нашлась (сплошная порода до самого верха),
    /// тогда патрон не тратится и ход не заканчивается.
    public static bool Jump(Worm worm, Vector2 dir, float charge)
    {
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        if (terrain == null) return false;

        Vector2 from = worm.transform.position;
        float range = Mathf.Lerp(MinRange, MaxRange, Mathf.Clamp01(charge));
        Vector2 target = from + dir.normalized * range;

        target.x = Mathf.Clamp(target.x, 1.5f, DestructibleTerrain.WorldWidth - 1.5f);
        target.y = Mathf.Clamp(target.y, 0.5f, DestructibleTerrain.WorldHeight - 1.5f);

        if (!FindRoom(terrain, ref target)) return false;

        Fx.Splash(from, new Color(0.7f, 0.5f, 1f), 12, 0.22f);
        Sfx.Teleport();

        worm.PlaceAt(target);

        Fx.Splash(target, new Color(0.7f, 0.5f, 1f), 12, 0.22f);
        if (GameManager.I.Cam != null) GameManager.I.Cam.Follow(worm.transform, true);
        return true;
    }

    /// Поднимает точку, пока червю не станет просторно: нужен свободный столбик
    /// в его рост, иначе телепорт замуровал бы игрока в породе.
    static bool FindRoom(DestructibleTerrain terrain, ref Vector2 p)
    {
        for (float lift = 0f; lift <= LiftLimit; lift += 0.25f)
        {
            var probe = new Vector2(p.x, p.y + lift);
            if (probe.y > DestructibleTerrain.WorldHeight - 1f) break;
            if (terrain.IsSolidWorld(probe)) continue;
            if (terrain.IsSolidWorld(probe + Vector2.up * 0.9f)) continue;
            if (terrain.IsSolidWorld(probe + new Vector2(0.45f, 0.45f))) continue;
            if (terrain.IsSolidWorld(probe + new Vector2(-0.45f, 0.45f))) continue;
            p = probe;
            return true;
        }
        return false;
    }
}
