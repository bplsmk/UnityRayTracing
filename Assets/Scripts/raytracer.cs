using UnityEngine;

[ExecuteAlways, ImageEffectAllowedInSceneView]
public class raytracer : MonoBehaviour
{
    [Header("View Settings")]
	[SerializeField] bool useShaderInSceneView;
    
    public ComputeShader RayTracingShader;

    private RenderTexture _target;
    private Camera _camera;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    // OnRenderImage is automatically called by Unity when camera has finished rendering
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        SetShaderParameters();
        Render(destination);
    }

    private void Render(RenderTexture destination)
    {
        // Make sure we have a current render target
        InitRenderTexture();

        // Set the target and dispatch the compute shader
        // One thread per pixel, 1 group per 8x8 pixel
        RayTracingShader.SetTexture(0, "Result", _target);
        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

        // Run the shader, then draw the result to the screen
        // If the button is on, raytrace in scene view, else render normally
        if (useShaderInSceneView)
        {
            Graphics.Blit(_target, destination);
        }
        else
        {
            Graphics.Blit(null, destination);
        }

    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            // Release render texture if we already have one
            if (_target != null)
            {
                _target.Release();
            }

            // Get a render target for Ray Tracing
            _target = new RenderTexture(Screen.width, Screen.height, 0,
                RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            _target.enableRandomWrite = true;
            _target.Create();
        }
    }

    // Grabbing the already calculated matrices from Unity to our shader
    private void SetShaderParameters()
    {
        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
    }
}
