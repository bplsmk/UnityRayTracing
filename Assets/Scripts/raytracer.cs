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
        if (buffer != null)
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

        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);

        if(Accumulate == true)
        {
            // Create a copy of previous frame
            RenderTexture previousFrame = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
            previousFrame.enableRandomWrite = true;
            previousFrame.Create();
            Graphics.Blit(_target, previousFrame);

            RenderTexture currentFrame = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, GraphicsFormat.R32G32B32A32_SFloat);
            currentFrame.enableRandomWrite = true;
            currentFrame.Create();
            RayTracingShader.SetInt("frame", numRenderedFrames);
            RayTracingShader.SetTexture(0, "Result", currentFrame);
            RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            AccumulationShader.SetInt("frame", numRenderedFrames);
            AccumulationShader.SetTexture(0, "_PreviousFrame", previousFrame);
            AccumulationShader.SetTexture(0, "Result", currentFrame);
            AccumulationShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
            Graphics.Blit(currentFrame, _target);

            Graphics.Blit(_target, destination);

            RenderTexture.ReleaseTemporary(previousFrame);
            RenderTexture.ReleaseTemporary(currentFrame);

            if (Application.isPlaying)
            {
                numRenderedFrames += 1;
            }
        }
        else
        {
            if (_target != null)
            {
                _target.Release();
            }
        
            // Set the target and dispatch the compute shader
            // One thread per pixel, 1 group per 8x8 pixel
            RayTracingShader.SetTexture(0, "Result", _target);
            RayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);

            // Send it to the camera
            Graphics.Blit(_target, destination);
        }
    }
    
    // OnRenderImage is automatically called by Unity when camera has finished rendering
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        RebuildMeshObjectBuffers();
        SetShaderParameters();
        Render(destination);
    }
}
