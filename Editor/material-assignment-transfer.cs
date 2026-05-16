using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class MaterialAssignmentTransferWindow : EditorWindow
{
    // 選択するプレハブ（アセット）
    private GameObject prefab;

    // マテリアルを適用するターゲット（シーン上の GameObject）
    private GameObject targetObject;

    // 適用先マテリアルインデックスの決定方式
    private enum MaterialIndexMapPolicy
    {
        ByIndex,
        BySubMeshVertexCount
    }
    private MaterialIndexMapPolicy policy;

    [MenuItem("Tools/Material Assignment Transfer")]
    private static void ShowWindow()
    {
        var window = GetWindow<MaterialAssignmentTransferWindow>();
        window.titleContent = new GUIContent("Material Assignment Transfer");
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Prefab からマテリアルをコピーして適用します。", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space();

        prefab = (GameObject)EditorGUILayout.ObjectField(
            "Prefab",
            prefab,
            typeof(GameObject),
            false // Prefab アセットを想定
        );

        targetObject = (GameObject)EditorGUILayout.ObjectField(
            "ターゲット GameObject",
            targetObject,
            typeof(GameObject),
            true // シーン上オブジェクトを想定
        );
        if (targetObject != null && !targetObject.scene.IsValid())
        {
            EditorGUILayout.HelpBox("ターゲットにはシーン上のオブジェクトを指定してください", MessageType.Error);
        }
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("適用先マテリアルインデックスの決定方式", EditorStyles.wordWrappedLabel);
        policy = (MaterialIndexMapPolicy)GUILayout.Toolbar((int)policy, new[] { "インデックス", "サブメッシュの頂点数" }, EditorStyles.radioButton);
        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(prefab == null || targetObject == null || !targetObject.scene.IsValid());
        if (GUILayout.Button("適用"))
        {
            ApplyMaterials(policy);
        }
        EditorGUI.EndDisabledGroup();
    }

    private Material[] GetMaterialsReorderedBySubMeshVertexCount(SkinnedMeshRenderer prefabRenderer, SkinnedMeshRenderer targetRenderer)
    {
        // Prefab側のメッシュのマテリアルをサブメッシュの頂点数でソートする
        var subMeshCount = prefabRenderer.sharedMesh.subMeshCount;
        var prefabSubMeshVertexCounts = Enumerable.Range(0, subMeshCount).Select(i => prefabRenderer.sharedMesh.GetSubMesh(i).vertexCount).ToArray();
        var sortedPrefabMats = prefabRenderer.sharedMaterials;
        Array.Sort(prefabSubMeshVertexCounts, sortedPrefabMats);

        // ターゲット側のメッシュのサブメッシュが、頂点数でソートしたときに何番目になるのか計算する
        var targetSubMeshVertexCounts = Enumerable.Range(0, subMeshCount).Select(i => targetRenderer.sharedMesh.GetSubMesh(i).vertexCount).ToArray(); 
        var targetOriginalIndices = Enumerable.Range(0, subMeshCount).ToArray();
        Array.Sort(targetSubMeshVertexCounts, targetOriginalIndices);
        var targetSortedIndices = new int[subMeshCount];
        for (int i = 0; i < subMeshCount; i++)
        {
            targetSortedIndices[targetOriginalIndices[i]] = i;
        }

        // ターゲット側のサブメッシュの頂点数の順位を基準にPrefab側のマテリアルを並び替えて返す
        var reorderedPrefabMats = Enumerable.Range(0, subMeshCount).Select(i => sortedPrefabMats[targetSortedIndices[i]]).ToArray();
        return reorderedPrefabMats;
    }

    private void ApplyMaterials(MaterialIndexMapPolicy policy)
    {
        if (prefab == null || targetObject == null)
        {
            Debug.LogWarning("Prefab またはターゲット GameObject が設定されていません。");
            return;
        }

        // Prefab 側の SkinnedMeshRenderer 一覧
        var prefabRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        // ターゲット側の SkinnedMeshRenderer 一覧
        var targetRenderers = targetObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // 名前で引けるように Dictionary 化
        var prefabDict = new Dictionary<string, SkinnedMeshRenderer>();
        var targetDict = new Dictionary<string, SkinnedMeshRenderer>();

        foreach (var r in prefabRenderers)
        {
            if (!prefabDict.ContainsKey(r.name))
            {
                prefabDict.Add(r.name, r);
            }
            else
            {
                Debug.LogWarning($"Prefab 内に同じ名前のメッシュが複数あります: {r.name}");
            }
        }

        foreach (var r in targetRenderers)
        {
            if (!targetDict.ContainsKey(r.name))
            {
                targetDict.Add(r.name, r);
            }
            else
            {
                Debug.LogWarning($"ターゲット側に同じ名前のメッシュが複数あります: {r.name}");
            }
        }

        int applyCount = 0;
        int warnCount = 0;

        // 1) Prefab 側の全メッシュについて、同名ターゲットにマテリアルをコピー
        foreach (var kv in prefabDict)
        {
            string meshName = kv.Key;
            var prefabRenderer = kv.Value;

            if (!targetDict.TryGetValue(meshName, out var targetRenderer))
            {
                Debug.LogWarning($"ターゲット側に同名のメッシュが見つかりません: {meshName}");
                warnCount++;
                continue;
            }

            var prefabMats = prefabRenderer.sharedMaterials;
            var targetMats = targetRenderer.sharedMaterials;

            if (prefabMats == null) prefabMats = new Material[0];
            if (targetMats == null) targetMats = new Material[0];

            if (prefabMats.Length != targetMats.Length)
            {
                Debug.LogWarning(
                    $"メッシュ '{meshName}' のマテリアル数が一致しません。" +
                    $" Prefab: {prefabMats.Length}, ターゲット: {targetMats.Length}。このメッシュには何もしません。"
                );
                warnCount++;
                continue;
            }

            // Undo 対応
            Undo.RecordObject(targetRenderer, "Apply Prefab Materials");

            targetRenderer.sharedMaterials = policy switch
            {
                MaterialIndexMapPolicy.ByIndex => prefabMats,
                MaterialIndexMapPolicy.BySubMeshVertexCount => GetMaterialsReorderedBySubMeshVertexCount(prefabRenderer, targetRenderer),
                _ => prefabMats
            };
            
            applyCount++;
        }

        // 2) ターゲット側にだけ存在するメッシュを警告
        foreach (var kv in targetDict)
        {
            string meshName = kv.Key;
            if (!prefabDict.ContainsKey(meshName))
            {
                Debug.LogWarning($"Prefab 側に存在しないメッシュがターゲット側にあります: {meshName}");
                warnCount++;
            }
        }

        // シーンをダーティにマーク（シーンの場合）
        if (targetObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(targetObject.scene);
        }

        Debug.Log($"Prefab Material Applier: {applyCount} 個のメッシュにマテリアルを適用しました。警告: {warnCount} 件。");
        EditorUtility.DisplayDialog(
            "Prefab Material Applier",
            $"適用完了\n適用メッシュ数: {applyCount}\n警告数: {warnCount}\n詳細は Console を参照してください。",
            "OK"
        );
    }
}
