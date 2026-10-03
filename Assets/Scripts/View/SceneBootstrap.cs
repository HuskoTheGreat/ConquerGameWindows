using UnityEngine;

namespace Catan.View
{
    /// <summary>Makes sure an otherwise empty scene has a camera and a light, so demos run with zero setup.</summary>
    public static class SceneBootstrap
    {
        public static void EnsureCameraAndLight()
        {
            if (Camera.main == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = go.AddComponent<Camera>();
                cam.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun");
                sun.AddComponent<Light>().type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            }
        }
    }
}
