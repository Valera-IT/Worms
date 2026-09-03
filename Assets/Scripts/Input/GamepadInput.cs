using UnityEngine;
using UnityEngine.InputSystem;

/// Геймпад: левый стик — ход и угол, A — прыжок, X — выстрел, бамперы — оружие,
/// правый стик — камера, триггеры — зум.
public class GamepadInput : IHumanInput
{
    const float Dead = 0.22f;

    public InputScheme Scheme => InputScheme.Gamepad;
    public bool Available => Gamepad.current != null;
    public bool Active { get; private set; }

    public float Move { get; private set; }
    public float AimAxis { get; private set; }
    public bool HasAimTarget => false;
    public Vector2 AimTarget => Vector2.zero;
    public bool JumpPressed { get; private set; }
    public bool FirePressed { get; private set; }
    public bool FireHeld { get; private set; }
    public bool FireReleased { get; private set; }
    public int WeaponRequest => -1;
    public int WeaponCycle { get; private set; }

    public float ZoomDelta { get; private set; }
    public Vector2 PanDelta { get; private set; }
    public bool PanActive { get; private set; }
    public bool RestartPressed { get; private set; }

    static float Cut(float v) => Mathf.Abs(v) < Dead ? 0f : Mathf.Sign(v) * (Mathf.Abs(v) - Dead) / (1f - Dead);

    public void Tick()
    {
        Move = 0f; AimAxis = 0f; WeaponCycle = 0;
        JumpPressed = FirePressed = FireHeld = FireReleased = RestartPressed = false;
        ZoomDelta = 0f; PanDelta = Vector2.zero; PanActive = false;
        Active = false;

        var pad = Gamepad.current;
        if (pad == null) return;

        Vector2 left = pad.leftStick.ReadValue();
        Move = Cut(left.x);
        AimAxis = Cut(left.y);

        JumpPressed = pad.buttonSouth.wasPressedThisFrame;
        FirePressed = pad.buttonWest.wasPressedThisFrame;
        FireHeld = pad.buttonWest.isPressed;
        FireReleased = pad.buttonWest.wasReleasedThisFrame;

        if (pad.rightShoulder.wasPressedThisFrame) WeaponCycle = 1;
        else if (pad.leftShoulder.wasPressedThisFrame) WeaponCycle = -1;

        RestartPressed = pad.startButton.wasPressedThisFrame;

        // Триггеры — зум, правый стик — панорама.
        float zoom = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        if (Mathf.Abs(zoom) > 0.05f) ZoomDelta = zoom * 12f * Time.deltaTime;

        Vector2 right = new Vector2(Cut(pad.rightStick.ReadValue().x), Cut(pad.rightStick.ReadValue().y));
        if (right.sqrMagnitude > 0.0001f)
        {
            PanDelta = right * (Screen.height * 0.9f * Time.deltaTime);
            PanActive = true;
        }

        Active = Mathf.Abs(Move) > 0.01f || Mathf.Abs(AimAxis) > 0.01f
              || JumpPressed || FireHeld || FireReleased || WeaponCycle != 0
              || RestartPressed || PanActive || Mathf.Abs(ZoomDelta) > 0.0001f;
    }
}
