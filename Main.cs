using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using ModLoader;
using ModLoader.Helpers;
using SFS.Builds;
using SFS.Input;
using SFS.IO;
using SFS.UI.ModGUI;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GUIType = SFS.UI.ModGUI.Type;

namespace BPEditor
{
    public class Main : Mod
    {
        public static Main main;
        public static FolderPath modFolder;

        public override string ModNameID => "BPEditor";
        public override string DisplayName => "BP Editor";
        public override string Author => "Cratior";
        public override string MinimumGameVersionNecessary => "1.6.00.16";
        public override string ModVersion => "0.9.1";
        public override string Description => "Improved part editor with visual handles, multi-select, and rich property controls.";

        public Dictionary<string, FilePath> UpdatableFiles => new Dictionary<string, FilePath>
        {
            {
                "https://github.com/uneven-coder/BP-Editor/releases/latest/download/BP-Editor.dll",
                new FolderPath(ModFolder).ExtendToFile("BP-Editor.dll")
            }
        };

        public static bool BlockWhileTyping() => !EditorCore.IsTyping;

        public override void Early_Load()
        {
            main = this;
            modFolder = new FolderPath(ModFolder);
            Config.InitSettings();
        }

        public override void Load()
        {
            Config.RegisterMenu();
            var harmony = new Harmony("com.bpeditor");
            harmony.PatchAll(typeof(Main).Assembly);
            var blockPrefix = new HarmonyMethod(typeof(Main), nameof(BlockWhileTyping));
            foreach (string candidate in new[] { "HandleShortcuts", "HandleKeyboard", "KeyboardInput", "Update" })
            {
                try
                {
                    var m = typeof(BuildManager).GetMethod(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null) harmony.Patch(m, blockPrefix);
                }
                catch (Exception) { }
            }
            try
            {
                var ifaceMap = typeof(KeybindingsPC.Key).GetInterfaceMap(typeof(I_Key));
                foreach (var target in ifaceMap.TargetMethods)
                    if (target.Name.Contains("IsKeyDown") || target.Name.Contains("IsKeyStay"))
                        harmony.Patch(target, blockPrefix);
            }
            catch (Exception) { }
            SceneHelper.OnBuildSceneLoaded += OnBuildSceneLoaded;
        }

        static void OnBuildSceneLoaded()
        {
            EditorCore.Initialize();
            BuildSelector selector = BuildManager.main.selector;
            selector.onSelectedChange = (Action)Delegate.Combine(selector.onSelectedChange, new Action(EditorCore.OnSelectionChanged));
        }
    }

    /** <summary>Persistent settings and configuration menu for BP Editor.</summary> */
    public class Config : ModSettings<Config.Data>
    {
        public static Config main;

        protected override FilePath SettingsFile => Main.modFolder.ExtendToFile("BPEditor.Config.txt");
        protected override void RegisterOnVariableChange(Action onChange) => Application.quitting += onChange;

        /** <summary>Initialises the settings object; safe to call in Early_Load.</summary> */
        public static void InitSettings()
        {
            main = new Config();
            main.Initialize();
        }

        /** <summary>Registers the config menu entry; must be called in Load after UITools is ready.</summary> */
        public static void RegisterMenu()
        {
            ConfigurationMenu.Add("BP Editor", new (string, Func<Transform, GameObject>)[]
            {
                ("Settings", t => BuildMenu(t, ConfigurationMenu.ContentSize))
            });
        }

        public static Data S => settings;

        public static bool CtrlToggled;
        public static bool AltToggled;

        public static float GetSensitivity()
        {
            Data s = S;
            if (IsCtrlActive(s)) return s.CtrlSensitivity;
            if (IsAltActive(s)) return s.AltSensitivity;
            return s.NormalSensitivity;
        }

        public static bool IsCtrlActive() => IsCtrlActive(S);
        public static bool IsAltActive() => IsAltActive(S);

