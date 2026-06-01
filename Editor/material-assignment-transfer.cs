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

    class MaterialAssignmentTransfer
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

        private List<TransferDescription> _plan = new List<TransferDescription>();
        public IReadOnlyCollection<TransferDescription> Plan
        {
            get => _plan;
        }

        private List<Renderer> _sourceNameConflictRenderers = new List<Renderer>();
        private List<Renderer> _targetNameConflictRenderers = new List<Renderer>();
        private List<Renderer> _targetMaterialCountMismatchRenderers = new List<Renderer>();

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
                var targetMaterials = new Material[materialSlotNum];
                for (int i = 0; i < materialSlotNum; i++)
                {
                    targetMaterials[i] = desc.Source.sharedMaterials[desc.MaterialSlotMap[i]];
                }
                desc.Target.sharedMaterials = targetMaterials;
            }
            Undo.CollapseUndoOperations(undoGroup);
            // EditorSceneManager.MarkSceneDirty(_target.scene);
        }

        private void UpdatePlan()
        {
            _plan.Clear();
            _sourceNameConflictRenderers.Clear();
            _targetNameConflictRenderers.Clear();
            _targetMaterialCountMismatchRenderers.Clear();

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
                if (!sourceDictionary.TryGetValue(renderer.name, out Renderer sourceRenderer))
                {
                    continue;
                }
                if (renderer.sharedMaterials.Count() != sourceRenderer.sharedMaterials.Count())
                {
                    _targetMaterialCountMismatchRenderers.Add(renderer);
                    continue;
                }
                if (!targetDictionary.TryAdd(renderer.name, renderer))
                {
                    _targetNameConflictRenderers.Add(renderer);
                }
            }

            foreach (var (sourceName, sourceRenderer) in sourceDictionary)
            {
                var targetRenderer = targetDictionary[sourceName];
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

    class MaterialAssignmentTransferWindow : EditorWindow
    {
        private MaterialAssignmentTransfer _transferer;

        // ログのスクロール位置
        private Vector2 _logScrollPosition;

        [MenuItem("Tools/Material Assignment Transfer")]
        private static void ShowWindow()
        {
            GetWindow<MaterialAssignmentTransferWindow>("Material Assignment Transfer");
        }

        private void OnEnable()
        {
            _transferer = new MaterialAssignmentTransfer();
        }

        private void OnGUI()
        {
            EditorGUIUtility.labelWidth = 60;
            EditorGUILayout.LabelField("マテリアル割り当てをコピーします。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            _transferer.Source = (GameObject)EditorGUILayout.ObjectField(
                label: "コピー元",
                obj: _transferer.Source,
                objType: typeof(GameObject),
                allowSceneObjects: true
            );
            _transferer.Target = (GameObject)EditorGUILayout.ObjectField(
                label: "ターゲット",
                obj: _transferer.Target,
                objType: typeof(GameObject),
                allowSceneObjects: true
            );

            // ターゲットがprefabの場合はエラーを出す
            if (_transferer.Target != null && EditorUtility.IsPersistent(_transferer.Target))
            {
                EditorGUILayout.HelpBox("ターゲットにはシーン上のオブジェクトを指定してください", MessageType.Error);
            }
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("マテリアルスロットの対応付け方式", EditorStyles.wordWrappedLabel);
            _transferer.Policy = (MaterialSlotMapPolicy)GUILayout.Toolbar((int)_transferer.Policy, new[] { "番号", "サブメッシュの頂点数" }, EditorStyles.radioButton);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("実行計画", EditorStyles.wordWrappedLabel);
            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition);
            foreach (var item in _transferer.Plan)
            {
                EditorGUILayout.ObjectField(
                    obj: item.Target.gameObject,
                    objType: typeof(GameObject),
                    allowSceneObjects: true
                );
                for (int i = 0; i < item.MaterialSlotMap.Count(); i++)
                {
                    var oldMaterial = item.Target.sharedMaterials[i];
                    var newMaterial = item.Source.sharedMaterials[item.MaterialSlotMap[i]];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Slot {i}", GUILayout.Width(50));
                    EditorGUILayout.ObjectField(
                        obj: oldMaterial,
                        objType: typeof(Material),
                        allowSceneObjects: true
                    );
                    EditorGUILayout.LabelField("⇒", GUILayout.Width(20));
                    EditorGUILayout.ObjectField(
                        obj: newMaterial,
                        objType: typeof(Material),
                        allowSceneObjects: true
                    );
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();

            EditorGUI.BeginDisabledGroup(!_transferer.IsInputValid());
            if (GUILayout.Button("実行！", GUILayout.Height(30)))
            {
                _transferer.Transfer();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space();
        }
    }
}


