using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectStructureVisualizer
{
    public partial class ProjectScanner
    {
        private void CacheAllPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var go = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (go != null)
                    {
                        prefabCache[path] = new PrefabStructure
                        {
                            prefabName = go.name,
                            prefabPath = path,
                            rootObject = ScanGameObject(go),
                        };
                    }
                }
                finally
                {
                    if (go != null)
                    {
                        PrefabUtility.UnloadPrefabContents(go);
                    }
                }
            }
        }

        private List<PrefabStructure> GetReferencedPrefabs()
        {
            var list = new List<PrefabStructure>();
            foreach (var kv in prefabCache)
            {
                if (referencedPrefabPaths.Contains(kv.Key))
                {
                    list.Add(kv.Value);
                }
            }

            return list;
        }
    }
}
