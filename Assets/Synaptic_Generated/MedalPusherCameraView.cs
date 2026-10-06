using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Switches between the playable station and the complete cabinet.</summary>
public class MedalPusherCameraView : MonoBehaviour
{
    public Camera targetCamera;
    public bool overview;
    private MedalBallLotteryStation lotteryStation;

    void Start() { ApplyView(); }
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame) ToggleView();
    }
    public void ToggleView() { overview = !overview; ApplyView(); }
    public void FocusStation(MedalBallLotteryStation station) { lotteryStation = station; ApplyView(); }
    public void ReleaseStation() { lotteryStation = null; ApplyView(); }
    public void ApplyView()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;
        MedalPusherUI.Instance?.RefreshVisibleTextMeshes();
        if (!overview && lotteryStation != null)
        {
            targetCamera.transform.position = lotteryStation.transform.TransformPoint(lotteryStation.isUpperStation
                ? new Vector3(3f, 13f, -10.8f) : new Vector3(5.6f, 7.5f, -7.8f));
            targetCamera.transform.LookAt(lotteryStation.transform.TransformPoint(lotteryStation.isUpperStation
                ? new Vector3(0, .4f, -2.7f) : new Vector3(0, .65f, -.3f)));
            targetCamera.fieldOfView = 43;
            return;
        }
        targetCamera.transform.position = overview ? new Vector3(24, 21, -29) : new Vector3(0f, 10.3f, -17.2f);
        targetCamera.transform.LookAt(overview ? new Vector3(0, 5.2f, 7) : new Vector3(0, .25f, -.5f));
        targetCamera.fieldOfView = overview ? 45 : 43;
    }
}
