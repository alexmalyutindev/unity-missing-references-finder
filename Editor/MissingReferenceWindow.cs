using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UI = UnityEngine.UIElements;

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
        var filterBar = new UI.VisualElement()
        {
            style =
            {
                flexDirection = new StyleEnum<FlexDirection>(FlexDirection.Row),
                flexGrow = new StyleFloat(StyleKeyword.Auto)
            }
        };
        var filter = new UI.TextField("Filter")
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


        var openFile = new UI.Button(
            () =>
            {
                var path = new DirectoryInfo(Application.dataPath).Parent;
                var logFilePath = EditorUtility.OpenFilePanel(
                    "Open logs",
                    path.CreateSubdirectory("MissingReports").FullName,
                    "log"
                );

                LoadFile(logFilePath);
            }
        )
        {
            text = "Open"
        };

        _missings = new List<MissingReferencesFinder.Missing>();
        _missingView = new UI.ListView(
            _missings,
            25,
            () => new UI.Label(),
            (element, i) =>
            {
                var label = element as Label;
                var missing = _missings[i];
                label.text = $"{missing.Component} : {missing.Name} : {missing.PropertyName}";
            }
        );
        _missingView.selectionType = SelectionType.Multiple;
        _missingView.onItemsChosen += o => Debug.Log(o);
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

    private void ResetSelected()
    {
        foreach (MissingReferencesFinder.Missing missing in _missingView.selectedItems)
        {
            Debug.Log(missing);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(missing.AssetPath);
            
            var target = asset.transform;
            
            var splitPath = missing.LocalPath.Split('/');

            for (int i = 1; i < splitPath.Length; i++)
            {
                var childName = splitPath[i];
                target = target.Find(childName);
                Debug.Log(target);
            }

            var component = target.GetComponent(Type.GetType(missing.ComponentType));
            // TODO: Get property via Reflection or Unity SerializedProperty!
            
            Debug.Log(component);
        }
    }

    private async void LoadFile(string logFilePath)
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
            _missings.Add(missing);
        }

        _missingView.Refresh();
        Debug.Log("Opened!");
    }
}
