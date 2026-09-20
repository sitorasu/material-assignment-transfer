using NUnit.Framework;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

namespace Sitorasu.MaterialAssignmentTransfer
{

    public class MaterialAssignmentTransferTests
    {
        private enum RendererKind
        {
            SkinnedMesh,
            Mesh
        }

        private record RendererSpec(string Name, RendererKind Kind, int[] SubMeshVertexCounts);

        private record RendererGroup(GameObject Root, IReadOnlyList<Renderer> Renderers);

        private readonly List<UnityEngine.Object> _createdObjects = new();

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _createdObjects.Add(obj);
            return obj;
        }

        private Mesh CreateMesh(string name, params int[] subMeshVertexCounts)
        {
            Assert.That(subMeshVertexCounts, Is.All.Matches<int>(count => count % 3 == 0));
            var subMeshCount = subMeshVertexCounts.Length;
            var totalVertexCount = subMeshVertexCounts.Sum();

            var mesh = Track(new Mesh
            {
                name = name,
                vertices = new Vector3[totalVertexCount],
                subMeshCount = subMeshCount
            });

            for (int i = 0; i < subMeshCount; i++)
            {
                int vertexCount = subMeshVertexCounts[i];
                int[] triangles = Enumerable.Range(0, vertexCount).ToArray();
                mesh.SetTriangles(triangles, i, calculateBounds: false);
            }

            return mesh;
        }

        private Renderer CreateRenderer(
            string name,
            RendererKind kind,
            params int[] subMeshVertexCounts
        )
        {
            var gameObject = Track(new GameObject(name));
            var mesh = CreateMesh(name + "_Mesh", subMeshVertexCounts);
            switch (kind)
            {
                case RendererKind.Mesh:
                    {
                        var renderer = gameObject.AddComponent<MeshRenderer>();
                        var filter = gameObject.AddComponent<MeshFilter>();
                        filter.sharedMesh = mesh;
                        renderer.sharedMaterials = subMeshVertexCounts.Select(_ => Track(new Material(Shader.Find("Standard")))).ToArray();
                        return renderer;
                    }
                case RendererKind.SkinnedMesh:
                    {
                        var renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
                        renderer.sharedMesh = mesh;
                        renderer.sharedMaterials = subMeshVertexCounts.Select(_ => Track(new Material(Shader.Find("Standard")))).ToArray();
                        return renderer;
                    }
                default:
                    Assert.Fail("Unsupported renderer kind: " + kind);
                    return null;
            }
        }

        private RendererGroup CreateRendererGroup(string rootName, params RendererSpec[] rendererSpecs)
        {
            var renderers = new List<Renderer>();
            var rootGameObject = Track(new GameObject(rootName));
            foreach (var spec in rendererSpecs)
            {
                Renderer renderer = CreateRenderer(spec.Name, spec.Kind, spec.SubMeshVertexCounts);
                renderer.transform.SetParent(rootGameObject.transform, false);
                renderers.Add(renderer);
            }
            return new RendererGroup(rootGameObject, renderers);
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
            var sourceRenderer = CreateRenderer("Test", RendererKind.SkinnedMesh, 3, 3, 3);
            var targetRenderer = CreateRenderer("Test", RendererKind.SkinnedMesh, 3, 3, 3);
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
            var sourceRenderer = CreateRenderer("Test1", RendererKind.SkinnedMesh, 3, 6, 9);
            var targetRenderer = CreateRenderer("Test1", RendererKind.SkinnedMesh, 90, 30, 60);
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

        [Test]
        public void RendererTypeMatchingTest()
        {
            var source = CreateRendererGroup(
                "Source",
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6, 9 }),
                new RendererSpec("Mesh", RendererKind.Mesh, new[] { 3, 6, 9 })
            );
            var target = CreateRendererGroup(
                "Target",
                new RendererSpec("Mesh", RendererKind.Mesh, new[] { 3, 6, 9 }),
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6, 9 })
            );
            var transferer = new Transferer()
            {
                Source = source.Root,
                Target = target.Root
            };
            transferer.Transfer();
            Assert.That(target.Renderers[0].sharedMaterials, Is.EqualTo(source.Renderers[1].sharedMaterials));
            Assert.That(target.Renderers[1].sharedMaterials, Is.EqualTo(source.Renderers[0].sharedMaterials));
        }

        [Test]
        public void SubMeshCountMatchingTest()
        {
            var source = CreateRendererGroup(
                "Source",
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6 }),
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6, 9 })
            );
            var target = CreateRendererGroup(
                "Target",
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6, 9 }),
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6 })
            );
            var transferer = new Transferer()
            {
                Source = source.Root,
                Target = target.Root
            };
            transferer.Transfer();
            Assert.That(target.Renderers[0].sharedMaterials, Is.EqualTo(source.Renderers[1].sharedMaterials));
            Assert.That(target.Renderers[1].sharedMaterials, Is.EqualTo(source.Renderers[0].sharedMaterials));
        }

        [Test]
        public void VertexCountMatchingTest()
        {
            var source = CreateRendererGroup(
                "Source",
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6, 9 }),
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 30, 60, 90 })
            );
            var target = CreateRendererGroup(
                "Target",
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 30, 60, 90 }),
                new RendererSpec("Skinned", RendererKind.SkinnedMesh, new[] { 3, 6, 9 })
            );
            var transferer = new Transferer()
            {
                Source = source.Root,
                Target = target.Root
            };
            transferer.Transfer();
            Assert.That(target.Renderers[0].sharedMaterials, Is.EqualTo(source.Renderers[1].sharedMaterials));
            Assert.That(target.Renderers[1].sharedMaterials, Is.EqualTo(source.Renderers[0].sharedMaterials));
        }
    }
}