using UnityEngine;

/// Плавное слежение за целью, ручное панорамирование с инерцией, зум и тряска при
/// взрывах. Адаптивный фреймвью под аспект экрана. Чем именно игрок крутит камеру —
/// колесом, стиком или щипком — решает слой ввода; сюда приходят уже готовые ZoomDelta и PanDelta.
public class CameraRig : MonoBehaviour
{
    Transform _target;
    Camera _cam;
    Vector3 _vel;
    float _shake;
    Vector3 _manualOffset;
    Vector3 _panVel;            // юниты/сек — накат панорамы после отрыва пальца

    Vector3 _holdPoint;         // точка взрыва, на которой задерживаемся
    float _holdLeft;            // сколько ещё её досматривать

    /// Пока true, ход не переключается: игрок смотрит на взрыв.
    public bool IsHolding => _holdLeft > 0f;

    float _idealOrthographicSize;   // рассчитанный размер под аспект экрана
    float _lastScreenWidth;         // для детекта ресайза

    void Awake()
    {
        _cam = GetComponent<Camera>();
        UpdateIdealSize();
    }

    /// force=true перебивает задержку на взрыве: новый снаряд интереснее старой воронки.
    public void Follow(Transform t, bool force = false)
    {
        if (IsHolding && !force) return;
        _holdLeft = 0f;
        _target = t;
        _manualOffset = Vector3.zero;
        _panVel = Vector3.zero;
    }

    /// Задержаться на точке взрыва. Повторные взрывы продлевают окно, поэтому
    /// цепная детонация досматривается целиком, а не обрывается на первой воронке.
    public void Hold(Vector2 point, float seconds)
    {
        _holdPoint = new Vector3(point.x, point.y, 0f);
        _holdLeft = Mathf.Max(_holdLeft, seconds);
        _target = null;
        _manualOffset = Vector3.zero;
        _panVel = Vector3.zero;
    }

    public void Shake(float amount)
    {
        _shake = Mathf.Max(_shake, amount);
    }

    void LateUpdate()
    {
        // Пересчитываем идеальный размер при ресайзе окна
        if (Mathf.Abs(Screen.width - _lastScreenWidth) > 0.1f)
        {
            UpdateIdealSize();
        }

        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var input = GameInput.I;

        // Зум относительно идеального размера: от 0.6x до 1.5x
        float minSize = _idealOrthographicSize * 0.6f;
        float maxSize = _idealOrthographicSize * 1.5f;

        if (input != null && Mathf.Abs(input.ZoomDelta) > 0.0001f)
            _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize - input.ZoomDelta, minSize, maxSize);

        float unitsPerPixel = _cam.orthographicSize * 2f / Screen.height;

        if (input != null && input.PanActive)
        {
            Vector3 step = new Vector3(input.PanDelta.x, input.PanDelta.y, 0f) * -unitsPerPixel;
            _manualOffset += step;
            // Копим скорость только для касаний: мышь и стик тянут камеру без наката.
            if (input.Scheme == InputScheme.Touch)
                _panVel = Vector3.Lerp(_panVel, step / dt, 0.5f);
        }
        else
        {
            if (_panVel.sqrMagnitude > 0.0004f)
            {
                _manualOffset += _panVel * dt;
                _panVel *= Mathf.Pow(0.14f, dt); // гаснет вдвое примерно за 0.35 с
            }
            else
            {
                _panVel = Vector3.zero;
            }
        }

        if (_holdLeft > 0f) _holdLeft -= dt;

        // На чём держим кадр: точка взрыва важнее цели, цель — важнее «стоим где стоим».
        bool hasFocus = true;
        Vector3 focus;
        if (_holdLeft > 0f) focus = _holdPoint;
        else if (_target != null) focus = _target.position;
        else { focus = transform.position; hasFocus = false; }

        Vector3 desired = hasFocus ? focus + _manualOffset : transform.position;

        float halfH = _cam.orthographicSize;
        float halfW = halfH * _cam.aspect;
        desired.x = Mathf.Clamp(desired.x, halfW, Mathf.Max(halfW, DestructibleTerrain.WorldWidth - halfW));
        desired.y = Mathf.Clamp(desired.y, halfH * 0.6f, Mathf.Max(halfH, DestructibleTerrain.WorldHeight - halfH * 0.4f));
        desired.z = -10f;

        // Держим ручной сдвиг в границах карты: у кромки он не растёт бесконечно,
        // а накат об эту кромку гасится, чтобы камеру не «прибивало» к стенке.
        if (hasFocus)
        {
            Vector3 bounded = desired - focus;
            bounded.z = 0f;
            if ((bounded - _manualOffset).sqrMagnitude > 1e-4f) _panVel = Vector3.zero;
            _manualOffset = bounded;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desired, ref _vel, 0.18f);

        if (_shake > 0.001f)
        {
            _shake = Mathf.Max(0f, _shake - Time.deltaTime * 2.2f);
            transform.position += (Vector3)(Random.insideUnitCircle * _shake);
        }
    }

    void UpdateIdealSize()
    {
        // Мир 96×48, аспект 2:1. Камера должна показывать максимум,
        // не превышая границ мира. Для узких экранов (4:3) ограничиваем высотой,
        // для широких (21:9) — шириной.
        float screenAspect = Screen.width / (float)Screen.height;

        // orthographicSize = полвысоты видимой области. Нам нужно:
        // highVisible = 2 * orthographicSize <= 48 => orthographicSize <= 24
        // widthVisible = 2 * orthographicSize * aspect <= 96 => orthographicSize <= 48 / aspect
        _idealOrthographicSize = Mathf.Min(
            24f,                                      // половина высоты мира
            DestructibleTerrain.WorldWidth / (2f * screenAspect)  // половина ширины мира / аспект
        );

        // Если камера была не инициализирована, установим её в идеальный размер
        if (_lastScreenWidth < 1f)
        {
            _cam.orthographicSize = _idealOrthographicSize;
        }

        _lastScreenWidth = Screen.width;
    }
}
