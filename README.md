# Project Visualizer

A powerful Unity editor tool for analyzing and visualizing Unity project structure. Uses Roslyn for semantic analysis to automatically scan scenes, scripts, Prefabs, and call relationships, generating visual project dependency graphs.

## Features

### Project Structure Scanning
- **Scene Structure Analysis**: Automatically scans GameObject hierarchy and components in scenes
- **Code Semantic Analysis**: Uses Roslyn for C# code semantic parsing, identifying classes, methods, fields, properties
- **Prefab Structure**: Scans and analyzes Prefab component structures
- **Call Relationship Tracking**: Identifies method calls, event subscriptions, Inspector references, etc.
- **Structural Relationships**: Inheritance, implementation, and usage relationships

### Visualization Graph
- **Interactive GUI**: GraphView-based visual editor window
- **Multi-layer Views**: Clear display of scene layer, class layer, and library layer
- **Smart Search**: Real-time node filtering
- **Edge Labeling**: Call relationships and method name labels
- **Color-Coded Nodes**: Different node types use different colors for easy identification

### Data Export
- **YAML Format Export**: Generates structured project structure files
- **Structured Data**: Includes complete scene, class, call, and relationship data

## Quick Start

### Installation

download this package, place this package under the `Assets` directory.

### Usage

#### 1. Scan Project

Open the menu in Unity Editor:
```
Project Scanner > Scan project
```

Click the **"Scan Project"** button and wait for the scan to complete.

The scan will generate a `ProjectStructure.yaml` file in the project root directory.

#### 2. Visualize Project Structure

Open the graph view:
```
Project Scanner > Project Graph (From YAML)
```

Click the **"Select YAML File"** button and select the generated `ProjectStructure.yaml` file.

In the graph view:
- Use the search box to filter nodes
- Hold `Alt` or middle mouse button to drag and move the view
- Scroll wheel to zoom

## Graph Color Legend

### Node Colors

#### Scene Layer
- **Scene**: <span style="color:#4782F5;">Blue</span> - Represents Unity scenes  
- **GameObject**: <span style="color:#61CF78;">Green</span> - Represents game objects  
- **Component**: <span style="color:#F5B029;">Orange</span> - Represents components attached to GameObjects

#### Code Layer
- **Interface**: <span style="color:#ED82ED;">Magenta </span> - Represents C# interfaces  
- **Enum**: <span style="color:#9C66E8;">Purple</span> - Represents enumeration types  
- **Struct**: <span style="color:#F57878;">Pink</span> - Represents struct types  
- **Class**: <span style="color:#A6A6A6;">Gray</span> - Represents C# classes

#### External Layer
- **Library**: <span style="color:#D9D94D;">Yellow</span> - Represents external library references  
- **Constant**: <span style="color:#99CCE6;">Light Blue</span> - Represents constant values

---

### Edge Colors

The color of connecting edges represents different relationship types:

- **Structural (contains / child_of)**: <span style="color:#59CC6B;">Green</span> - Parent-child object relationships  
- **Component (has_component)**: <span style="color:#F5B029;">Orange</span> - Connections between GameObjects and components  
- **Property (has_property)**: <span style="color:#99CCE6;">Light Blue</span> - Property references  
- **Inheritance (inherits / implements)**: <span style="color:#ED82ED;">Magenta</span> - Inheritance and implementation relationships  
- **Method Call (uses / method)**: <span style="color:#A6C2F2;">Light Purple-Blue</span> - Method call relationships  
- **Reference (references / field_reference)**: <span style="color:#D9D94D;">Yellow</span> - Field references  
- **Other**: <span style="color:#B3B3B3;">Gray</span> - Other relationships

## Scan Details

### Scene Structure
Scans scenes for:
- Scene root nodes
- GameObject hierarchy (child_of relationships)
- Component attachments (has_component relationships)
- MonoBehaviour instances

### Code Structure
Identifies all C# classes:
- Namespace and class names
- Complete method signatures
- Parameter types and return types
- Fields and properties
- Outside library

### Call Relationships (Calls)
Tracks method calls:
- Support Inspector reference relationships

### Structural Relationships (Structure Relations)
Analyzes code structural relationships contain Inheritance, Usage, Implements

### Prefab
The prefab instantiate inside code treat as external usage, other are ignored or treated as gameobject. 

### Dependence
- **Roslyn**: Microsoft.CodeAnalysis for C# code semantic analysis
- **YamlDotNet**: YAML serialization
- **Unity GraphView**: Graph visualization

## YAML Output Structure

The generated `ProjectStructure.yaml` contains the following data:

```yaml
scenes:
  - sceneName: "SampleScene"
    gameObjects: [...]
    
externalClasses:
  - namespaceName: "MyNamespace"
    className: "MyClass"
    methods: [...]
    
calls:
  - fromNodeId: "node_1"
    toNodeId: "node_2"
    callType: "method_call"
    libraryName: "MyLibrary"
    methodName: "DoSomething"
    
structureRelations:
  - fromNodeId: "node_1"
    toNodeId: "node_2"
    relationType: "inherits_from"
    
prefabs:
  - prefabPath: "Assets/Prefab/MyPrefab.prefab"
    rootObject: {...}
```

### Scan Filters

The scanner automatically filters the following content:

#### Unity Namespace Blacklist
```csharp
UnityEngine.Debug
UnityEngine.Transform
UnityEngine.Time
UnityEngine.Input
...
```

These methods are specially processed to identify their structural significance.

## Notes

1. **Editor Only**: This package is only for Unity Editor and does not affect runtime performance
2. **Scan Scope**: Only scans files under the `Assets` directory, excludes `Packages` and `Editor` directories
3. **Performance**: Large projects may take some time to scan and build graph

## Disclaimer

**This project is fully open source.**

Due to the complexity of Unity's system, there may be some methods and calls in Unity that are not fully covered or may be detected incorrectly. We welcome suggestions and feedback to help improve this tool.

**Currently, this package cannot be used as a UPM package due to unknown reasons. We hope to resolve this issue in the future.**