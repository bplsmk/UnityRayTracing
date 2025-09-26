using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class raytracingobject : MonoBehaviour
{
    private void OnEnable()
    {
        raytracer.RegisterObject(this);
    }

    private void OnDisable()
    {
        raytracer.UnregisterObject(this);
    }
}
