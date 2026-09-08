using UnityEngine;

/// Полоса силы прямо на карте: клин, растущий из ствола в сторону прицела —
/// как в «Червях». Полоска внизу экрана заставляла в момент выстрела отводить
/// глаза от червя, то есть ровно от того места, где видно угол и ветер.
public class PowerMeter : MonoBehaviour
{
    /// Где начинается луч и до чего дорастает при полном заряде, юнитов от
    /// центра червя. Дальний конец нарочно уходит за кольцо прицела (оно
    /// стоит в 2,6): полная сила должна быть видна как «до упора».
    const float Near = 0.8f;
    const float Far = 5.2f;

    /// Ширина клина у ствола относительно его ширины на полном заряде: на
    /// четверти силы полоса не только короче, но и тоньше.
    const float ThinStart = 0.45f;

    SpriteRenderer _sr;
    Transform _tr;
    float _unit;    // длина спрайта в юнитах при единичном масштабе

    public static PowerMeter Attach(Transform parent)
    {
        var go = new GameObject("PowerMeter");
        go.transform.SetParent(parent, false);

        var pm = go.AddComponent<PowerMeter>();
        // Порядок ниже прицела: кольцо наводки остаётся поверх луча.
        pm._sr = Sprites.Make("Beam", WeaponIcons.PowerBeam, Color.white, 11, go.transform);
        pm._tr = pm._sr.transform;
        pm._unit = WeaponIcons.PowerBeam.bounds.size.x;
        pm._sr.enabled = false;
        return pm;
    }

    /// Показать набранную долю силы вдоль направления прицела.
    /// Отрицательный заряд убирает полосу.
    public void Show(float charge, Vector2 dir)
    {
        if (charge <= 0f)
        {
            if (_sr.enabled) _sr.enabled = false;
            return;
        }

        _sr.enabled = true;

        float t = Mathf.Clamp01(charge);
        float len = Mathf.Lerp(0.6f, Far - Near, t);

        _tr.localPosition = dir * Near;
        _tr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        // Вдоль — длина луча, поперёк — его толщина. Перелив в картинке идёт
        // поперёк, так что растяжение по длине ничего не размывает.
        _tr.localScale = new Vector3(len / _unit, Mathf.Lerp(ThinStart, 1f, t), 1f);
    }
}
