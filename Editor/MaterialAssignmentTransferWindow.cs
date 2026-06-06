using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sitorasu.MaterialAssignmentTransfer
{
    class MaterialAssignmentTransferWindow : EditorWindow
    {
        private Transferer _transferer = new();

        // ログのスクロール位置
        private Vector2 _logScrollPosition;

        [MenuItem("Tools/sitorasu's tools/Material Assignment Transfer")]
        private static void ShowWindow()
        {
            GetWindow<MaterialAssignmentTransferWindow>("Material Assignment Transfer");
        }

        private void OnEnable()
        {
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
        }

        private void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
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
            _logScrollPosition = EditorGUILayout.BeginScrollView(_logScrollPosition, EditorStyles.helpBox);
            var effectivePlan = GetEffectivePlan(_transferer.Plan);
            if (effectivePlan.Count() > 0)
            {
                foreach (var desc in effectivePlan)
                {
                    EditorGUILayout.ObjectField(
                        obj: desc.Target.gameObject,
                        objType: typeof(GameObject),
                        allowSceneObjects: true
                    );
                    for (int i = 0; i < desc.MaterialSlotMap.Count(); i++)
                    {
                        var oldMaterials = desc.Target.sharedMaterials;
                        var oldMaterial = i < oldMaterials.Count() ? oldMaterials[i] : null;
                        var newMaterials = desc.Source.sharedMaterials;
                        var mappedIndex = desc.MaterialSlotMap[i];
                        var newMaterial = mappedIndex < newMaterials.Count() ? newMaterials[mappedIndex] : null;
                        EditorGUILayout.BeginHorizontal();
                        var labelStyle = EditorStyles.label;
                        if (oldMaterial != newMaterial)
                        {
                            labelStyle = new GUIStyle(EditorStyles.boldLabel);
                            labelStyle.normal.textColor = Color.green;
                        }
                        EditorGUILayout.LabelField($"[{i}]", labelStyle, GUILayout.Width(20));
                        EditorGUILayout.ObjectField(
                            obj: oldMaterial,
                            objType: typeof(Material),
                            allowSceneObjects: true
                        );
                        EditorGUILayout.LabelField("⇒", GUILayout.Width(15));
                        EditorGUILayout.ObjectField(
                            obj: newMaterial,
                            objType: typeof(Material),
                            allowSceneObjects: true
                        );
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.Space();
                }
            }
            else if (_transferer.IsInputValid())
            {
                EditorGUILayout.LabelField("ターゲットに対して行うべき変更はありません。", EditorStyles.wordWrappedLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();

            EditorGUI.BeginDisabledGroup(!_transferer.IsInputValid() || effectivePlan.Count() == 0);
            if (GUILayout.Button("実行！", GUILayout.Height(30)))
            {
                _transferer.Transfer();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space();
        }

        private static IReadOnlyCollection<TransferDescription> GetEffectivePlan(IReadOnlyCollection<TransferDescription> plan)
        {
            var newPlan = new List<TransferDescription>();
            foreach (var desc in plan)
            {
                var targetMaterials = desc.Target.sharedMaterials;
                var sourceMaterials = desc.Source.sharedMaterials;
                for (int i = 0; i < desc.MaterialSlotMap.Count(); i++)
                {
                    var oldMaterial = i < targetMaterials.Count() ? targetMaterials[i] : null;
                    var mappedIndex = desc.MaterialSlotMap[i];
                    var newMaterial = mappedIndex < sourceMaterials.Count() ? sourceMaterials[mappedIndex] : null;
                    if (oldMaterial != newMaterial)
                    {
                        newPlan.Add(desc);
                        break;
                    }
                }
            }
            return newPlan;
        }

        private void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            _transferer.UpdatePlan();
            Repaint();
        }
    }
}


