using NUnit.Framework;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace Sitorasu.MaterialAssignmentTransfer
{

    public class MaterialAssignmentTransferTests
    {
        private readonly List<UnityEngine.Object> _createdObjects = new();

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _createdObjects.Add(obj);
            return obj;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
            {
                if (_createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_createdObjects[i]);
                }
            }
        }

        [Test]
        public void MaterialSlotMapByIndexTest()
        {
            var sourceRenderer = CreateSkinnedMeshRenderer("Test", 3, 3, 3);
            var targetRenderer = CreateSkinnedMeshRenderer("Test", 3, 3, 3);
            var transferer = new Transferer()
            {
                Source = sourceRenderer.gameObject,
                Target = targetRenderer.gameObject
            };
            transferer.Transfer();
            Assert.That(targetRenderer.sharedMaterials, Is.EqualTo(sourceRenderer.sharedMaterials));
        }

        private record SubMeshSpec(int VertexCount, Material Material);

        private SkinnedMeshRenderer CreateSkinnedMeshRenderer(
            string name,
            params SubMeshSpec[] subMeshSpecs
        )
        {
            var subMeshCount = subMeshSpecs.Length;
            var totalVertexCount = subMeshSpecs.Sum(spec => spec.VertexCount);
            var materials = subMeshSpecs.Select(spec => spec.Material).ToArray();

            var mesh = Track(new Mesh
            {
                name = name + "_Mesh",
                vertices = new Vector3[totalVertexCount],
                subMeshCount = subMeshCount
            });

            for (int i = 0; i < subMeshCount; i++)
            {
                int vertexCount = subMeshSpecs[i].VertexCount;
                int[] triangles = { 0, 1, 2 };
                mesh.SetTriangles(triangles, i, calculateBounds: false);
            }

            var gameObject = Track(new GameObject(name));
            var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();

            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = materials;

            return renderer;
        }

        private SkinnedMeshRenderer CreateSkinnedMeshRenderer(string name, params int[] subMeshVertexCounts)
        {
            var subMeshSpecs = subMeshVertexCounts.Select(vertexCount => new SubMeshSpec(vertexCount, Track(new Material(Shader.Find("Standard"))))).ToArray();
            return CreateSkinnedMeshRenderer(name, subMeshSpecs);
        }
    }
}