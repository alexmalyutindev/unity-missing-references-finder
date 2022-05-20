using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Component = UnityEngine.Component;

public class MissingReferencesFinder : MonoBehaviour
{
    private class ObjectData
    {
        public float ExpectedProgress;
        public GameObject GameObject;
    }

    [MenuItem("Tools/Find Missing References/In current scene", false, 50)]
    public static void FindMissingReferencesInCurrentScene()
    {
        var scene = SceneManager.GetActiveScene();
        ShowInitialProgressBar(scene.path);

        ClearConsole();

        var wasCancelled = false;
        var count = FindMissingReferencesInScene(
            scene,
            1,
            () => { wasCancelled = false; },
            () => { wasCancelled = true; }
        );
        ShowFinishDialog(wasCancelled, count);
    }

    [MenuItem("Tools/Find Missing References/In current prefab", false, 51)]
    public static void FindMissingReferencesInCurrentPrefab()
    {
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

#if UNITY_2020_1_OR_NEWER
        var assetPath = prefabStage.assetPath;
#else
        var assetPath = prefabStage.prefabAssetPath;
#endif
        ShowInitialProgressBar(assetPath);
        ClearConsole();

        var count = FindMissingReferences(assetPath, prefabStage.prefabContentsRoot, true);
        ShowFinishDialog(false, count);
    }

    [MenuItem("Tools/Find Missing References/In current prefab", true, 51)]
    public static bool FindMissingReferencesInCurrentPrefabValidate() =>
        PrefabStageUtility.GetCurrentPrefabStage() != null;

    [MenuItem("Tools/Find Missing References/In all scenes in build", false, 52)]
    public static void FindMissingReferencesInAllScenesInBuild()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToList();

        var count = 0;
        var wasCancelled = true;
        foreach (var scene in scenes)
        {
            Scene openScene;
            try
            {
                openScene = EditorSceneManager.OpenScene(scene.path);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Could not open scene at path \"{scene.path}\". This scene was added to the build, and it's possible that it has been deleted: Error: {ex.Message}"
                );
                continue;
            }

