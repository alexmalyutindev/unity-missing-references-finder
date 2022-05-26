using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UI = UnityEngine.UIElements;
using EUI = UnityEditor.UIElements;
using Image = UnityEngine.UI.Image;
using Object = UnityEngine.Object;

public class MissingReferenceWindow : EditorWindow
{
    private List<MissingReferencesFinder.Missing> _missings;
    private ListView _missingView;

    [MenuItem("Tools/Find Missing References/Window")]
    public static void Open()
    {
        GetWindow<MissingReferenceWindow>();
    }

    private void CreateGUI()
    {
        var filterBar = FilterBar();

        var openFile = new UI.Button(
            () =>
            {
                var path = new DirectoryInfo(Application.dataPath).Parent;
                var logFilePath = EditorUtility.OpenFilePanel(
                    "Open logs",
                    path.CreateSubdirectory("MissingReports").FullName,
                    "log"
                );

                LoadReportFromFile(logFilePath);
            }
        )
        {
            text = "Open"
        };

        _missings = new List<MissingReferencesFinder.Missing>();
        _missingView = new UI.ListView(
            _missings,
            70,
            MakeItem,
            BindItem
        );
        _missingView.selectionType = SelectionType.Multiple;
        _missingView.onItemsChosen += SelectHandler;
        _missingView.style.flexGrow = new StyleFloat(1);

        rootVisualElement.Add(filterBar);
        rootVisualElement.Add(openFile);

        rootVisualElement.Add(_missingView);

        var resetReference = new UI.Button(ResetSelected)
        {
            text = "Reset selected reference"
        };

        rootVisualElement.Add(resetReference);
    }

