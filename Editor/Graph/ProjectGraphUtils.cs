using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace ProjectStructureVisualizer
{
    public static class ProjectGraphUtils
    {
        // Utility function: normalize full name (remove global:: prefix)
        public static string NormalizeFullName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            return name.StartsWith("global::") ? name.Substring(8) : name;
        }

        // Utility function: resolve node ID (support nodeId -> class name mapping)
        public static string ResolveNodeId(string id, Dictionary<string, string> nodeIdToClassName)
        {
            if (string.IsNullOrEmpty(id))
                return id;

            // If it's nodeId (like node_3), find the class name through mapping
            if (id.StartsWith("node_") && nodeIdToClassName.TryGetValue(id, out var className))
            {
                return className;
            }

            // If it's a number (instanceId), return directly
            if (int.TryParse(id, out _))
            {
                return id;
            }

            // Process class name or method name: simplify and normalize
            string normalized = NormalizeFullName(id);

            // Try to simplify (remove method name, keep class name)
            string resolved = SimplifyMemberName(normalized);

            // If the simplified name has been normalized, use it; otherwise normalize again
            return NormalizeFullName(resolved);
        }

        // Utility function: simplify member name (remove method name, keep class name)
        public static string SimplifyMemberName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            int lastDot = fullName.LastIndexOf('.');
            return lastDot > 0 ? fullName.Substring(0, lastDot) : fullName;
        }

        public static bool IsExternalLibrary(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Ignore basic types
            if (IsBuiltInType(name))
            {
                return false;
            }

            // Ignore Unity system namespaces
            if (name.StartsWith("UnityEngine."))
            {
                return false;
            }

            if (name.StartsWith("UnityEditor."))
            {
                return false;
            }

            if (name.StartsWith("System."))
            {
                return false;
            }

            // If it contains namespaces (dots), it is considered an external library
            return name.Contains('.');
        }

        // Utility function: find the nearest node
        public static Node FindNearestNode(string id, Dictionary<string, Node> nodeById)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            // First check if it is a system type, if so, return null
            if (IsUnitySystemType(id) || IsSystemNamespaceType(id))
                return null;

            // First check exact match
            if (nodeById.TryGetValue(id, out var exact))
                return exact;

            // Normalize the search ID (remove global:: prefix)
            string normalizedId = NormalizeFullName(id);

            // Exact match normalized ID
            if (nodeById.TryGetValue(normalizedId, out var normalizedExact))
                return normalizedExact;

            // Allow partial matching (some types are only stored at the class layer)
            foreach (var kv in nodeById)
            {
                if (normalizedId.EndsWith(kv.Key) || kv.Key.EndsWith(normalizedId))
                    return kv.Value;
            }

            foreach (var kv in nodeById)
            {
                if (id.EndsWith(kv.Key) || kv.Key.EndsWith(id))
                    return kv.Value;
            }

            return null;
        }

        // Check if it is a Unity system type
        public static bool IsUnitySystemType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string simplified = SimplifyMemberName(name);
            return simplified.StartsWith("UnityEngine.") || simplified.StartsWith("UnityEditor.");
        }

        // Check if it is a type in the System namespace
        public static bool IsSystemNamespaceType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Check if it is a basic type
            if (IsBuiltInType(name))
                return true;

            // Check if it is a type in the System namespace
            string simplified = SimplifyMemberName(name);
            return simplified.StartsWith("System.");
        }

        public static bool IsBuiltInType(string name)
        {
            // List of basic types
            string[] builtInTypes =
            {
                "string",
                "String",
                "int",
                "Int32",
                "bool",
                "Boolean",
                "float",
                "Single",
                "double",
                "Double",
                "byte",
                "Byte",
                "char",
                "Char",
                "long",
                "Int64",
                "short",
                "Int16",
                "uint",
                "UInt32",
                "ulong",
                "UInt64",
                "ushort",
                "UInt16",
                "decimal",
                "Decimal",
                "object",
                "Object",
                "void",
                "Void",
            };

            // Check if it is a basic type or an alias of a basic type
            string normalizedName = name.Contains('.')
                ? name.Substring(name.LastIndexOf('.') + 1)
                : name;

            foreach (var builtIn in builtInTypes)
            {
                if (normalizedName == builtIn)
                {
                    return true;
                }
            }

            return false;
        }

        public static string ExtractLibraryName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return fullName;

            // Directly extract the first part of the namespace as the library name
            int firstDot = fullName.IndexOf('.');
            if (firstDot > 0)
            {
                return fullName.Substring(0, firstDot);
            }
            return fullName;
        }

    }
}

