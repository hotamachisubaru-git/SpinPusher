using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Drops medals at the pointed playfield position; UI presses remain UI-only.</summary>
[DefaultExecutionOrder(-100)]
public class MedalInputHandler : MonoBehaviour
{
    public MedalPusherGame game;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private bool mouseHoldAllowed;

    void Start()
    {
        if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
    }

    void Update()
    {
        if (game == null) return;
        if (game.SettingsOpen) { mouseHoldAllowed = false; game.StopThrowing(); return; }
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            mouseHoldAllowed = TryAimAtScreenPosition(mouse.position.ReadValue());
            if (mouseHoldAllowed) TryInsertAtScreenPosition(mouse.position.ReadValue());
        }
        if (mouse == null || !mouse.leftButton.isPressed) mouseHoldAllowed = false;
        bool keyboardHeld = keyboard != null && keyboard.spaceKey.isPressed;
        bool mouseHeld = mouse != null && mouse.leftButton.isPressed && mouseHoldAllowed
            && TryAimAtScreenPosition(mouse.position.ReadValue());
        if (keyboardHeld || mouseHeld) game.StartThrowing();
        else game.StopThrowing();
    }

    public bool TryAimAtScreenPosition(Vector2 screenPosition)
    {
        if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
        return game != null && !game.SettingsOpen && !IsPointerOverUI(screenPosition)
            && game.TrySetMedalDropTarget(Camera.main, screenPosition);
    }

    public bool TryInsertAtScreenPosition(Vector2 screenPosition)
    {
        if (!TryAimAtScreenPosition(screenPosition)) return false;
        long before = game.TotalPaidMedals;
        game.ThrowSingleMedal();
        return game.TotalPaidMedals > before;
    }

    private bool IsPointerOverUI(Vector2 screenPosition)
    {
        EventSystem system = EventSystem.current;
        if (system == null) return false;
        // Use current position to avoid EventSystem Update ordering delays.
        PointerEventData pointer = new PointerEventData(system) { position = screenPosition };
        uiHits.Clear();
        system.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.gameObject != null && hit.gameObject.GetComponentInParent<Canvas>() != null) return true;
        return false;
    }

    void OnDisable()
    {
        mouseHoldAllowed = false;
        if (game != null) game.StopThrowing();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) OnDisable();
    }
}
