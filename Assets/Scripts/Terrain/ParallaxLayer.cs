using UnityEngine;

/// Слой заднего плана, отстающий от камеры. Factor 0 — слой стоит в мире,
/// 1 — приклеен к камере (бесконечно далеко). Небо, хребты и облака отличаются
/// только этим числом, поэтому глубина сцены получается без единого ассета.
public class ParallaxLayer : MonoBehaviour
{
    public float Factor = 0.5f;

    /// Собственный дрейф в юнитах в секунду — им плывут облака.
    public Vector2 Drift;

    /// Ширина повтора: уплывшее за край облако возвращается с другой стороны.
    public float WrapWidth;

    Vector3 _home;      // где слой стоял бы при камере в _camHome
    Vector3 _camHome;
    Vector2 _drifted;
    Transform _cam;

    public static ParallaxLayer Attach(GameObject go, float factor, Vector2 drift = default, float wrapWidth = 0f)
    {
        var p = go.AddComponent<ParallaxLayer>();
        p.Factor = factor;
        p.Drift = drift;
        p.WrapWidth = wrapWidth;
        return p;
    }

    void Start()
    {
        _cam = Camera.main != null ? Camera.main.transform : null;
        _home = transform.position;
        _camHome = _cam != null ? _cam.position : Vector3.zero;
    }

    void LateUpdate()
    {
        if (_cam == null)
        {
            _cam = Camera.main != null ? Camera.main.transform : null;
            if (_cam == null) return;
            _camHome = _cam.position;
        }

        _drifted += Drift * Time.deltaTime;
        if (WrapWidth > 0f && Mathf.Abs(_drifted.x) > WrapWidth) _drifted.x -= Mathf.Sign(_drifted.x) * WrapWidth;

        var d = _cam.position - _camHome;
        transform.position = new Vector3(
            _home.x + d.x * Factor + _drifted.x,
            _home.y + d.y * Factor + _drifted.y,
            _home.z);
    }
}