        static bool IsCtrlActive(Data s) => s.CtrlIsToggle
            ? CtrlToggled
            : Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        static bool IsAltActive(Data s) => s.AltIsToggle
            ? AltToggled
            : Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        static GameObject BuildMenu(Transform parent, Vector2Int size)
        {
            Box box = Builder.CreateBox(parent, size.x, size.y, 0, 0, 0.3f);
            box.CreateLayoutGroup(GUIType.Vertical, TextAnchor.UpperCenter, 14f, new RectOffset(15, 15, 12, 12), true);
            int w = size.x - 60, inp = 72, sld = w - 130 - inp - 4;
            Builder.CreateLabel(box, size.x, 46, 0, 0, "BP Editor");
            Builder.CreateLabel(box, w, 26, 0, 0, "Sensitivity").FontSize = 13f;
            NumRow(box, w, sld, inp, "Normal", () => S.NormalSensitivity, v => S.NormalSensitivity = Mathf.Max(0.001f, v), 0.001f, 5f);
            NumRow(box, w, sld, inp, "Ctrl Fine", () => S.CtrlSensitivity, v => S.CtrlSensitivity = Mathf.Max(0.001f, v), 0.001f, 2f);
            NumRow(box, w, sld, inp, "Alt Ultra", () => S.AltSensitivity, v => S.AltSensitivity = Mathf.Max(0.001f, v), 0.001f, 1f);
            Builder.CreateSeparator(box, w - 20, 0, 0);
            Builder.CreateToggleWithLabel(box, w, 32, () => S.CtrlIsToggle, () => { S.CtrlIsToggle = !S.CtrlIsToggle; CtrlToggled = false; }, 0, 0, "Ctrl as Toggle");
            Builder.CreateToggleWithLabel(box, w, 32, () => S.AltIsToggle, () => { S.AltIsToggle = !S.AltIsToggle; AltToggled = false; }, 0, 0, "Alt as Toggle");
            Builder.CreateSeparator(box, w - 20, 0, 0);
            Builder.CreateLabel(box, w, 26, 0, 0, "Handle Outline Colour").FontSize = 13f;
            Box previewBox = Builder.CreateBox(box, w, 22, 0, 0, 1f);
            Image previewImg = previewBox.gameObject.GetComponent<Image>() ?? previewBox.gameObject.AddComponent<Image>();
            previewImg.color = new Color(S.OutlineR, S.OutlineG, S.OutlineB);
            previewBox.gameObject.AddComponent<ColourPreviewUpdater>();
            NumRow(box, w, sld, inp, "Red", () => S.OutlineR, v => S.OutlineR = Mathf.Clamp01(v), 0f, 1f, previewImg);
            NumRow(box, w, sld, inp, "Green", () => S.OutlineG, v => S.OutlineG = Mathf.Clamp01(v), 0f, 1f, previewImg);
            NumRow(box, w, sld, inp, "Blue", () => S.OutlineB, v => S.OutlineB = Mathf.Clamp01(v), 0f, 1f, previewImg);
            Builder.CreateSeparator(box, w - 20, 0, 0);
            NumRow(box, w, sld, inp, "Box Line Width", () => S.BBoxLineWidth, v => S.BBoxLineWidth = Mathf.Max(0.001f, v), 0.001f, 0.1f);
            NumRow(box, w, sld, inp, "Min Bounds Size", () => S.MinBoundsDisplay, v => S.MinBoundsDisplay = Mathf.Max(0f, v), 0f, 2f);
            return box.gameObject;
        }

        static void NumRow(Box parent, int w, int sliderW, int inputW, string label,
            Func<float> getter, Action<float> setter, float min, float max, Image previewToUpdate = null)
        {
            Container row = Builder.CreateContainer(parent, 0, 0);
            row.CreateLayoutGroup(GUIType.Horizontal, TextAnchor.MiddleCenter, 4f, null, true);
            int labelW = w - sliderW - inputW - 8;
            Builder.CreateLabel(row.gameObject.transform, labelW, 32, 0, 0, label).TextAlignment = TextAlignmentOptions.MidlineLeft;
            string pending = getter().ToString("F4", CultureInfo.InvariantCulture);
            TextInput ti = Builder.CreateTextInput(row.gameObject.transform, inputW, 32, 0, 0, pending,
                new UnityAction<string>(txt => pending = txt ?? ""));
            Action<float> apply = v =>
            {
                setter(v);
                if (previewToUpdate != null) previewToUpdate.color = new Color(S.OutlineR, S.OutlineG, S.OutlineB);
            };
            var tmpField = ti.gameObject.GetComponentInChildren<TMP_InputField>();
            if (tmpField != null)
                tmpField.onEndEdit.AddListener(txt => { if (float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) apply(v); });
            else
            {
                var legField = ti.gameObject.GetComponentInChildren<InputField>();
                if (legField != null)
                    legField.onEndEdit.AddListener(txt => { if (float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) apply(v); });
            }
            Builder.CreateSlider(row.gameObject.transform, sliderW, getter(),
                new ValueTuple<float, float>(min, max), false, val => apply(val), val => val.ToString("F3"));
        }

        class ColourPreviewUpdater : MonoBehaviour
        {
            Image img;
            void Awake() => img = GetComponent<Image>();
            void Update() { if (img != null) img.color = new Color(S.OutlineR, S.OutlineG, S.OutlineB); }
        }

        /** <summary>Serialised settings data.</summary> */
        public class Data
        {
            public float NormalSensitivity = 1.0f;
            public float CtrlSensitivity = 0.1f;
            public float AltSensitivity = 0.05f;
            public bool LockAspect = false;
            public bool LockBounds = false;
            public float OutlineR = 1f;
            public float OutlineG = 1f;
            public float OutlineB = 1f;
            public float BBoxLineWidth = 0.013f;
            public float MinBoundsDisplay = 0.70f;
            public bool CtrlIsToggle = false;
            public bool AltIsToggle = false;
        }
    }

    [HarmonyPatch(typeof(BuildManager), "OnDrag")]
    static class Patch_BuildManager_OnDrag
    {
        [HarmonyPrefix]
        static bool Prefix() => !HandleSystem.IsCapturing;
    }

    [HarmonyPatch(typeof(BuildManager), "OnInputStart")]
    static class Patch_BuildManager_OnInputStart
    {
        [HarmonyPrefix]
        static bool Prefix()
        {
            if (Camera.main == null || HandleSystem.instance == null) return true;
            Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            return !HandleSystem.instance.TryBeginDragExternal(mouseWorld);
        }
    }

    [HarmonyPatch(typeof(BuildManager), "OnInputMove")]
    static class Patch_BuildManager_OnInputMove
    {
        [HarmonyPrefix]
        static bool Prefix() => !HandleSystem.IsCapturing;
    }

    [HarmonyPatch(typeof(BuildManager), "OnInputEnd")]
    static class Patch_BuildManager_OnInputEnd
    {
        [HarmonyPrefix]
        static bool Prefix() => !HandleSystem.IsCapturing;
    }
}
