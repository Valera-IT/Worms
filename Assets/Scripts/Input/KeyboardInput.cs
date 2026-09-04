using UnityEngine;
using UnityEngine.InputSystem;

/// Клавиатура и мышь. Мышь считается частью этой схемы: колесо — зум, средняя кнопка — панорама.
public class KeyboardInput : IHumanInput
{
    public InputScheme Scheme => InputScheme.Keyboard;
    public bool Available => Keyboard.current != null;
    public bool Active { get; private set; }

    public float Move { get; private set; }
    public float AimAxis { get; private set; }
    public bool HasAimTarget => false;
    public Vector2 AimTarget => Vector2.zero;
    public bool JumpPressed { get; private set; }
    public bool FirePressed { get; private set; }
    public bool FireHeld { get; private set; }
    public bool FireReleased { get; private set; }
    public int WeaponRequest { get; private set; }
    public int WeaponCycle { get; private set; }
    public bool SelectWormPressed { get; private set; }

    public float ZoomDelta { get; private set; }
    public Vector2 PanDelta { get; private set; }
    public bool PanActive { get; private set; }
    public bool RestartPressed { get; private set; }

    Vector2 _dragOrigin;
    bool _dragging;

    public void Tick()
    {
        Move = 0f; AimAxis = 0f; WeaponRequest = -1; WeaponCycle = 0;
        JumpPressed = FirePressed = FireReleased = RestartPressed = false;
        SelectWormPressed = false;
        FireHeld = false;
        ZoomDelta = 0f; PanDelta = Vector2.zero;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) Move -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) Move += 1f;

            if (kb.upArrowKey.isPressed || kb.wKey.isPressed) AimAxis += 1f;
            if (kb.downArrowKey.isPressed || kb.sKey.isPressed) AimAxis -= 1f;

            JumpPressed = kb.enterKey.wasPressedThisFrame
                       || kb.numpadEnterKey.wasPressedThisFrame
                       || kb.jKey.wasPressedThisFrame;

            FirePressed = kb.spaceKey.wasPressedThisFrame;
            FireHeld = kb.spaceKey.isPressed;
            FireReleased = kb.spaceKey.wasReleasedThisFrame;

            // Цифры бьют по первым десяти стволам, Q и E листают весь арсенал:
            // клавиш на шестнадцать пунктов не хватает.
            for (int i = 0; i < 9; i++)
            {
                var key = kb[Key.Digit1 + i];
                if (key != null && key.wasPressedThisFrame) { WeaponRequest = i; break; }
            }
            if (kb.digit0Key.wasPressedThisFrame) WeaponRequest = 9;

            if (kb.eKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame) WeaponCycle = 1;
            else if (kb.qKey.wasPressedThisFrame || kb.leftBracketKey.wasPressedThisFrame) WeaponCycle = -1;

            // Tab — другой червь команды, как Backspace в оригинале: та же
            // клавиша «перебрать своих», только под рукой на всех раскладках.
            SelectWormPressed = kb.tabKey.wasPressedThisFrame;

            // R или Esc открывают паузу (прежний перезапуск по R переехал в меню паузы).
            RestartPressed = kb.rKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;
        }

        bool mouseUsed = false;
        var mouse = Mouse.current;
        if (mouse != null)
        {
            // Новый ввод отдаёт колесо в «щелчках по 120», старый API нормализовал их к единице.
            float scroll = mouse.scroll.ReadValue().y / 120f;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                ZoomDelta = Mathf.Clamp(scroll, -3f, 3f) * 1.5f;
                mouseUsed = true;
            }

            Vector2 pos = mouse.position.ReadValue();
            if (mouse.middleButton.wasPressedThisFrame) { _dragging = true; _dragOrigin = pos; }
            if (mouse.middleButton.wasReleasedThisFrame) _dragging = false;
            if (_dragging && !mouse.middleButton.isPressed) _dragging = false;

            if (_dragging)
            {
                PanDelta = pos - _dragOrigin;
                _dragOrigin = pos;
                PanActive = true;
                mouseUsed = true;
            }
            else PanActive = false;
        }
        else PanActive = false;

        Active = mouseUsed
              || Mathf.Abs(Move) > 0.01f || Mathf.Abs(AimAxis) > 0.01f
              || JumpPressed || FireHeld || FireReleased || RestartPressed
              || WeaponRequest >= 0 || WeaponCycle != 0 || SelectWormPressed;
    }
}
