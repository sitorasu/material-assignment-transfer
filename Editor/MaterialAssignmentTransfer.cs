using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class IsExternalInit
    {
    }
}

namespace Sitorasu.MaterialAssignmentTransfer
{
    enum MaterialSlotMapPolicy
    {
        ByIndex,
        BySubMeshVertexCount
    }

    record TransferDescription(Renderer Source, Renderer Target, int[] MaterialSlotMap);

    class Transferer
    {
        private GameObject _source;
        public GameObject Source
        {
            get => _source;
            set
            {
                if (_source != value)
                {
                    _source = value;
                    UpdatePlan();
                }
            }
        }

        private GameObject _target;
        public GameObject Target
        {
            get => _target;
            set
            {
                if (_target != value)
                {
                    _target = value;
                    UpdatePlan();
                }
            }
        }

        private MaterialSlotMapPolicy _policy = MaterialSlotMapPolicy.ByIndex;
        public MaterialSlotMapPolicy Policy
        {
            get => _policy;
            set
            {
                if (_policy != value)
                {
                    _policy = value;
                    UpdatePlan();
                }
            }
        }

        private readonly List<TransferDescription> _plan = new();
        public IReadOnlyCollection<TransferDescription> Plan
        {
            get => _plan;
        }

        private readonly List<Renderer> _sourceNameConflictRenderers = new();
        private readonly List<Renderer> _targetNameConflictRenderers = new();

        public bool IsInputValid()
        {
            return _source != null && _target != null && !EditorUtility.IsPersistent(_target);
        }

        public void Transfer()
        {
            if (!IsInputValid())
            {
                return;
            }
            var targets = _plan.Select(desc => desc.Target).Distinct().ToArray();
            Undo.RecordObjects(targets, "Material Assignment Transfer");
            int undoGroup = Undo.GetCurrentGroup();
            foreach (var desc in _plan)
            {
                var sourceMaterials = desc.Source.sharedMaterials;
                var newMaterials = new Material[desc.MaterialSlotMap.Count()];
                for (int i = 0; i < desc.MaterialSlotMap.Count(); i++)
                {
                    var mappedIndex = desc.MaterialSlotMap[i];
                    var newMaterial = mappedIndex < sourceMaterials.Count() ? sourceMaterials[mappedIndex] : null;
                    newMaterials[i] = newMaterial;
                }
                desc.Target.sharedMaterials = newMaterials;
            }
        }

        public void UpdatePlan()
        {
            _plan.Clear();
            _sourceNameConflictRenderers.Clear();
            _targetNameConflictRenderers.Clear();

            if (!IsInputValid())
            {
                return;
            }

            var sourceRenderers = _source.GetComponentsInChildren<Renderer>(includeInactive: true);
            var targetRenderers = _target.GetComponentsInChildren<Renderer>(includeInactive: true);
            var sourceGroups = sourceRenderers.GroupBy(renderer => renderer.name).ToDictionary(group => group.Key, group => group.ToList());
            var targetGroups = targetRenderers.GroupBy(renderer => renderer.name).ToDictionary(group => group.Key, group => group.ToList());

            foreach (var (rendererName, targetGroup) in targetGroups)
            {
                if (!sourceGroups.TryGetValue(rendererName, out var sourceGroup))
                {
                    continue;
                }

                var rendererPairs = GenerateRendererPairs(sourceGroup, targetGroup);

                foreach (var (sourceRenderer, targetRenderer) in rendererPairs)
                {
                    int[] materialSlotMap = _policy switch
                    {
                        MaterialSlotMapPolicy.ByIndex => Enumerable.Range(0, sourceRenderer switch
                        {
                            SkinnedMeshRenderer r => r.sharedMesh.subMeshCount,
                            _ => sourceRenderer.sharedMaterials.Count()
                        }).ToArray(),
                        MaterialSlotMapPolicy.BySubMeshVertexCount => GenerateMaterialSlotMapBySubMeshVertexCount(sourceRenderer, targetRenderer),
                        _ => null
                    };
                    Debug.Assert(materialSlotMap != null, "Unknown MaterialSlotPolicy");
                    _plan.Add(new TransferDescription(sourceRenderer, targetRenderer, materialSlotMap));
                }
            }
        }

