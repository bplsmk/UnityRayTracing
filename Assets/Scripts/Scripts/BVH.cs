using UnityEngine;
using Unity.Mathematics;
using System;

public class BVH
{
    public BVHTriangle[] BVHTriangles;
    public NodeList nodeList;

    public BVH(Vector3[] vertices, int[] indices)
    {
        BoundingBox BB = new BoundingBox();
        nodeList = new();
        BVHTriangles = new BVHTriangle[indices.Length / 3];

        for (int i = 0; i < indices.Length; i += 3)
        {
            float3 v0 = vertices[indices[i + 0]];
            float3 v1 = vertices[indices[i + 1]];
            float3 v2 = vertices[indices[i + 2]];

            float3 min = math.min(math.min(v0, v1), v2);
            float3 max = math.max(math.max(v0, v1), v2);

            float3 center = (v0 + v1 + v2) / 3;
            int count = 0;
            BVHTriangles[count] = new BVHTriangle(v0, v1, v2, center);
            count += 1;

            BB.AddToBox(min, max);
        }

        nodeList.addtoList(new Node(BB));

        SplitBoundingBox(0, 0, BVHTriangles.Length);
    }

    public void SplitBoundingBox(int index, int triIndex, int triCount, int depth = 0)
    {
        if (depth == 10)
        {
            return;
        }

        Node parent = nodeList.Nodes[index];

        float3 size = parent.max + parent.min;
        int splitDirection = size.x > math.max(size.y, size.z) ? 0 : size.y > size.z ? 1 : 2;
        float splitLocation = Mathf.Lerp(parent.min[splitDirection], parent.max[splitDirection], 0.5f);

        BoundingBox childA = new();
        BoundingBox childB = new();
        int leftIndex = 0;

        for (int i = triIndex; i < triIndex + triCount; i++)
        {
            BVHTriangle triangle = BVHTriangles[i];
            float3 min = math.min(math.min(triangle.v0, triangle.v1), triangle.v2);
            float3 max = math.max(math.max(triangle.v0, triangle.v1), triangle.v2);

            if (triangle.center[splitDirection] < splitLocation)
            {
                childA.AddToBox(min, max);

                BVHTriangle swap = BVHTriangles[triIndex + leftIndex];
                BVHTriangles[triIndex + leftIndex] = triangle;
                BVHTriangles[i] = swap;
                leftIndex++;
            }
            else
            {
                childB.AddToBox(min, max);
            }
        }

        int rightIndex = triCount - leftIndex;
        int leftTriangleIndex = triIndex;
        int rightTriangleIndex = triIndex + leftIndex;

        int leftChildIndex = nodeList.addtoList(new Node(childA, leftTriangleIndex, 0));
        int rightChildIndex = nodeList.addtoList(new Node(childB, rightTriangleIndex, 0));

        parent.Index = leftChildIndex;
        nodeList.Nodes[index] = parent;

        SplitBoundingBox(leftChildIndex, triIndex, leftIndex, depth + 1);
        SplitBoundingBox(rightChildIndex, triIndex + leftIndex, rightIndex, depth + 1);
    }

    public struct BoundingBox
    {
        // aabb means axis-aligned bounding box
        public float3 aabbMin;
        public float3 aabbMax;
        public float3 center => (aabbMin + aabbMax) / 2;
        public float3 size => aabbMax - aabbMin;

        public void AddToBox(float3 min, float3 max)
        {
            aabbMin.x = min.x < aabbMin.x ? min.x : aabbMin.x;
            aabbMin.y = min.y < aabbMin.y ? min.y : aabbMin.y;
            aabbMin.z = min.z < aabbMin.z ? min.z : aabbMin.z;

            aabbMax.x = max.x < aabbMax.x ? min.x : aabbMax.x;
            aabbMax.y = max.y < aabbMax.y ? min.y : aabbMax.y;
            aabbMax.z = max.z < aabbMax.z ? min.z : aabbMax.z;
        }
    }

    public struct Node
    {
        public float3 min;
        public float3 max;
        public int Index;
        public int TriangleCount;

        public Node(BoundingBox boundingbox) : this()
        {
            min = boundingbox.aabbMin;
            max = boundingbox.aabbMax;
            Index = -1;
            TriangleCount = -1;
        }

        public Node(BoundingBox boundingbox, int index, int count)
        {
            this.min = boundingbox.aabbMin;
            this.max = boundingbox.aabbMax;
            this.Index = index;
            this.TriangleCount = count;
        }
    }

    public Node[] GetNodeList() => nodeList.Nodes.AsSpan(0, nodeList.index).ToArray();

    public class NodeList
    {
        public Node[] Nodes = new Node[256];
        public int index;

        public int addtoList(Node node)
        {
            if (index >= Nodes.Length)
            {
                Array.Resize(ref Nodes, Nodes.Length * 2);
            }

            int nodeIndex = index;
            Nodes[index++] = node;
            return nodeIndex;
        }
    }

    public struct BVHTriangle
    {
        public float3 v0;
        public float3 v1;
        public float3 v2;
        public float3 center;

        public BVHTriangle(float3 a, float3 b, float3 c, float3 centerV)
        {
            this.v0 = a;
            this.v1 = b;
            this.v2 = c;
            this.center = centerV;
        }
    }
}