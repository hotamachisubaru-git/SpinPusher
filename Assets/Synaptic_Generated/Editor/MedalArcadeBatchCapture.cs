using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Renders the game and HUD to a PNG during an isolated batch Play check.</summary>
public static class MedalArcadeBatchCapture
{
    public static void Capture(string filename, string selection = null)
    {
        var view = Object.FindAnyObjectByType<MedalPusherCameraView>();
        var controller = Object.FindAnyObjectByType<MedalSlotJackpotController>();
        if (view == null || controller == null || view.targetCamera == null) return;
        var camera = view.targetCamera;
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        float fov = camera.fieldOfView;
        if (selection == "Cabinet")
        {
            camera.transform.position = new Vector3(24, 21, -29);
            camera.transform.LookAt(new Vector3(0, 5.2f, 7));
            camera.fieldOfView = 45;
        }
        if (selection == "Upper" && controller.upperStation != null)
        {
            camera.transform.position = controller.upperStation.transform.TransformPoint(new Vector3(7.3f, 10.2f, -12.2f));
            camera.transform.LookAt(controller.upperStation.transform.TransformPoint(new Vector3(0, .7f, -3.5f)));
            camera.fieldOfView = 47;
        }
        foreach (var station in controller.stations)
            if (selection == station.kind.ToString())
            {
                camera.transform.position = station.transform.TransformPoint(new Vector3(5.6f, 7.5f, -7.8f));
                camera.transform.LookAt(station.transform.TransformPoint(new Vector3(0, .65f, -.3f)));
                camera.fieldOfView = 43;
            }
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var renderTexture = new RenderTexture(1920, 1080, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        try
        {
            camera.targetTexture = renderTexture;
            foreach (var canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
            }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = renderTexture;
            var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            texture.Apply();
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-expansion-20261004");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, filename), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
        finally
        {
            foreach (var canvas in canvases) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(renderTexture);
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.fieldOfView = fov;
            Canvas.ForceUpdateCanvases();
        }
    }
}
