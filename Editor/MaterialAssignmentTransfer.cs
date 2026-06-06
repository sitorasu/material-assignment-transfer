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
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Material Assignment Transfer");
            int undoGroup = Undo.GetCurrentGroup();
            foreach (var desc in _plan)
            {
                Undo.RecordObject(desc.Target, "Material Assignment Transfer");
                var materialSlotNum = desc.MaterialSlotMap.Count();
                var sourceMaterialNum = desc.Source.sharedMaterials.Count();
                var targetMaterials = new Material[sourceMaterialNum];
                for (int i = 0; i < materialSlotNum; i++)
                {
                    var mappedIndex = desc.MaterialSlotMap[i];
                    if (mappedIndex < desc.Source.sharedMaterials.Count())
                    {

                        targetMaterials[i] = desc.Source.sharedMaterials[mappedIndex];
                    }
                }
                desc.Target.sharedMaterials = targetMaterials;
            }
            Undo.CollapseUndoOperations(undoGroup);
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
            var sourceDictionary = new Dictionary<string, Renderer>();
            var targetDictionary = new Dictionary<string, Renderer>();
            foreach (var renderer in sourceRenderers)
            {
                if (!sourceDictionary.TryAdd(renderer.name, renderer))
                {
                    _sourceNameConflictRenderers.Add(renderer);
                }
            }
            foreach (var renderer in targetRenderers)
            {
                // 同じ名前のメッシュがソース側になかったら処理対象外
                if (!sourceDictionary.TryGetValue(renderer.name, out Renderer sourceRenderer))
                {
                    continue;
                }
                // 型が一致しなければ対象外
                if (renderer.GetType() != sourceRenderer.GetType())
                {
                    continue;
                }
                // サブメッシュの数が一致しなければ処理対象外
                if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh.subMeshCount != ((SkinnedMeshRenderer)sourceRenderer).sharedMesh.subMeshCount)
                {
                    continue;
                }
                // 既に同じ名前のメッシュが処理対象となっていた場合、頂点数が近い方を採用
                if (!targetDictionary.TryAdd(renderer.name, renderer))
                {
                    if (renderer is SkinnedMeshRenderer challenger)
                    {
                        var challengerVertexCount = challenger.sharedMesh.vertexCount;
                        var candidate = targetDictionary[renderer.name];
                        var candidateVertexCount = ((SkinnedMeshRenderer)candidate).sharedMesh.vertexCount;
                        var sourceVertexCount = ((SkinnedMeshRenderer)sourceRenderer).sharedMesh.vertexCount;
                        var candidateDistance = Math.Abs(candidateVertexCount - sourceVertexCount);
                        var challengerDistance = Math.Abs(challengerVertexCount - sourceVertexCount);
                        if (challengerDistance < candidateDistance)
                        {
                            _targetNameConflictRenderers.Add(renderer);
                        }
                    }
                    else
                    {
                        _targetNameConflictRenderers.Add(renderer);
                    }
                }
            }

            foreach (var (targetName, targetRenderer) in targetDictionary)
            {
                var sourceRenderer = sourceDictionary[targetName];
                int[] materialSlotMap = _policy switch
                {
                    MaterialSlotMapPolicy.ByIndex => Enumerable.Range(0, sourceRenderer.sharedMaterials.Count()).ToArray(),
                    MaterialSlotMapPolicy.BySubMeshVertexCount => GenerateMaterialSlotMapBySubMeshVertexCount(sourceRenderer, targetRenderer),
                    _ => null
                };
                Debug.Assert(materialSlotMap != null, "Unknown MaterialSlotPolicy");
                _plan.Add(new TransferDescription(sourceRenderer, targetRenderer, materialSlotMap));
            }
        }

        private int[] GenerateMaterialSlotMapBySubMeshVertexCount(Renderer source, Renderer target)
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


