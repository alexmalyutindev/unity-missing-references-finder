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

public class MissingReferenceWindow : EditorWindow
{
    [SerializeField]
    private List<int> _selected;

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
            name = "load_report",
            text = "Open"
        };

        _missings = new List<MissingReferencesFinder.Missing>();
        _missingView = new UI.ListView(_missings, 70, MakeItem, BindItem)
        {
            selectionType = SelectionType.Multiple,
            style = { flexGrow = new StyleFloat(1) }
        };
        _missingView.onItemsChosen += SelectHandler;
        _missingView.onSelectionChange += SelectionChangedHandler;

        rootVisualElement.Add(filterBar);
        rootVisualElement.Add(openFile);

        rootVisualElement.Add(_missingView);

        var resetReference = new UI.Button(ResetSelected)
        {
            text = "Reset selected reference"
        };

        rootVisualElement.Add(resetReference);
        
        Undo.undoRedoPerformed += () =>
        {
            if (Undo.GetCurrentGroupName() == "MissingView_SelectionChanged")
            {
                _missingView.SetSelection(_selected);
            }
        };
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
        var root = new UI.VisualElement()
        {
            style =
            {
                borderBottomWidth = 1,
                borderBottomColor = new StyleColor(Color.black),
                paddingTop = new StyleLength(2),
                paddingLeft = new StyleLength(2)
            }
        };

        root.Add(new UI.Label() { name = "asset_name" });
        root.Add(
            new UI.Label()
            {
                name = "local_path",
                // style =
                // {
                //     textOverflow = new StyleEnum<TextOverflow>(TextOverflow.Ellipsis),
                //     whiteSpace = new StyleEnum<WhiteSpace>(WhiteSpace.Normal),
                //     display = new StyleEnum<DisplayStyle>(DisplayStyle.Flex)
                // }
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
        var nameLabel = element.Q<UI.Label>("asset_name");
        var missing = _missings[i];
        var fileName = Path.GetFileName(missing.AssetPath);
        nameLabel.text = $"{fileName}";

        var localPath = element.Q<Label>("local_path");
        localPath.text = missing.LocalPath;
        localPath.tooltip = missing.LocalPath;


        // Field
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
        var component = GetComponent(asset, missing);

        var field = element.Q<EUI.PropertyField>("object_field");
        field.label = missing.PropertyName;
        field.bindingPath = missing.PropertyName;
        var serializedObject = new SerializedObject(component);
        var property = EUI.BindingExtensions.BindProperty(field, serializedObject);
        _missings[i].SerializedProperty = property;

        var clearReference = element.Q<UI.Button>("clear_ref");
        clearReference.clickable = new Clickable(
            () => ClearReference(serializedObject, property, asset)
        );
    }

    private void SelectionChangedHandler(IEnumerable<object> obj)
    {
        Undo.RecordObject(this, "MissingView_SelectionChanged");
        _selected = _missingView.selectedIndices.ToList();
    }

    private void SelectHandler(IEnumerable<object> obj)
    {
        var missing = (MissingReferencesFinder.Missing) obj.FirstOrDefault()!;

        AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath));
        var asset = PrefabUtility.LoadPrefabContents(missing.AssetPath);


        var component = GetComponent(asset, missing);
        var serializedObject = new SerializedObject(component);

        var property = serializedObject.FindProperty(missing.PropertyName);

        Selection.SetActiveObjectWithContext(component, asset);
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
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
            var property = missing.SerializedProperty;
            var serializedObject = property.serializedObject;

            // TODO: Reset default overrides!
            // var overrides = PrefabUtility.GetObjectOverrides(asset, true);

            ClearReference(serializedObject, property, asset);
        }

        AssetDatabase.StopAssetEditing();
    }

    private static void ClearReference(SerializedObject serializedObject, SerializedProperty property, GameObject asset)
    {
        serializedObject.Update();
        property.objectReferenceValue = null;
        property.objectReferenceInstanceIDValue = 0;
        serializedObject.ApplyModifiedProperties();
        PrefabUtility.SavePrefabAsset(asset);
        Debug.Log($"[Clear] {serializedObject.targetObject}", serializedObject.targetObject);
    }

    private async void LoadReportFromFile(string logFilePath)
    {
        if (String.IsNullOrEmpty(logFilePath))
            return;

        _missings.Clear();
        _missingView.Refresh();

        var loadButton = rootVisualElement.Q<UI.Button>("load_report");

        Debug.Log($"Loading... [{logFilePath}]");
        loadButton.SetEnabled(false);
        var defaultText = loadButton.text;
        loadButton.text = "Loading...";

        using var stream = File.OpenRead(logFilePath);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            var missing = JsonConvert.DeserializeObject<MissingReferencesFinder.Missing>(line);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
            try
            {
                var component = GetComponent(asset, missing);
                var serializedObject = new SerializedObject(component);
                var property = serializedObject.FindProperty(missing.PropertyName);
                missing.SerializedProperty = property;

                _missings.Add(missing);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Format error:\n{line}");
            }
        }

        _missingView.Refresh();
        loadButton.SetEnabled(true);
        loadButton.text = defaultText;

        Debug.Log("Loaded!");
    }
}
