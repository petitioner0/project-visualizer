using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ProjectStructureVisualizer
{
    public partial class ProjectScanner
    {
        private async Task ScanCodeStructure()
        {
            if (compilation == null)
            {
                UnityEngine.Debug.LogError("Compilation unit is empty");
                return;
            }

            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                if (!syntaxTree.FilePath.EndsWith(".cs"))
                    continue;
                var root = await syntaxTree.GetRootAsync();
                AnalyzeSourceFile(root, compilation);
            }
        }

        private void AnalyzeSourceFile(SyntaxNode root, Compilation compilation)
        {
            var semanticModel = compilation.GetSemanticModel(root.SyntaxTree);

            // Include all top-level type declarations
            var typeDeclarations = root.DescendantNodes()
                .Where(n =>
                    n is ClassDeclarationSyntax
                    || n is InterfaceDeclarationSyntax
                    || n is StructDeclarationSyntax
                    || n is EnumDeclarationSyntax
                );

            foreach (var decl in typeDeclarations)
            {
                switch (decl)
                {
                    case ClassDeclarationSyntax cls:
                        var classInfo = AnalyzeClass(cls, semanticModel);
                        if (classInfo != null)
                        {
                            string fullName = NormalizeFullName(
                                $"{classInfo.namespaceName}.{classInfo.className}".TrimStart('.')
                            );
                            // Only add classes that are actually used (classes with methods, or subclasses of MonoBehaviour)
                            if (classInfo.methods.Count > 0 || classInfo.isMonoBehaviour)
                            {
                                classesByFullName[fullName] = classInfo;
                            }
                        }
                        break;

                    case InterfaceDeclarationSyntax iface:
                        AnalyzeInterface(iface, semanticModel);
                        break;

                    case StructDeclarationSyntax st:
                        AnalyzeStruct(st, semanticModel);
                        break;

                    case EnumDeclarationSyntax en:
                        AnalyzeEnum(en, semanticModel);
                        break;
                }
            }

            // Scan method call relations (class only)
            AnalyzeCallRelations(root, semanticModel);
        }

        private void AnalyzeCallRelations(SyntaxNode root, SemanticModel semanticModel)
        {
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

            foreach (var methodDecl in methodDeclarations)
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
                if (methodSymbol == null)
                    continue;

                string methodFullName = NormalizeFullName(
                    $"{methodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{methodSymbol.Name}"
                );
                if (!nodeIdByMember.TryGetValue(methodFullName, out string currentMethodId))
                    continue;

                methodAnalysisStack.Push(currentAnalyzingMethodId);
                currentAnalyzingMethodId = currentMethodId;

                try
                {
                    var invocations = methodDecl
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>();
                    foreach (var invocation in invocations)
                        AnalyzeMethodInvocation(invocation, semanticModel);

                    // Scan enum member access
                    var memberAccesses = methodDecl
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>();
                    foreach (var memberAccess in memberAccesses)
                        AnalyzeMemberAccess(memberAccess, semanticModel);

                    // Scan object creation expressions (including struct constructors)
                    var objectCreations = methodDecl
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>();
                    foreach (var objectCreation in objectCreations)
                        AnalyzeObjectCreation(objectCreation, semanticModel);
                }
                finally
                {
                    currentAnalyzingMethodId =
                        methodAnalysisStack.Count > 0 ? methodAnalysisStack.Pop() : null;
                }
            }
        }

        private void AnalyzeMethodInvocation(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel
        )
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            var method = symbolInfo.Symbol as IMethodSymbol;
            if (method == null || currentAnalyzingMethodId == null)
                return;

            string targetAssembly = method.ContainingAssembly?.Name ?? "UnknownAssembly";
            string targetFullName = NormalizeFullName(
                $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{method.Name}"
            );

            // Detect Instantiate call, extract prefab reference
            if (
                method.Name == "Instantiate"
                && method.ContainingType.ToDisplayString() == "UnityEngine.Object"
            )
            {
                AnalyzeInstantiateCall(invocation, semanticModel);
            }

            // Skip unimportant Unity API
            if (IsUnityApiFiltered(targetFullName, targetFullName))
                return;

            callRelations.Add(
                new CallRelation
                {
                    fromNodeId = currentAnalyzingMethodId,
                    toNodeId = targetFullName,
                    callType = "method",
                    methodName = method.Name,
                    libraryName = targetAssembly,
                }
            );
        }

        private void AnalyzeInstantiateCall(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel
        )
        {
            // Get the first argument of Instantiate
            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var firstArg = invocation.ArgumentList.Arguments[0].Expression;

                // Try to get symbol information
                var symbolInfo = semanticModel.GetSymbolInfo(firstArg);
                string fieldName = null;
                string typeName = null;

                // Check if it's a field reference
                if (symbolInfo.Symbol is IFieldSymbol fieldSymbol)
                {
                    fieldName = fieldSymbol.Name;
                    typeName = fieldSymbol.Type.ToDisplayString();
                }
                // If it's a simple identifier expression
                else if (firstArg is IdentifierNameSyntax identifier)
                {
                    var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                    if (symbol is IFieldSymbol)
                    {
                        fieldName = identifier.Identifier.ValueText;
                        typeName = ((IFieldSymbol)symbol).Type.ToDisplayString();
                    }
                }
                // If it's a member access expression
                else if (firstArg is MemberAccessExpressionSyntax memberAccess)
                {
                    if (memberAccess.Name is IdentifierNameSyntax name)
                    {
                        fieldName = name.Identifier.ValueText;
                        var memberSymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol;
                        if (memberSymbol is IFieldSymbol field)
                        {
                            typeName = field.Type.ToDisplayString();
                        }
                    }
                }

                if (
                    !string.IsNullOrEmpty(fieldName)
                    && !string.IsNullOrEmpty(typeName)
                    && (
                        typeName.StartsWith("UnityEngine.GameObject")
                        || typeName.StartsWith("UnityEngine.Component")
                        || typeName.EndsWith("Prefab")
                    )
                )
                {
                    UnityEngine.Debug.Log(
                        $"Detected Instantiate field: {fieldName}, type: {typeName}"
                    );

                    // Check if this field name matches any prefab path
                    foreach (var kvp in prefabCache)
                    {
                        // Remove .prefab extension and path prefix, compare only file name
                        string prefabFileName = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
                        string normalizedFieldName = fieldName
                            .Replace("Prefab", "")
                            .Replace("prefab", "");
                        string normalizedFileName = prefabFileName
                            .Replace("Prefab", "")
                            .Replace("prefab", "");

                        if (
                            prefabFileName.Equals(
                                fieldName,
                                System.StringComparison.OrdinalIgnoreCase
                            )
                            || prefabFileName.Contains(
                                fieldName,
                                System.StringComparison.OrdinalIgnoreCase
                            )
                            || fieldName.Contains(
                                prefabFileName,
                                System.StringComparison.OrdinalIgnoreCase
                            )
                            || normalizedFieldName.Equals(
                                normalizedFileName,
                                System.StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            referencedPrefabPaths.Add(kvp.Key);
                            UnityEngine.Debug.Log(
                                $"Found Prefab referenced via Instantiate: {kvp.Key} (field: {fieldName})"
                            );
                            break;
                        }
                    }
                }
            }
        }

        private bool IsUnityApiFiltered(string targetNamespace, string targetMethodFullName)
        {
            if (structuralUnityMethods.Contains(targetMethodFullName))
                return false;

            foreach (var ns in unityNamespaceBlacklist)
                if (targetNamespace.StartsWith(ns))
                    return true;

            return false;
        }

        private void AnalyzeMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel
        )
        {
            if (currentAnalyzingMethodId == null)
                return;

            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            var field = symbolInfo.Symbol as IFieldSymbol;

            // Check if it's an enum member
            if (field != null && field.ContainingType.TypeKind == TypeKind.Enum)
            {
                string enumTypeNamespace =
                    field.ContainingType.ContainingNamespace?.ToDisplayString() ?? "";

                // Skip Unity system types
                if (
                    enumTypeNamespace.StartsWith("UnityEngine")
                    || enumTypeNamespace.StartsWith("UnityEditor")
                    || enumTypeNamespace.StartsWith("TMPro")
                    || enumTypeNamespace.StartsWith("System")
                )
                {
                    return;
                }

                string enumTypeName = field.ContainingType.Name;
                string enumNodeId = NormalizeFullName($"{enumTypeNamespace}.{enumTypeName}");

                // Check if the same relationship already exists
                bool alreadyExists = structureRelations.Any(r =>
                    r.fromNodeId == currentAnalyzingMethodId
                    && r.toNodeId == enumNodeId
                    && r.relationType == "uses"
                );

                if (!alreadyExists)
                {
                    structureRelations.Add(
                        new StructureRelation
                        {
                            fromNodeId = currentAnalyzingMethodId,
                            toNodeId = enumNodeId,
                            relationType = "uses",
                        }
                    );
                }
            }
        }

        private void AnalyzeObjectCreation(
            ObjectCreationExpressionSyntax objectCreation,
            SemanticModel semanticModel
        )
        {
            if (currentAnalyzingMethodId == null)
                return;

            var symbolInfo = semanticModel.GetSymbolInfo(objectCreation);
            var typeSymbol = symbolInfo.Symbol as INamedTypeSymbol;

            // Check if it's a struct type
            if (typeSymbol != null && typeSymbol.TypeKind == TypeKind.Struct)
            {
                string structTypeNamespace =
                    typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";

                // Skip Unity system types
                if (
                    structTypeNamespace.StartsWith("UnityEngine")
                    || structTypeNamespace.StartsWith("UnityEditor")
                    || structTypeNamespace.StartsWith("TMPro")
                    || structTypeNamespace.StartsWith("System")
                )
                {
                    return;
                }

                string structTypeName = typeSymbol.Name;
                string structNodeId = NormalizeFullName($"{structTypeNamespace}.{structTypeName}");

                // Check if the same relationship already exists
                bool alreadyExists = structureRelations.Any(r =>
                    r.fromNodeId == currentAnalyzingMethodId
                    && r.toNodeId == structNodeId
                    && r.relationType == "uses"
                );

                if (!alreadyExists)
                {
                    structureRelations.Add(
                        new StructureRelation
                        {
                            fromNodeId = currentAnalyzingMethodId,
                            toNodeId = structNodeId,
                            relationType = "uses",
                        }
                    );
                }
            }
        }

        private ExternalClass AnalyzeClass(
            ClassDeclarationSyntax classDecl,
            SemanticModel semanticModel
        )
        {
            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl);
            if (classSymbol == null)
                return null;

            // Skip data model namespace
            if (
                classSymbol
                    .ContainingNamespace?.ToDisplayString()
                    .StartsWith("ProjectStructureVisualizer") == true
            )
                return null;

            string className = classSymbol.Name;
            string namespaceName = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";
            string nodeId = NormalizeFullName($"{namespaceName}.{className}");

            // Basic information
            var classInfo = new ExternalClass
            {
                className = className,
                namespaceName = namespaceName,
                type = "class",
                isMonoBehaviour = IsSubclassOf(classSymbol, "UnityEngine.MonoBehaviour"),
            };
            nodeIdByMember[nodeId] = GenerateNodeId();

            // Inheritance relationship
            if (classSymbol.BaseType != null && classSymbol.BaseType.Name != "Object")
            {
                string parentName = NormalizeFullName(classSymbol.BaseType.ToDisplayString());
                structureRelations.Add(
                    new StructureRelation
                    {
                        fromNodeId = nodeId,
                        toNodeId = parentName,
                        relationType = "inherits",
                    }
                );
            }

            // Interface implementation relationship
            foreach (var iface in classSymbol.AllInterfaces)
            {
                string ifaceName = NormalizeFullName(iface.ToDisplayString());
                structureRelations.Add(
                    new StructureRelation
                    {
                        fromNodeId = nodeId,
                        toNodeId = ifaceName,
                        relationType = "implements",
                    }
                );
            }

            // Member field uses struct / enum relationship
            foreach (var field in classSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                var fieldType = field.Type;
                if (fieldType.TypeKind is TypeKind.Struct or TypeKind.Enum)
                {
                    // Skip Unity system types
                    string typeNamespace = fieldType.ContainingNamespace?.ToDisplayString() ?? "";
                    if (
                        typeNamespace.StartsWith("UnityEngine")
                        || typeNamespace.StartsWith("UnityEditor")
                        || typeNamespace.StartsWith("TMPro")
                        || typeNamespace.StartsWith("System")
                    )
                    {
                        continue;
                    }

                    // Build namespace + type name format consistent with node creation
                    string usedTypeName = fieldType.Name;
                    string usedTypeNamespace =
                        fieldType.ContainingNamespace?.ToDisplayString() ?? "<global namespace>";
                    string usedType = NormalizeFullName($"{usedTypeNamespace}.{usedTypeName}");

                    // Check if the same relationship already exists
                    bool alreadyExists = structureRelations.Any(r =>
                        r.fromNodeId == nodeId && r.toNodeId == usedType && r.relationType == "uses"
                    );

                    if (!alreadyExists)
                    {
                        structureRelations.Add(
                            new StructureRelation
                            {
                                fromNodeId = nodeId,
                                toNodeId = usedType,
                                relationType = "uses",
                            }
                        );
                    }
                }
            }

            // Collect method definitions
            foreach (var member in classDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodSymbol = semanticModel.GetDeclaredSymbol(member);
                if (methodSymbol == null)
                    continue;

                string methodFullName = NormalizeFullName(
                    $"{classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{methodSymbol.Name}"
                );
                string methodId = GenerateNodeId();
                nodeIdByMember[methodFullName] = methodId;

                string methodType = DetermineMethodType(member, semanticModel);
                classInfo.methods.Add(
                    new MethodStructure
                    {
                        methodName = methodSymbol.Name,
                        methodType = methodType,
                        isStatic = methodSymbol.IsStatic,
                        nodeId = methodId,
                    }
                );
            }

            // Collect event definitions
            foreach (var member in classDecl.Members.OfType<EventFieldDeclarationSyntax>())
            {
                foreach (var variable in member.Declaration.Variables)
                {
                    var eventSymbol = semanticModel.GetDeclaredSymbol(variable);
                    if (eventSymbol != null)
                    {
                        string eventId = GenerateNodeId();
                        classInfo.events.Add(
                            new EventStructure
                            {
                                eventName = eventSymbol.Name,
                                nodeId = eventId,
                                isStatic = eventSymbol.IsStatic,
                            }
                        );
                    }
                }
            }

            return classInfo;
        }

        private string DetermineMethodType(
            MethodDeclarationSyntax methodDecl,
            SemanticModel semanticModel
        )
        {
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
            if (methodSymbol == null)
                return "custom";

            string methodName = methodSymbol.Name;

            // Unity MonoBehaviour lifecycle methods
            if (
                new[]
                {
                    "Start",
                    "Awake",
                    "Update",
                    "FixedUpdate",
                    "LateUpdate",
                    "OnEnable",
                    "OnDisable",
                    "OnDestroy",
                }.Contains(methodName)
            )
                return "unity_lifecycle";

            // Unity event callbacks
            if (methodName.StartsWith("On") && methodName.Contains("Trigger"))
                return "unity_callback";

            // Compiler-generated
            if (
                methodSymbol.IsImplicitlyDeclared
                || methodSymbol.MethodKind == MethodKind.PropertyGet
                || methodSymbol.MethodKind == MethodKind.PropertySet
                || methodSymbol.MethodKind == MethodKind.EventAdd
                || methodSymbol.MethodKind == MethodKind.EventRemove
            )
                return "generated";

            return "custom";
        }

        // Interface: only create nodes, no call analysis
        private void AnalyzeInterface(
            InterfaceDeclarationSyntax ifaceDecl,
            SemanticModel semanticModel
        )
        {
            var symbol = semanticModel.GetDeclaredSymbol(ifaceDecl);
            if (symbol == null)
                return;

            // Skip data model namespaces
            if (
                symbol
                    .ContainingNamespace?.ToDisplayString()
                    .StartsWith("ProjectStructureVisualizer") == true
            )
                return;

            string ifaceName = symbol.Name;
            string namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";
            string nodeId = NormalizeFullName($"{namespaceName}.{ifaceName}");

            var ifaceInfo = new ExternalClass
            {
                className = ifaceName,
                namespaceName = namespaceName,
                type = "interface",
                isMonoBehaviour = false,
            };

            classesByFullName[nodeId] = ifaceInfo;
        }

        // Struct: only create nodes, no call analysis
        private void AnalyzeStruct(StructDeclarationSyntax structDecl, SemanticModel semanticModel)
        {
            var symbol = semanticModel.GetDeclaredSymbol(structDecl);
            if (symbol == null)
                return;

            // Skip data model namespaces
            if (
                symbol
                    .ContainingNamespace?.ToDisplayString()
                    .StartsWith("ProjectStructureVisualizer") == true
            )
                return;

            string structName = symbol.Name;
            string namespaceName =
                symbol.ContainingNamespace?.ToDisplayString() ?? "<global namespace>";
            string nodeId = NormalizeFullName($"{namespaceName}.{structName}");

            var structInfo = new ExternalClass
            {
                className = structName,
                namespaceName = namespaceName,
                type = "struct",
                isMonoBehaviour = false,
            };

            classesByFullName[nodeId] = structInfo;
        }

        // Enum: only create nodes, no call analysis
        private void AnalyzeEnum(EnumDeclarationSyntax enumDecl, SemanticModel semanticModel)
        {
            var symbol = semanticModel.GetDeclaredSymbol(enumDecl);
            if (symbol == null)
                return;

            // Skip data model namespaces
            if (
                symbol
                    .ContainingNamespace?.ToDisplayString()
                    .StartsWith("ProjectStructureVisualizer") == true
            )
                return;

            string enumName = symbol.Name;
            string namespaceName =
                symbol.ContainingNamespace?.ToDisplayString() ?? "<global namespace>";
            string nodeId = NormalizeFullName($"{namespaceName}.{enumName}");

            var enumInfo = new ExternalClass
            {
                className = enumName,
                namespaceName = namespaceName,
                type = "enum",
                isMonoBehaviour = false,
            };

            classesByFullName[nodeId] = enumInfo;
        }

        // Helper method: determine inheritance relationship
        private bool IsSubclassOf(INamedTypeSymbol type, string targetBase)
        {
            while (type != null)
            {
                if (type.ToDisplayString() == targetBase)
                    return true;
                type = type.BaseType;
            }
            return false;
        }
    }
}
