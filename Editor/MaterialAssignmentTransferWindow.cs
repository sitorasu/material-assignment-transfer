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
            foreach (var item in _transferer.Plan)
            {
                EditorGUILayout.ObjectField(
                    obj: item.Target.gameObject,
                    objType: typeof(GameObject),
                    allowSceneObjects: true
                );
                for (int i = 0; i < item.MaterialSlotMap.Count(); i++)
                {
                    var oldMaterials = item.Target.sharedMaterials;
                    var oldMaterial = i < oldMaterials.Count() ? oldMaterials[i] : null;
                    var newMaterials = item.Source.sharedMaterials;
                    var mappedIndex = item.MaterialSlotMap[i];
                    var newMaterial = mappedIndex < newMaterials.Count() ? newMaterials[mappedIndex] : null;
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

        private void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            _transferer.UpdatePlan();
            Repaint();
        }
    }
}