    private static VisualElement FilterBar()
    {
        var filterBar = new UI.VisualElement()
        {
            style =
            {
                flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row),
                flexGrow = new StyleFloat(StyleKeyword.Auto)
            }
        };
        var filter = new EUI.ToolbarSearchField()
        {
            style = { flexGrow = new StyleFloat(1) }
        };
        var filterButton = new UI.Button(
            () => { } // TODO: Filtering
        )
        {
            text = "Search"
        };
        filterBar.Add(filter);
        filterBar.Add(filterButton);
        return filterBar;
    }

    private VisualElement MakeItem()
    {
        var root = new UI.VisualElement();
        root.Add(
            new UI.Label()
            {
                name = "missing_label"
            }
        );

        var fieldContainer = new UI.VisualElement()
        {
            style = { flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row) }
        };
        fieldContainer.Add(
            new EUI.PropertyField()
            {
                name = "object_field"
            }
        );
        fieldContainer.Add(
            new UI.Button()
            {
                name = "clear_ref",
                text = "Clear Reference"
            }
        );
        root.Add(fieldContainer);
        return root;
    }

    private void BindItem(VisualElement element, int i)
    {
        // Label
        var label = element.Q<UI.Label>("missing_label");
        var missing = _missings[i];
        var fileName = Path.GetFileName(missing.AssetPath);
        label.text = $"{fileName}\n{missing.LocalPath}";


        // Field
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
        var component = GetComponent(asset, missing);

        var field = element.Q<EUI.PropertyField>("object_field");
        field.label = missing.PropertyName;
        field.bindingPath = missing.PropertyName;
        var serializedObject = new SerializedObject(component);
        var property = EUI.BindingExtensions.BindProperty(field, serializedObject);
        _missings[i].SerializedProperty = property;

        Debug.Log(property.objectReferenceInstanceIDValue);

        var clearReference = element.Q<UI.Button>("clear_ref");

        clearReference.clickable = new Clickable(
            () =>
            {
                if (PrefabUtility.IsPartOfVariantPrefab(component))
                {
                    Debug.LogWarning(
                        "[Clear Reference] Trying to clear ref in prefab variant!" +
                        $"{serializedObject.targetObject}",
                        serializedObject.targetObject
                    );
                    return;
                }

                var modification = new PropertyModification()
                {
                    target = component,
                    value = String.Empty,
                    objectReference = null,
                    propertyPath = missing.PropertyName
                };

                serializedObject.Update();
                property.objectReferenceValue = null;
                property.objectReferenceInstanceIDValue = 0;
                serializedObject.ApplyModifiedProperties();
                PrefabUtility.SavePrefabAsset(asset);

                if (PrefabUtility.IsDefaultOverride(modification))
                {
                    PrefabUtility.ApplyPropertyOverride(property, missing.AssetPath, InteractionMode.AutomatedAction);
                }

                Debug.Log($"[Clear Reference] {serializedObject.targetObject}", serializedObject.targetObject);
            }
        );
    }

    private void SelectHandler(IEnumerable<object> obj)
    {
        var missing = (MissingReferencesFinder.Missing) obj.FirstOrDefault()!;

        // var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
        var asset = PrefabUtility.LoadPrefabContents(missing.AssetPath);
        AssetDatabase.OpenAsset(asset.GetInstanceID());

        var component = GetComponent(asset, missing);
        var serializedObject = new SerializedObject(component);

        var property = serializedObject.FindProperty(missing.PropertyName);

        Selection.SetActiveObjectWithContext(component, asset);
        Debug.Log(property);
    }

    private static Component GetComponent(GameObject asset, MissingReferencesFinder.Missing missing)
    {
        var target = asset.transform;
        var splitPath = missing.LocalPath.Split('/');

        for (int i = 1; i < splitPath.Length; i++)
        {
            var childName = splitPath[i];
            target = target.Find(childName);
        }

        var component = target.GetComponent(Type.GetType(missing.ComponentType));
        return component;
    }

    private void ResetSelected()
    {
        AssetDatabase.StartAssetEditing();
        foreach (MissingReferencesFinder.Missing missing in _missingView.selectedItems)
        {
            // var asset = PrefabUtility.LoadPrefabContents(missing.AssetPath);
            // var component = GetComponent(asset, missing);
            // var serializedObject = new SerializedObject(component);
            // var property = serializedObject.FindProperty(missing.PropertyName);
            //
            // AssetDatabase.StartAssetEditing();
            //
            // // property.DeleteCommand();
            // // property.objectReferenceInstanceIDValue = 0;
            //
            // var propertyModification = new PropertyModification()
            // {
            //     target = component,
            //     objectReference = null,
            //     propertyPath = missing.PropertyName
            // };
            // PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            // PrefabUtility.SetPropertyModifications(asset, new [] { propertyModification });
            //
            // AssetDatabase.StopAssetEditing();
            //
            // PrefabUtility.SaveAsPrefabAsset(asset, missing.AssetPath);
            // Debug.Log($"Fix: {missing.AssetPath}");

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
            var property = missing.SerializedProperty;
            var serializedObject = property.serializedObject;

            if (PrefabUtility.IsPartOfVariantPrefab(serializedObject.targetObject))
            {
                Debug.LogWarning(
                    "[Clear Reference] Trying to clear ref in prefab variant!\n" +
                    $"{serializedObject.targetObject}",
                    serializedObject.targetObject
                );
                continue;
            }


            serializedObject.Update();
            property.objectReferenceValue = null;
            property.objectReferenceInstanceIDValue = 0;
            serializedObject.ApplyModifiedProperties();
            PrefabUtility.SavePrefabAsset(asset);
            Debug.Log($"[Clear] {serializedObject.targetObject}", serializedObject.targetObject);
        }

        AssetDatabase.StopAssetEditing();
    }

    private async void LoadReportFromFile(string logFilePath)
    {
        if (String.IsNullOrEmpty(logFilePath))
            return;

        _missings.Clear();
        Debug.Log($"Open [{logFilePath}]...");

        using var stream = File.OpenRead(logFilePath);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            var missing = JsonConvert.DeserializeObject<MissingReferencesFinder.Missing>(line);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
            var component = GetComponent(asset, missing);
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(missing.PropertyName);
            missing.SerializedProperty = property;

            _missings.Add(missing);
        }

        _missingView.Refresh();
        Debug.Log("Opened!");
    }
}
