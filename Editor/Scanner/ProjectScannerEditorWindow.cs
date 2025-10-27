using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ProjectStructureVisualizer
{
    public class ProjectScannerWindow : EditorWindow
    {
        private ProjectScanner scanner;
        private bool isScanning = false;
        private string lastResult = "";

        [MenuItem("Project Scanner/Scan project")]
        public static void ShowWindow()
        {
            var window = GetWindow<ProjectScannerWindow>("Project Scanner");
            window.minSize = new Vector2(300, 400);
        }

        private void OnGUI()
        {
            GUILayout.Label("Unity Project Structure Scanner", EditorStyles.boldLabel);
            GUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(isScanning);
            if (
                GUILayout.Button(
                    isScanning ? "Scanning..." : "Start Scanning Project",
                    GUILayout.Height(40)
                )
            )
            {
                _ = RunScanAsync();
            }
            EditorGUI.EndDisabledGroup();

            if (isScanning)
            {
                EditorGUILayout.HelpBox("Scanning project, please wait...", MessageType.Info);
            }

            if (!string.IsNullOrEmpty(lastResult))
            {
                EditorGUILayout.HelpBox(lastResult, MessageType.Info);
            }
            GUILayout.Space(10);
            GUILayout.Label("Output File: ProjectStructure.yaml", EditorStyles.helpBox);
        }

        private async Task RunScanAsync()
        {
            try
            {
                isScanning = true;
                lastResult = "";
                Repaint();

                scanner = new ProjectScanner();
                bool success = await scanner.ScanProjectAsync();

                if (success)
                {
                    lastResult = "Scan successful! Check console for detailed information.";
                }
                else
                {
                    lastResult = "Scan failed, check console for error messages.";
                }
            }
            catch (System.Exception e)
            {
                lastResult = $"Error occurred: {e.Message}";
                Debug.LogError($"Scan exception: {e}");
            }
            finally
            {
                isScanning = false;
                Repaint();
            }
        }
    }
}
