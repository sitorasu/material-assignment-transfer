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

        [Test]
        public void MaterialSlotMapBySubMeshVertexCountTest()
        {
            var sourceRenderer = CreateSkinnedMeshRenderer("Test1", 3, 6, 9);
            var targetRenderer = CreateSkinnedMeshRenderer("Test1", 90, 30, 60);
            var transferer = new Transferer()
            {
                Source = sourceRenderer.gameObject,
                Target = targetRenderer.gameObject,
                Policy = MaterialSlotMapPolicy.BySubMeshVertexCount
            };
            transferer.Transfer();
            Material[] expectedMaterials = {
                sourceRenderer.sharedMaterials[2], // 9 vertices
                sourceRenderer.sharedMaterials[0], // 3 vertices
                sourceRenderer.sharedMaterials[1], // 6 vertices
            };
            Assert.That(targetRenderer.sharedMaterials, Is.EqualTo(expectedMaterials));
        }

        private record SubMeshSpec(int VertexCount, Material Material);

        private SkinnedMeshRenderer CreateSkinnedMeshRenderer(
            string name,
            params SubMeshSpec[] subMeshSpecs
        )
        {
            Assert.That(subMeshSpecs, Is.All.Matches<SubMeshSpec>(spec => spec.VertexCount % 3 == 0));
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
                int[] triangles = Enumerable.Range(0, vertexCount).ToArray();
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