            count += FindMissingReferencesInScene(
                openScene,
                1 / (float) scenes.Count(),
                () => { wasCancelled = false; },
                () => { wasCancelled = true; }
            );
            if (wasCancelled) break;
        }

        ShowFinishDialog(wasCancelled, count);
    }

    /*[MenuItem("Tools/Find Missing References/In all scenes in project", false, 52)]
    public static void FindMissingReferencesInAllScenes() {
        var scenes = EditorBuildSettings.scenes;

        var finished = true;
        foreach (var scene in scenes) {
            var s = EditorSceneManager.OpenScene(scene.path);
            finished = findMissingReferencesInScene(s, 1 /(float)scenes.Count());
            if (!finished) break;
        }
        showFinishDialog(!finished);
    }*/

    [MenuItem("Tools/Find Missing References/In assets", false, 52)]
    public static void FindMissingReferencesInAssets()
    {
        ShowInitialProgressBar("all assets");
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        var objs = allAssetPaths
            .Where(IsProjectAsset)
            .ToArray();

        var wasCancelled = false;
        using (CreateLoggingContext())
        {
            var count = FindMissingReferences(
                "Project",
                objs,
                () => { wasCancelled = false; },
                () => { wasCancelled = true; }
            );
            ShowFinishDialog(wasCancelled, count);
        }

        EditorUtility.RevealInFinder(LogDirectory.FullName);
    }

    [MenuItem("Tools/Find Missing References/In assets (Include Children)", false, 52)]
    public static void FindMissingReferencesInAssetsIncludeChildren()
    {
        ShowInitialProgressBar("all assets");
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        var objs = allAssetPaths
            .Where(IsProjectAsset)
            .ToArray();

        var wasCancelled = false;

        using (CreateLoggingContext())
        {
            var count = FindMissingReferences(
                "Project",
                objs,
                () => { wasCancelled = false; },
                () => { wasCancelled = true; },
                includeChildren: true
            );
            ShowFinishDialog(wasCancelled, count);
        }

        EditorUtility.RevealInFinder(LogDirectory.FullName);
    }

    [MenuItem("Tools/Find Missing References/Everywhere", false, 53)]
    public static void FindMissingReferencesEverywhere()
    {
        var currentScenePath = SceneManager.GetActiveScene().path;

        #region Prevent from starting if the current scene is unsaved or has any changes.

        if (string.IsNullOrWhiteSpace(currentScenePath))
        {
            if (!EditorUtility.DisplayDialog(
                    "Missing References Finder",
                    "You must save the current scene before starting to find missing references in the project.",
                    "Save",
                    "Cancel"
                )) return;
            if (EditorSceneManager.SaveOpenScenes())
            {
                currentScenePath = SceneManager.GetActiveScene().path;
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Missing References Finder",
                    "Could not start finding missing references in the project because the current scene is not saved.",
                    "Ok"
                );
                return;
            }
        }

        #endregion

        // Warn the user to save the scene if it has unsaved changes. If the user selects "Cancel" the process is stopped.
        // If the user selects "Don't save", saving is omitted but this still returns true so the process starts. This 
        // behavior is expected and correct (the user has been warned and they still chose not to save).
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        var scenes = EditorBuildSettings.scenes;
        var progressWeight = 1 / (float) (scenes.Length + 1);

        ClearConsole();

        var count = 0;
        var wasCancelled = true;
        var currentProgress = 0f;
        foreach (var scene in scenes)
        {
            Scene openScene;
            try
            {
                openScene = EditorSceneManager.OpenScene(scene.path);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Could not open scene at path \"{scene.path}\". This scene was added to the build, and it's possible that it has been deleted: Error: {ex.Message}"
                );
                continue;
            }

            count += FindMissingReferencesInScene(
                openScene,
                progressWeight,
                () => { wasCancelled = false; },
                () => { wasCancelled = true; },
                currentProgress
            );
            currentProgress += progressWeight;
            if (wasCancelled) break;
        }

        if (!wasCancelled)
        {
            var allAssetPaths = AssetDatabase.GetAllAssetPaths();
            var objs = allAssetPaths
                .Where(IsProjectAsset)
                .ToArray();

            count += FindMissingReferences(
                "Project",
                objs,
                () => { wasCancelled = false; },
                () => { wasCancelled = true; },
                currentProgress,
                progressWeight
            );
        }

        ShowFinishDialog(wasCancelled, count);

        // Restore the scene that was originally open when the tool was started.
        if (!string.IsNullOrEmpty(currentScenePath)) EditorSceneManager.OpenScene(currentScenePath);
    }

    private static bool IsProjectAsset(string path)
    {
#if UNITY_EDITOR_OSX
        return !path.StartsWith("/");
#else
        return path.Substring(1, 2) != ":/";
#endif
    }

    private static int FindMissingReferences(string context, string[] paths, Action onFinished, Action onCanceled,
        float initialProgress = 0f, float progressWeight = 1f, bool includeChildren = false)
    {
        var count = 0;
        var wasCancelled = false;
        for (var i = 0; i < paths.Length; i++)
        {
            var obj = AssetDatabase.LoadAssetAtPath(paths[i], typeof(GameObject)) as GameObject;
            if (obj == null || !obj) continue;

            if (wasCancelled || EditorUtility.DisplayCancelableProgressBar(
                    "Searching missing references in assets.",
                    $"{paths[i]}",
                    initialProgress + ((i / (float) paths.Length) * progressWeight)
                ))
            {
                onCanceled.Invoke();
                return count;
            }

            count += FindMissingReferences(context, obj, includeChildren);
        }

        onFinished.Invoke();
        return count;
    }

    private static int FindMissingReferences(string context, GameObject go, bool findInChildren = false)
    {
        var count = 0;
        var components = go.GetComponents<Component>();

        for (var j = 0; j < components.Length; j++)
        {
            var c = components[j];
            if (!c)
            {
                LogMissingComponent(context, go);
                // LogMissing($"Missing Component in GameObject: {FullPath(go)} in {context}", go);
                count++;
                continue;
            }

            var so = new SerializedObject(c);
            var sp = so.GetIterator();

            while (sp.NextVisible(true))
            {
                if (sp.propertyType == SerializedPropertyType.ObjectReference)
                {
                    if (sp.objectReferenceValue == null && sp.objectReferenceInstanceIDValue != 0)
                    {
                        LogMissing(context, sp); // TODO: ContextType
                        count++;
                    }
                }
            }
        }

        if (findInChildren)
        {
            foreach (Transform child in go.transform)
            {
                count += FindMissingReferences(context, child.gameObject, true);
            }
        }

        return count;
    }

    private static int FindMissingReferencesInScene(Scene scene, float progressWeightByScene, Action onFinished,
        Action onCanceled, float currentProgress = 0f)
    {
        var rootObjects = scene.GetRootGameObjects();

        var queue = new Queue<ObjectData>();
        foreach (var rootObject in rootObjects)
        {
            queue.Enqueue(
                new ObjectData
                    { ExpectedProgress = progressWeightByScene / rootObjects.Length, GameObject = rootObject }
            );
        }

        var count = FindMissingReferences(
            scene.path,
            queue,
            onFinished,
            onCanceled,
            true,
            currentProgress
        );
        return count;
    }

    private static int FindMissingReferences(string context, Queue<ObjectData> queue, Action onFinished,
        Action onCanceled, bool findInChildren = false, float currentProgress = 0f)
    {
        var count = 0;
        while (queue.Any())
        {
            var data = queue.Dequeue();
            var go = data.GameObject;
            var components = go.GetComponents<Component>();

            float progressEachComponent;
            if (findInChildren)
            {
                progressEachComponent = (data.ExpectedProgress) / (components.Length + go.transform.childCount);
            }
            else
            {
                progressEachComponent = data.ExpectedProgress / components.Length;
            }

            for (var j = 0; j < components.Length; j++)
            {
                currentProgress += progressEachComponent;
                if (EditorUtility.DisplayCancelableProgressBar(
                        $"Searching missing references in {context}",
                        go.name,
                        currentProgress
                    ))
                {
                    onCanceled.Invoke();
                    return count;
                }

                var c = components[j];
                if (!c)
                {
                    LogMissingComponent(context, go);
                    count++;
                    continue;
                }

                using (var so = new SerializedObject(c))
                {
                    using (var sp = so.GetIterator())
                    {
                        while (sp.NextVisible(true))
                        {
                            if (sp.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                if (sp.objectReferenceValue == null
                                    && sp.objectReferenceInstanceIDValue != 0)
                                {
                                    LogMissing(context, sp);
                                    count++;
                                }
                            }
                        }
                    }
                }
            }

            if (findInChildren)
            {
                foreach (Transform child in go.transform)
                {
                    if (child.gameObject == go) continue;
                    queue.Enqueue(
                        new ObjectData { ExpectedProgress = progressEachComponent, GameObject = child.gameObject }
                    );
                }
            }
        }

        onFinished.Invoke();
        return count;
    }

    private static void ShowInitialProgressBar(string searchContext, bool clearConsole = true)
    {
        if (clearConsole)
        {
            Debug.ClearDeveloperConsole();
        }

        EditorUtility.DisplayProgressBar("Missing References Finder", $"Preparing search in {searchContext}", 0f);
    }

    private static void ShowFinishDialog(bool wasCancelled, int count)
    {
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog(
            "Missing References Finder",
            wasCancelled
                ? $"Process cancelled.\n{count} missing references were found.\n Current results are shown as errors in the console."
                : $"Finished finding missing references.\n{count} missing references were found.\n Results are shown as errors in the console.",
            "Ok"
        );
    }

    private static string FullPath(GameObject go)
    {
        var parent = go.transform.parent;
        return parent == null ? go.name : FullPath(parent.gameObject) + "/" + go.name;
    }

    private static void ClearConsole()
    {
        var logEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
        if (logEntries == null) return;

        var clearMethod = logEntries.GetMethod(
            "Clear",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        );
        if (clearMethod == null) return;

        clearMethod.Invoke(null, null);
    }

    private static void LogMissingComponent(string context, GameObject go)
    {
        var message = JsonUtility.ToJson(
            new Missing()
            {
                Type = Missing.MissingType.MissingComponent,
                Context = context,
                Name = go.name,
                AssetPath = AssetDatabase.GetAssetPath(go),
                LocalPath = FullPath(go),
                Component = "[Missing]"
            }
        );
        UnityLog(message, go);
        _logStream?.WriteLine(message);
    }

    private static void LogMissing(string context, SerializedProperty property)
    {
        string message;
        UnityEngine.Object target;
        switch (property.serializedObject.targetObject)
        {
            case Component component:
                var gameObject = component.gameObject;
                var assetPath = AssetDatabase.GetAssetPath(gameObject);
                message = JsonConvert.SerializeObject(
                    new Missing()
                    {
                        Type = Missing.MissingType.MissingReference,
                        Context = context,
                        Name = gameObject.name,
                        AssetPath = assetPath,
                        LocalPath = FullPath(gameObject),
                        Component = component.GetType().Name,
                        PropertyName = property.propertyPath
                    }
                );
                target = String.IsNullOrEmpty(assetPath)
                    ? gameObject
                    : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                break;

            case ScriptableObject so:
                target = so;
                message = JsonConvert.SerializeObject(
                    new Missing()
                    {
                        Type = Missing.MissingType.MissingReference,
                        Context = context,
                        Name = so.name,
                        AssetPath = AssetDatabase.GetAssetPath(so),
                        LocalPath = String.Empty,
                        Component = String.Empty,
                        PropertyName = property.propertyPath
                    }
                );
                break;
            default:
                message = $"TODO: Not supported type {property.serializedObject.targetObject}";
                target = property.serializedObject.targetObject;
                break;
        }

        UnityLog(message, target);
        _logStream?.WriteLine(message);
    }

    private static void UnityLog(string message, UnityEngine.Object context)
    {
        Debug.LogError(message, context);
    }

    private static StreamWriter _logStream;
    private static readonly DirectoryInfo LogDirectory = new DirectoryInfo(Application.dataPath).Parent?
        .CreateSubdirectory("MissingReports");

    private static IDisposable CreateLoggingContext()
    {
        var logPath = Path.Combine(
            LogDirectory.FullName,
            $"MissingReport_{DateTime.Now.ToFileTime()}.log"
        );

        _logStream = new StreamWriter(logPath);
        return _logStream;
    }

    public struct Missing
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public MissingType Type;
        public string Context;
        public string AssetPath;
        public string LocalPath;
        public string Name;
        public string Component;
        public string PropertyName;


        public enum MissingType
        {
            MissingReference,
            MissingComponent
        }

        public enum ContextType
        {
            Project,
            Scene
        }
    }
}
