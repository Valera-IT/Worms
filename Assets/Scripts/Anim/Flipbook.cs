using UnityEngine;

/// Самый простой потребитель клипа: крутит его на одном SpriteRenderer.
/// Червю нужен WormAnimator с выбором позы, а куполу и выхлопу — только
/// перебор кадров, и заводить ради этого вторую копию головки незачем.
[DisallowMultipleComponent]
public class Flipbook : MonoBehaviour
{
    SpriteRenderer _sr;
    readonly SpriteAnimPlayer _play = new SpriteAnimPlayer();

    public static Flipbook Attach(SpriteRenderer sr, Sprite[] frames, float fps)
    {
        var f = sr.gameObject.AddComponent<Flipbook>();
        f._sr = sr;
        // Ряды тела и лица у клипа одинаковой длины — здесь это один и тот же
        // ряд: цвет команды реквизит не красит, разъезжаться нечему.
        f._play.Play(new SpriteAnim(sr.name, frames, frames, fps, true));
        return f;
    }

    void LateUpdate()
    {
        if (_sr == null) return;
        _play.Tick(Time.deltaTime);
        _sr.sprite = _play.BodyFrame;
    }
}