        private static IReadOnlyCollection<(Renderer Source, Renderer Target)> GenerateRendererPairs(IReadOnlyList<Renderer> sourceRenderers, IReadOnlyList<Renderer> targetRenderers)
        {
            var pairs = new List<(Renderer Source, Renderer Target)>();
            var candidates = new List<(Renderer Source, Renderer Target, int SourceIndex, int TargetIndex, int Distance)>();

            for (int sourceIndex = 0; sourceIndex < sourceRenderers.Count; sourceIndex++)
            {
                for (int targetIndex = 0; targetIndex < targetRenderers.Count; targetIndex++)
                {
                    var sourceRenderer = sourceRenderers[sourceIndex];
                    var targetRenderer = targetRenderers[targetIndex];
                    if (!CanTransfer(sourceRenderer, targetRenderer))
                    {
                        continue;
                    }

                    candidates.Add((sourceRenderer, targetRenderer, sourceIndex, targetIndex, GetRendererDistance(sourceRenderer, targetRenderer)));
                }
            }

            var used = new HashSet<Renderer>();
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.SourceIndex).ThenBy(candidate => candidate.TargetIndex))
            {
                if (!used.Contains(candidate.Source) && !used.Contains(candidate.Target))
                {
                    pairs.Add((candidate.Source, candidate.Target));
                    used.Add(candidate.Source);
                    used.Add(candidate.Target);
                }
            }

            return pairs;
        }

        private static bool CanTransfer(Renderer source, Renderer target)
        {
            if (target.GetType() != source.GetType())
            {
                return false;
            }

            if (source is SkinnedMeshRenderer sourceSkinned && target is SkinnedMeshRenderer targetSkinned)
            {
                return sourceSkinned.sharedMesh.subMeshCount == targetSkinned.sharedMesh.subMeshCount;
            }

            return true;
        }

        private static int GetRendererDistance(Renderer source, Renderer target)
        {
            if (source is SkinnedMeshRenderer sourceSkinned && target is SkinnedMeshRenderer targetSkinned)
            {
                return Math.Abs(sourceSkinned.sharedMesh.vertexCount - targetSkinned.sharedMesh.vertexCount);
            }

            return 0;
        }

        private static int[] GenerateMaterialSlotMapBySubMeshVertexCount(Renderer source, Renderer target)
        {
            if (source is SkinnedMeshRenderer sourceSkinned && target is SkinnedMeshRenderer targetSkinned)
            {
                // ①ソース側のメッシュについて、サブメッシュの番号→頂点数の順位 の対応を計算
                var subMeshCount = sourceSkinned.sharedMesh.subMeshCount;
                var sourceSubMeshVertexCounts = Enumerable.Range(0, subMeshCount).Select(i => sourceSkinned.sharedMesh.GetSubMesh(i).vertexCount).ToArray();
                var sourceIndexToRank = Enumerable.Range(0, subMeshCount).ToArray();
                Array.Sort(sourceSubMeshVertexCounts, sourceIndexToRank);

                // ②ターゲット側のメッシュについて、頂点数の順位→サブメッシュの番号 の対応を計算
                Debug.Assert(subMeshCount == targetSkinned.sharedMesh.subMeshCount);
                var targetSubMeshVertexCounts = Enumerable.Range(0, subMeshCount).Select(i => targetSkinned.sharedMesh.GetSubMesh(i).vertexCount).ToArray();
                var targetIndexToRank = Enumerable.Range(0, subMeshCount).ToArray();
                Array.Sort(targetSubMeshVertexCounts, targetIndexToRank);
                var targetRankToIndex = new int[subMeshCount];
                for (int i = 0; i < subMeshCount; i++)
                {
                    targetRankToIndex[targetIndexToRank[i]] = i;
                }

                // ①と②を合成
                var materialSlotMap = Enumerable.Range(0, subMeshCount).Select(i => targetRankToIndex[sourceIndexToRank[i]]).ToArray();
                return materialSlotMap;
            }
            else
            {
                return Enumerable.Range(0, source.sharedMaterials.Count()).ToArray();
            }
        }
    }
}


