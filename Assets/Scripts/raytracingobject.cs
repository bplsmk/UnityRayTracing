using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class raytracingobject : MonoBehaviour
{
    public RTmaterial material;

    private void OnEnable()
    {
        raytracer.RegisterObject(this);
    }

    private void OnDisable()
    {
        raytracer.UnregisterObject(this);
    }
}
