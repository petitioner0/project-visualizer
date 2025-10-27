using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using UnityEditor;
using UnityEngine;
using YamlDotNet.Serialization;

namespace ProjectStructureVisualizer
{
    public partial class ProjectScanner
    {
        private Compilation compilation;

        private readonly Dictionary<string, ExternalClass> classesByFullName = new();
        private readonly Dictionary<string, string> nodeIdByMember = new();
        private readonly Dictionary<string, EventStructure> eventDefinitions = new();
        private readonly Dictionary<string, PrefabStructure> prefabCache = new();
        private readonly HashSet<string> referencedPrefabPaths = new();
        private readonly List<CallRelation> callRelations = new();
        private readonly List<StructureRelation> structureRelations = new();
        private int nodeIdCounter = 0;

        private string currentAnalyzingMethodId = null;
        private readonly Stack<string> methodAnalysisStack = new();
        private string originalScenePath = "";

        private readonly HashSet<string> unityNamespaceBlacklist = new()
        {
            "UnityEngine.Debug",
            "UnityEngine.Transform",
            "UnityEngine.Time",
            "UnityEngine.Input",
            "UnityEngine.Physics",
            "UnityEngine.Mathf",
            "UnityEngine.Application",
            "UnityEngine.Screen",
            "UnityEngine.Camera",
            "UnityEngine.Light",
            "UnityEngine.Renderer",
            "UnityEngine.Collider",
        };

        private readonly HashSet<string> structuralUnityMethods = new()
        {
            "UnityEngine.Object.Instantiate",
            "UnityEngine.Object.Destroy",
            "UnityEngine.SceneManagement.SceneManager.LoadScene",
            "UnityEngine.Resources.Load",
            "UnityEngine.GameObject.Find",
        };

        public async Task<bool> ScanProjectAsync()
        {
            try
            {
                await LoadSourceFiles();

                // Cache all Prefabs first (for later identification of Instantiate-referenced prefabs)
                CacheAllPrefabs();

                // Use BuildFullStructure to properly build all data
                var data = await BuildFullStructure();

                // Prefabs referenced by Instantiate will be automatically identified during code scanning
                // Prefab instances in scenes are already handled as GameObjects, no additional processing needed

                // Update prefabs data
                data.prefabs = GetReferencedPrefabs();

                ExportToYaml(data);

                Debug.Log(
                    $"Project scan completed! Scenes: {data.scenes.Count}, Classes: {data.externalClasses.Count}, Calls: {data.calls.Count}, Structure Relations: {data.structureRelations.Count}, Prefabs: {data.prefabs.Count}"
                );
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Scan failed: {e.Message}\n{e.StackTrace}");
                return false;
            }
            finally
            {
                CleanupResources(); // Cleanup resources
            }
        }

        private async Task<ProjectStructureData> BuildFullStructure()
        {
            var data = new ProjectStructureData();

            // Scan scene structures
            var scenes = ScanScenes();
            data.scenes.AddRange(scenes);

            // Structure layer (child_of / has_component) - will be gradually added during scanning

            // Scan C# semantic structures
            await ScanCodeStructure();
            data.externalClasses.AddRange(classesByFullName.Values);

            // Add structure relations created during code scanning
            if (structureRelations.Count > 0)
                data.structureRelations.AddRange(structureRelations);

            // Collect all call relations (including Inspector references)
            if (callRelations.Count > 0)
                data.calls.AddRange(callRelations);

            // Create complementary mapping between classes ↔ scene instances
            CreateMonoBehaviourInstanceLinks(data);

            return data;
        }

        // Complementary layer: Complementation links between MonoBehaviour ↔ scene components
        private void CreateMonoBehaviourInstanceLinks(ProjectStructureData data)
        {
            foreach (var scene in data.scenes)
            {
                foreach (var go in scene.gameObjects)
                {
                    foreach (var comp in go.components)
                    {
                        string norm = NormalizeFullName(comp.className);
                        if (classesByFullName.ContainsKey(norm))
                        {
                            data.calls.Add(
                                new CallRelation
                                {
                                    fromNodeId = norm,
                                    toNodeId = go.instanceId,
                                    callType = "scene_instance",
                                    libraryName = "ECSLink",
                                    methodName = "",
                                    fieldName = "",
                                }
                            );
                        }
                    }
                }
            }
        }

        private async System.Threading.Tasks.Task LoadSourceFiles()
        {
            string assetsPath = Application.dataPath;
            var csFiles = Directory
                .GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("/Editor/") && !f.Contains("/Packages/"))
                .ToList();

            var syntaxTrees = new List<SyntaxTree>();

            foreach (var file in csFiles)
            {
                try
                {
                    var sourceCode = await File.ReadAllTextAsync(file);
                    var tree = CSharpSyntaxTree.ParseText(sourceCode, path: file);
                    syntaxTrees.Add(tree);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Skipping file {file}: {ex.Message}");
                }
            }

            // Load all loaded assembly references
            var references = AppDomain
                .CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && File.Exists(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .ToList();

            // Create CSharp compilation
            compilation = CSharpCompilation.Create(
                "UnityProject",
                syntaxTrees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        private void ExportToYaml(ProjectStructureData data)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(
                    YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance
                )
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();

            string yaml = serializer.Serialize(data);
            string outputPath = Path.Combine(Application.dataPath, "..", "ProjectStructure.yaml");
            File.WriteAllText(outputPath, yaml);
        }

        private string GenerateNodeId() => $"node_{++nodeIdCounter}";

        private static string NormalizeFullName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            return name.StartsWith("global::", StringComparison.Ordinal) ? name.Substring(8) : name;
        }

        // Cleanup resources
        private void CleanupResources()
        {
            try
            {
                // Cleanup compilation unit
                compilation = null;
                prefabCache.Clear();

                // Cleanup other collections
                classesByFullName.Clear();
                nodeIdByMember.Clear();
                eventDefinitions.Clear();
                callRelations.Clear();
                structureRelations.Clear();
                referencedPrefabPaths.Clear();
                methodAnalysisStack.Clear();

                // Reset counters
                nodeIdCounter = 0;
                currentAnalyzingMethodId = null;
                originalScenePath = "";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error occurred while cleaning up resources: {e.Message}");
            }
        }
    }
}
