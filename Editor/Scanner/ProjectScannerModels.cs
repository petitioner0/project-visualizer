using System;
using System.Collections.Generic;

namespace ProjectStructureVisualizer
{
    public partial class ProjectScanner
    {
        [Serializable]
        public class ProjectStructureData
        {
            public List<SceneStructure> scenes = new();
            public List<ExternalClass> externalClasses = new();
            public List<CallRelation> calls = new(); 
            public List<StructureRelation> structureRelations = new(); 
            public List<PrefabStructure> prefabs = new();
        }

        [Serializable]
        public class SceneStructure
        {
            public string sceneName;
            public string scenePath;
            public List<GameObjectStructure> gameObjects = new();
        }

        [Serializable]
        public class GameObjectStructure
        {
            public string name;
            public string instanceId;
            public List<ComponentStructure> components = new();
            public List<GameObjectStructure> children = new();
        }

        [Serializable]
        public class ComponentStructure
        {
            public string componentType;
            public string className;
            public string instanceId; // Component 的 instanceId
            public List<MethodStructure> methods = new();
            public Dictionary<string, object> properties = new();
        }

        [Serializable]
        public class MethodStructure
        {
            public string methodName;
            public string methodType;
            public bool isStatic;
            public string nodeId;
        }

        [Serializable]
        public class ExternalClass
        {
            public string className;
            public string namespaceName;
            public string type = "class"; // "class", "interface", "struct", "enum"
            public bool isMonoBehaviour;
            public List<MethodStructure> methods = new();
            public List<EventStructure> events = new();
            public List<MethodStructure> staticInitializers = new();
        }

        [Serializable]
        public class EventStructure
        {
            public string eventName;
            public string nodeId;
            public bool isStatic;
        }

        [Serializable]
        public class CallRelation
        {
            public string fromNodeId;
            public string toNodeId;
            public string callType; // method, field_reference, prefab_reference, etc.
            public string methodName; // Only used for method calls
            public string fieldName; // Used for Inspector referenced fields
            public string libraryName;
        }

        [Serializable]
        public class StructureRelation
        {
            public string fromNodeId;
            public string toNodeId;
            public string relationType; // "child_of", "has_component"
        }

        [Serializable]
        public class PrefabStructure
        {
            public string prefabName;
            public string prefabPath;
            public GameObjectStructure rootObject;
        }
    }
}
