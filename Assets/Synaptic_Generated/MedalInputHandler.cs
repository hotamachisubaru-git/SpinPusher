using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Supports Space and mouse holds; UI presses remain UI-only.</summary>
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
        if (keyboard != null)
        {
            if (keyboard.digit1Key.wasPressedThisFrame) game.SelectInlet(0);
            if (keyboard.digit2Key.wasPressedThisFrame) game.SelectInlet(1);
            if (keyboard.digit3Key.wasPressedThisFrame) game.SelectInlet(2);
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame) game.SelectInlet(game.selectedInlet - 1);
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame) game.SelectInlet(game.selectedInlet + 1);
        }
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            mouseHoldAllowed = !IsPointerOverUI(mouse);
        if (mouse == null || !mouse.leftButton.isPressed) mouseHoldAllowed = false;
        bool keyboardHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
        bool mouseHeld = mouse != null && mouse.leftButton.isPressed && mouseHoldAllowed;
        if (keyboardHeld || mouseHeld) game.StartThrowing();
        else game.StopThrowing();
    }

    private bool IsPointerOverUI(Mouse mouse)
    {
        EventSystem system = EventSystem.current;
        if (system == null) return false;
        // Use current position to avoid EventSystem Update ordering delays.
        PointerEventData pointer = new PointerEventData(system) { position = mouse.position.ReadValue() };
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
