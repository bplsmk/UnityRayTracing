using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Linq;
using System.Collections.Generic;

public class raytracer : MonoBehaviour
{
    [Header("Ray Tracing Settings")]
    [SerializeField, Range(0, 32)] int maxBounceCount = 4;
    [SerializeField] bool Accumulate;

    [Header("Info")]
	[SerializeField] int numRenderedFrames;
    
    [Header("Anti-Aliasing")]
    [SerializeField, Range(1, 4)] int SSAA = 1; // 1 = off, 2 = 2x SSAA, 4 = 4x SSAA

    [Header("Controls")]
    [Tooltip("Mouse button index to toggle the raytracer (0=left,1=right,2=middle)")]
    [SerializeField] int toggleMouseButton = 1;
    [Tooltip("Enable/disable ray tracing. Toggle at runtime with the configured mouse button.")]
    [SerializeField] bool RayTracingEnabled = true;

    public ComputeShader RayTracingShader;
    public ComputeShader AccumulationShader;

    private RenderTexture _target;
    private Camera _camera;
    private static bool _meshObjectsNeedRebuilding = false;
    private static List<raytracingobject> _rayTracingObjects = new List<raytracingobject>();
    private static List<MeshObject> _meshObjects = new List<MeshObject>();
    private static List<Vector3> _vertices = new List<Vector3>();
    private static List<int> _indices = new List<int>();
    private ComputeBuffer _meshObjectBuffer;
    private ComputeBuffer _vertexBuffer;
    private ComputeBuffer _indexBuffer;
    

    struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix;
        public int indices_offset;
        public int indices_count;
        public RTmaterial material;
    }

    private void Awake()
    {
        _camera = GetComponent<Camera>();
    }

    private void OnDisable()
    {
        _meshObjectBuffer?.Release();
        _vertexBuffer?.Release();
        _indexBuffer?.Release();
    }

    public static void RegisterObject(raytracingobject obj)
    {
        _rayTracingObjects.Add(obj);
        _meshObjectsNeedRebuilding = true;
    }

    public static void UnregisterObject(raytracingobject obj)
    {
        _rayTracingObjects.Remove(obj);
        _meshObjectsNeedRebuilding = true;
    }

    private void RebuildMeshObjectBuffers()
    {
        if (!_meshObjectsNeedRebuilding)
        {
            return;
        }

        _meshObjectsNeedRebuilding = false;

        // Clear all lists
        _meshObjects.Clear();
        _vertices.Clear();
        _indices.Clear();

        // Loop over all objects and gather their data
        foreach (raytracingobject obj in _rayTracingObjects)
        {
            Mesh mesh = obj.GetComponent<MeshFilter>().sharedMesh;

            // Add vertex data
            int firstVertex = _vertices.Count;
            _vertices.AddRange(mesh.vertices);

            // Add index data - if the vertex buffer wasn't empty before, the
            // indices need to be offset
            int firstIndex = _indices.Count;
            var indices = mesh.GetIndices(0);
            _indices.AddRange(indices.Select(index => index + firstVertex));

            // Add the object itself
            _meshObjects.Add(new MeshObject()
            {
                localToWorldMatrix = obj.transform.localToWorldMatrix,
                indices_offset = firstIndex,
                indices_count = indices.Length,
                material = obj.material
            });
        }

        CreateComputeBuffer(ref _meshObjectBuffer, _meshObjects, 92);
        CreateComputeBuffer(ref _vertexBuffer, _vertices, 12);
        CreateComputeBuffer(ref _indexBuffer, _indices, 4);
    }

    private static void CreateComputeBuffer<T>(ref ComputeBuffer buffer, List<T> data, int stride)
        where T : struct
    {
        if (buffer != null)
        {
            // If no data or buffer doesn't match the given criteria, release it
            if (data.Count == 0 || buffer.count != data.Count || buffer.stride != stride)
            {
                buffer.Release();
                buffer = null;
            }
        }

        if (data.Count != 0)
        {
            if (buffer == null)
            {
                buffer = new ComputeBuffer(data.Count, stride);
            }

            buffer.SetData(data);
        }
    }

    private void SetComputeBuffer(string name, ComputeBuffer buffer)
    {
        if (buffer != null && RayTracingShader != null)
        {
            RayTracingShader.SetBuffer(0, name, buffer);
        }
    }

    // Grabbing the already calculated matrices from Unity to our shader
    private void SetShaderParameters()
    {
        RayTracingShader.SetInt("MaxBounceCount", maxBounceCount);

        RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
        RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);

        SetComputeBuffer("_MeshObjects", _meshObjectBuffer);
        SetComputeBuffer("_Vertices", _vertexBuffer);
        SetComputeBuffer("_Indices", _indexBuffer);
    }

    void Update()
    {
        // Checking if obj has moved
        foreach (raytracingobject obj in _rayTracingObjects)
        {
            if (obj.transform.hasChanged)
            {
                _meshObjectsNeedRebuilding = true;
                obj.transform.hasChanged = false;
            }
        }
        
        // Toggle raytracing on/off with configured mouse button
        if (Input.GetMouseButtonDown(toggleMouseButton))
        {
            RayTracingEnabled = !RayTracingEnabled;
            // Reset accumulation when toggling back on
            if (RayTracingEnabled)
            {
                numRenderedFrames = 0;
                // force a rebuild so buffers are up-to-date when re-enabled
                _meshObjectsNeedRebuilding = true;
            }
        }

    }

    private void InitRenderTexture()
    {
        if (_target == null || _target.width != Screen.width || _target.height != Screen.height)
        {
            // Get a render target for Ray Tracing
            _target = new RenderTexture(Screen.width, Screen.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
            _target.enableRandomWrite = true;
            _target.Create();
        }
    }

    private void Render(RenderTexture destination)
    {
        // Create the initial frame
        InitRenderTexture();

        // Determine render resolution for SSAA (render at higher resolution and downsample)
        int rtWidth = Screen.width * Mathf.Max(1, SSAA);
        int rtHeight = Screen.height * Mathf.Max(1, SSAA);

        // Create a copy of previous frame at the SSAA resolution (we'll blit the screen-sized _target into this, Unity will scale)
        RenderTexture previousFrame = RenderTexture.GetTemporary(rtWidth, rtHeight, 0, GraphicsFormat.R32G32B32A32_SFloat);
        previousFrame.enableRandomWrite = true;
        previousFrame.Create();
        Graphics.Blit(_target, previousFrame);

        // Raytrace into a higher-resolution currentFrame
        RenderTexture currentFrame = RenderTexture.GetTemporary(rtWidth, rtHeight, 0, GraphicsFormat.R32G32B32A32_SFloat);
        currentFrame.enableRandomWrite = true;
        currentFrame.Create();
        RayTracingShader.SetInt("frame", numRenderedFrames);
        // Pass per-pixel sample count to the compute shader
        RayTracingShader.SetTexture(0, "Result", currentFrame);

        // Dispatch with ceil division to ensure full coverage even if dimensions not divisible by 8
        int threadGroupX = Mathf.CeilToInt((float)rtWidth / 8.0f);
        int threadGroupY = Mathf.CeilToInt((float)rtHeight / 8.0f);
        RayTracingShader.Dispatch(0, threadGroupX, threadGroupY, 1);


        // Accumulate the previous frame with the current frame (both at SSAA resolution)
        AccumulationShader.SetInt("frame", numRenderedFrames);
        AccumulationShader.SetBool("accumulate", Accumulate);
        AccumulationShader.SetTexture(0, "_PreviousFrame", previousFrame);
        AccumulationShader.SetTexture(0, "Result", currentFrame);
        AccumulationShader.Dispatch(0, threadGroupX, threadGroupY, 1);

        // Downsample the high-res currentFrame to the screen-sized _target (this achieves SSAA)
        Graphics.Blit(currentFrame, _target);

        // Present it to the screen and release the temporary frames
        Graphics.Blit(_target, destination);

        RenderTexture.ReleaseTemporary(previousFrame);
        RenderTexture.ReleaseTemporary(currentFrame);

        if (Application.isPlaying)
        {
            numRenderedFrames += 1;
        }
    }
    
    // OnRenderImage is automatically called by Unity when camera has finished rendering
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // If raytracing is disabled or shaders aren't assigned, just blit the
        // camera image through and skip the raytracing code to avoid null
        // buffer/shader errors.
        if (!RayTracingEnabled || RayTracingShader == null || AccumulationShader == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        RebuildMeshObjectBuffers();
        SetShaderParameters();
        Render(destination);
    }
}
