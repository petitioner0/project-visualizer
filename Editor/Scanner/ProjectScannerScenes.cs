using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectStructureVisualizer
{
    public partial class ProjectScanner
    {
        private List<SceneStructure> ScanScenes()
        {
            var result = new List<SceneStructure>();
            var current = EditorSceneManager.GetActiveScene();
            originalScenePath = current.path;

            try
            {
                int sceneCount = SceneManager.sceneCountInBuildSettings;
                for (int i = 0; i < sceneCount; i++)
                {
                    var path = SceneUtility.GetScenePathByBuildIndex(i);
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        var sceneStruct = new SceneStructure
                        {
                            sceneName = scene.name,
                            scenePath = path,
                        };
                        foreach (var root in scene.GetRootGameObjects())
                            sceneStruct.gameObjects.Add(ScanGameObject(root));
                        result.Add(sceneStruct);
                    }
                    finally
                    {
                        if (scene.isLoaded && scene.path != originalScenePath)
                            EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalScenePath))
                    EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
            }

            return result;
        }

        private GameObjectStructure ScanGameObject(GameObject go)
        {
            var obj = new GameObjectStructure
            {
                name = go.name,
                instanceId = go.GetInstanceID().ToString(),
            };

            // No longer process Prefab instances in the scene as they are already handled as GameObjects
            // Only prefabs created dynamically through Instantiate will be processed

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null)
                    continue;
                obj.components.Add(ScanComponent(comp));
            }

            for (int i = 0; i < go.transform.childCount; i++)
                obj.children.Add(ScanGameObject(go.transform.GetChild(i).gameObject));

            return obj;
        }

        private ComponentStructure ScanComponent(Component comp)
        {
            var cs = new ComponentStructure
            {
                componentType = comp.GetType().Name,
                className = comp.GetType().FullName,
                instanceId = comp.GetInstanceID().ToString(),
            };

            string norm = NormalizeFullName(comp.GetType().FullName);
            if (classesByFullName.TryGetValue(norm, out var classInfo))
                cs.methods = classInfo.methods.ConvertAll(m => new MethodStructure
                {
                    methodName = m.methodName,
                    methodType = m.methodType,
                    isStatic = m.isStatic,
                    nodeId = m.nodeId,
                });

            ExtractComponentProperties(comp, cs);
            return cs;
        }

        private void ExtractComponentProperties(Component comp, ComponentStructure cs)
        {
            var so = new SerializedObject(comp);
            var prop = so.GetIterator();

            while (prop.NextVisible(true))
            {
                if (prop.name == "m_Script")
                    continue;

                // Keep meaningful properties, simplify vector types
                object value = GetSerializedPropertyValue(prop, comp);
                if (value != null)
                {
                    cs.properties[prop.name] = value;
                }
            }
        }

        private object GetSerializedPropertyValue(SerializedProperty p, Component owner)
        {
            switch (p.propertyType)
            {
                // Basic types
                case SerializedPropertyType.Integer:
                    return p.intValue;
                case SerializedPropertyType.Boolean:
                    return p.boolValue;
                case SerializedPropertyType.Float:
                    return p.floatValue;
                case SerializedPropertyType.String:
                    return p.stringValue;
                case SerializedPropertyType.Enum:
                    if (p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length)
                        return p.enumDisplayNames[p.enumValueIndex];
                    else
                        return p.enumValueIndex.ToString();

                // Vector types
                case SerializedPropertyType.Vector2:
                    return $"({p.vector2Value.x}, {p.vector2Value.y})";
                case SerializedPropertyType.Vector3:
                    return $"({p.vector3Value.x}, {p.vector3Value.y}, {p.vector3Value.z})";
                case SerializedPropertyType.Vector4:
                    return $"({p.vector4Value.x}, {p.vector4Value.y}, {p.vector4Value.z}, {p.vector4Value.w})";
                case SerializedPropertyType.Quaternion:
                    return $"({p.quaternionValue.x}, {p.quaternionValue.y}, {p.quaternionValue.z}, {p.quaternionValue.w})";
                case SerializedPropertyType.Color:
                    return $"RGB({p.colorValue.r:F2}, {p.colorValue.g:F2}, {p.colorValue.b:F2})";
                case SerializedPropertyType.Rect:
                    return $"Rect(x:{p.rectValue.x}, y:{p.rectValue.y}, w:{p.rectValue.width}, h:{p.rectValue.height})";
                case SerializedPropertyType.Bounds:
                    return $"Bounds(center:{p.boundsValue.center}, size:{p.boundsValue.size})";

                // Object references
                case SerializedPropertyType.ObjectReference:
                    var obj = p.objectReferenceValue;
                    if (obj == null)
                        return "null";

                    string objType = obj.GetType().Name;
                    string objName = obj.name;

#if UNITY_EDITOR
                    string assetPath = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        // Check if it's a prefab
                        if (
                            assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            if (prefabCache.ContainsKey(assetPath))
                            {
                                referencedPrefabPaths.Add(assetPath);
                            }
                        }

                        return $"Ref → {objType}: {objName} ({assetPath})";
                    }
#endif

                    // Scene objects
                    if (obj is Component comp)
                    {
                        callRelations.Add(
                            new CallRelation
                            {
                                fromNodeId = owner.GetInstanceID().ToString(),
                                toNodeId = comp.GetInstanceID().ToString(),
                                callType = "field_reference",
                                fieldName = p.name,
                                libraryName = "InspectorBinding",
                            }
                        );
                        return $"Ref → {comp.gameObject.name}.{objType}";
                    }
                    if (obj is GameObject go)
                    {
                        callRelations.Add(
                            new CallRelation
                            {
                                fromNodeId = owner.GetInstanceID().ToString(),
                                toNodeId = go.GetInstanceID().ToString(),
                                callType = "field_reference",
                                fieldName = p.name,
                                libraryName = "InspectorBinding",
                            }
                        );
                        return $"Ref → GameObject: {go.name}";
                    }
                    return $"Ref → {objType}: {objName}";

                default:
                    return null;
            }
        }
    }
}
