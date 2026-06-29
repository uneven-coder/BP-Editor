using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.UI.ModGUI;
using UITools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BPEditor
{
    /** <summary>Builds and drives the BP Editor side panel window.</summary> */
    public static class EditorPanel
    {
        const int WindowW = 520;
        const int WindowH = 720;
        const int RowH = 40;
        const int SecPadH = 7;
        const int LabelW = 110;
        const int BtnW = 48;

        static int RowW => WindowW - 10 - SecPadH * 2;
        static int InputW => RowW - LabelW - 4 - BtnW - 4 - BtnW - 4;

        static float groupScaleFactor = 1.1f;
        static float groupRotateDegrees = 15f;
        static float radialRadius = 3f;

        static GameObject holder;
        static Window window;
        static GameObject typingBanner;

        /** <summary>Creates the window on first call; subsequent calls are no-ops.</summary> */
        public static void Initialize() => BuildWindow();

        static void BuildWindow()
        {
            if (holder == null)
                holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "BPEditor_Panel");
            if (window == null || window.gameObject == null)
            {
                Vector2 canvas = UIUtility.CanvasPixelSize;
                int startX = -(int)(canvas.x * 0.5f) + WindowW / 2 + 20;
                window = UIToolsBuilder.CreateClosableWindow(holder.transform, 0, WindowW, WindowH, startX, 0, true, true, 1f, "BP Editor", false);
                window.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperCenter, 8, new RectOffset(5, 5, 6, 6), true);
                window.EnableScrolling(SFS.UI.ModGUI.Type.Vertical);
                window.RegisterPermanentSaving("BPEditor.Window");
                BuildTypingBanner();
            }
            ClearChildren();
        }

        static void BuildTypingBanner()
        {
            Box box = Builder.CreateBox(window.gameObject.transform, WindowW - 10, 30, 0, 0, 0.12f);
            var rt = box.gameObject.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(WindowW - 10, 30f);
            rt.anchoredPosition = new Vector2(0f, -44f);
            var img = box.gameObject.GetComponent<Image>() ?? box.gameObject.AddComponent<Image>();
            img.color = new Color(0.80f, 0.60f, 0.10f, 0.62f);
            var lbl = Builder.CreateLabel(box, WindowW - 14, 26, 0, -13, "Keybinds Paused");
            lbl.FontSize = 15f;
            box.gameObject.transform.SetAsLastSibling();
            typingBanner = box.gameObject;
            typingBanner.SetActive(false);
        }

        static void ClearChildren()
        {
            Transform ch = window.ChildrenHolder;
            for (int i = ch.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(ch.GetChild(i).gameObject);
        }

        /** <summary>Shows or hides the "Keybinds Paused" banner.</summary> */
        public static void SetTypingIndicator(bool active) => typingBanner?.SetActive(active);

        /** <summary>Clears and rebuilds all panel rows for the current selection.</summary> */
        public static void Rebuild()
        {
            if (window == null) BuildWindow();
            ClearChildren();
            if (!EditorCore.HasSelection)
            {
                Builder.CreateLabel(window, WindowW - 10, RowH, 0, 0, "Select a part to edit").Opacity = 0.45f;
                Relayout(); return;
            }
            var posRows = CreateSection("Position");
            AddNumRow(posRows, "X", 0.5f,
                () => EditorCore.GetCommonFloat(p => p.Position.x, out _),
                v => { foreach (Part p in EditorCore.Selection) PartOps.SetPositionX(p, v); });
            AddNumRow(posRows, "Y", 0.5f,
                () => EditorCore.GetCommonFloat(p => p.Position.y, out _),
                v => { foreach (Part p in EditorCore.Selection) PartOps.SetPositionY(p, v); });
            var oriRows = CreateSection("Orientation");
            AddNumRow(oriRows, "Scale X", 0.1f,
                () => EditorCore.GetCommonFloat(p => p.orientation.orientation.Value.x, out _),
                v => { foreach (Part p in EditorCore.Selection) PartOps.SetOrientationX(p, v); });
            AddNumRow(oriRows, "Scale Y", 0.1f,
                () => EditorCore.GetCommonFloat(p => p.orientation.orientation.Value.y, out _),
                v => { foreach (Part p in EditorCore.Selection) PartOps.SetOrientationY(p, v); });
            AddNumRow(oriRows, "Rotation", 15f,
                () => EditorCore.GetCommonFloat(p => p.orientation.orientation.Value.z, out _),
                v => { foreach (Part p in EditorCore.Selection) PartOps.SetRotation(p, v); });
            var actRows = CreateSection("Actions");
            AddBtnRow(actRows, "Flip H", "Flip V",
                () =>
                {
                    if (EditorCore.IsSingle) PartOps.FlipHorizontal(EditorCore.SinglePart);
                    else PartOps.FlipHorizontalMulti(EditorCore.Selection);
                    EditorCore.NotifyModified();
                },
                () =>
                {
                    if (EditorCore.IsSingle) PartOps.FlipVertical(EditorCore.SinglePart);
                    else PartOps.FlipVerticalMulti(EditorCore.Selection);
                    EditorCore.NotifyModified();
                });
            AddBtnRow(actRows, "Rot +90°", "Rot −90°",
                () =>
                {
                    if (EditorCore.IsSingle) PartOps.AddRotation(EditorCore.SinglePart, 90f);
                    else PartOps.RotateMulti(EditorCore.Selection, 90f);
                    EditorCore.NotifyModified();
                },
                () =>
                {
                    if (EditorCore.IsSingle) PartOps.AddRotation(EditorCore.SinglePart, -90f);
                    else PartOps.RotateMulti(EditorCore.Selection, -90f);
                    EditorCore.NotifyModified();
                });
            if (EditorCore.IsMulti)
            {
                var grpRows = CreateSection("Group Ops");
                AddGroupScaleRow(grpRows);
                AddGroupRotateRow(grpRows);
                var alignRows = CreateSection("Align");
                AddBtnRow(alignRows, "Align L", "Align R",
                    () => PartOps.AlignLeft(EditorCore.Selection),
                    () => PartOps.AlignRight(EditorCore.Selection));
                AddBtnRow(alignRows, "Align T", "Align B",
                    () => PartOps.AlignTop(EditorCore.Selection),
                    () => PartOps.AlignBottom(EditorCore.Selection));
                AddBtnRow(alignRows, "Centre H", "Centre V",
                    () => PartOps.AlignCentreH(EditorCore.Selection),
                    () => PartOps.AlignCentreV(EditorCore.Selection));
                AddBtnRow(alignRows, "Dist H", "Dist V",
                    () => PartOps.DistributeH(EditorCore.Selection),
                    () => PartOps.DistributeV(EditorCore.Selection));
                AddRadialRow(alignRows);
            }
            BuildVariablesSection();
            BuildControlsSection();
            Relayout();
        }

        static void BuildVariablesSection()
        {
            if (EditorCore.IsSingle)
            {
                var doubleKeys = CollectKeys(p => p.variablesModule?.doubleVariables.GetSaveDictionary().Keys);
                if (doubleKeys.Count == 0) return;
                var dvRows = CreateSection("Size / Variables");
                foreach (string key in doubleKeys)
                {
                    string k = key;
                    AddNumRow(dvRows, FriendlyName(k), 0.1f,
                        () => (float)EditorCore.GetCommonDouble(p =>
                        {
                            var vm = p.variablesModule;
                            return vm != null && vm.doubleVariables.Has(k) ? vm.doubleVariables.GetValue(k) : 0.0;
                        }, out _),
                        v =>
                        {
                            EditorCore.WithSuppressedEvents(() => PartOps.SetDoubleVariableMulti(EditorCore.Selection, k, v));
                            HandleSystem.RefreshHandles();
                            EditorCore.NotifyModified();
                        },
                        delta =>
                        {
                            EditorCore.WithSuppressedEvents(() => PartOps.StepDoubleVariableMulti(EditorCore.Selection, k, delta));
                            HandleSystem.RefreshHandles();
                            EditorCore.NotifyModified();
                        });
                }
            }
            else
            {
                var keyToParts = new Dictionary<string, List<Part>>();
                var keyOrder = new List<string>();
                foreach (Part part in EditorCore.Selection)
                {
                    if (part.variablesModule == null) continue;
                    foreach (string key in part.variablesModule.doubleVariables.GetSaveDictionary().Keys)
                    {
                        if (!keyToParts.ContainsKey(key)) { keyToParts[key] = new List<Part>(); keyOrder.Add(key); }
                        keyToParts[key].Add(part);
                    }
                }
                if (keyOrder.Count == 0) return;
                var dvRows = CreateSection("Variables");
                foreach (string key in keyOrder)
                {
                    if (keyToParts[key].Count <= 1) continue;
                    string k = key;
                    var parts = keyToParts[k];
                    AddNumRow(dvRows, FriendlyName(k), 0.1f,
                        () =>
                        {
                            double first = 0; bool set = false;
                            foreach (Part p in parts)
                            {
                                var vm = p.variablesModule;
                                if (vm == null || !vm.doubleVariables.Has(k)) continue;
                                double v = vm.doubleVariables.GetValue(k);
                                if (!set) { first = v; set = true; }
                                else if (Math.Abs(v - first) >= 1e-9) return float.NaN;
                            }
                            return (float)first;
                        },
                        v =>
                        {
                            EditorCore.WithSuppressedEvents(() => PartOps.SetDoubleVariableMulti(parts, k, v));
                            HandleSystem.RefreshHandles(); EditorCore.NotifyModified();
                        },
                        delta =>
                        {
                            EditorCore.WithSuppressedEvents(() => PartOps.StepDoubleVariableMulti(parts, k, delta));
                            HandleSystem.RefreshHandles(); EditorCore.NotifyModified();
                        });
                }
                foreach (Part part in EditorCore.Selection)
                {
                    var uniqueKeys = keyOrder.Where(k => keyToParts[k].Count == 1 && keyToParts[k][0] == part).ToList();
                    if (uniqueKeys.Count == 0) continue;
                    AddSubHeader(dvRows, PartDisplayName(part));
                    foreach (string key in uniqueKeys)
                    {
                        string k = key;
                        Part cap = part;
                        AddNumRow(dvRows, FriendlyName(k), 0.1f,
                            () => { var vm = cap.variablesModule; return vm != null && vm.doubleVariables.Has(k) ? (float)vm.doubleVariables.GetValue(k) : 0f; },
                            v =>
                            {
                                EditorCore.WithSuppressedEvents(() => PartOps.SetDoubleVariable(cap, k, v));
                                HandleSystem.RefreshHandles(); EditorCore.NotifyModified();
                            },
                            delta =>
                            {
                                EditorCore.WithSuppressedEvents(() => PartOps.StepDoubleVariableMulti(new[] { cap }, k, delta));
                                HandleSystem.RefreshHandles(); EditorCore.NotifyModified();
                            });
                    }
                }
            }
        }

        static void BuildControlsSection()
        {
            if (EditorCore.IsSingle) BuildSinglePartControls();
            else BuildMultiPartControls();
        }

        static void BuildSinglePartControls()
        {
            Part part = EditorCore.SinglePart;
            if (part == null) return;
            var stringKeys = part.variablesModule?.stringVariables.GetSaveDictionary().Keys.ToList() ?? new List<string>();
            var boolKeys = part.variablesModule?.boolVariables.GetSaveDictionary().Keys.ToList() ?? new List<string>();
            var vds = part.GetComponentsInChildren<VariablesDrawer>();
            var tms = part.GetComponentsInChildren<ToggleModule>();
            bool hasDrawer = vds.Any(vd => vd.elements != null && vd.elements.Any(
                e => e.variableType == VariablesDrawer.VariableType.Bool || e.variableType == VariablesDrawer.VariableType.String));
            bool hasToggles = tms.Any(tm => tm.state != null);
            if (!hasDrawer && !hasToggles && stringKeys.Count == 0 && boolKeys.Count == 0) return;
            Transform rows = CreateSection("Controls");
            var shownBool = new HashSet<string>();
            var shownString = new HashSet<string>();
            foreach (VariablesDrawer vd in vds)
            {
                if (vd.elements == null) continue;
                foreach (VariablesDrawer.DrawElement el in vd.elements)
                {
                    if (el.variableType == VariablesDrawer.VariableType.Float) continue;
                    string lbl = el.label?.Field ?? "?";
                    Part capturedPart = part;
                    VariablesDrawer.DrawElement capturedEl = el;
                    if (el.variableType == VariablesDrawer.VariableType.Bool)
                    {
                        if (capturedEl.boolReference == null) continue;
                        string varName = GetRefVarName(capturedEl.boolReference);
                        if (!string.IsNullOrEmpty(varName)) shownBool.Add(varName);
                        string capturedVar = varName;
                        AddToggleRow(rows, lbl,
                            () => capturedEl.boolReference.Value,
                            () =>
                            {
                                if (!string.IsNullOrEmpty(capturedVar))
                                    PartOps.ToggleBoolVariable(capturedPart, capturedVar);
                                else
                                {
                                    capturedEl.boolReference.Value = !capturedEl.boolReference.Value;
                                    AdaptModule.UpdateAdaptation(new[] { capturedPart });
                                    capturedPart.RegenerateMesh();
                                }
                            });
                    }
                    else if (el.variableType == VariablesDrawer.VariableType.String)
                    {
                        if (capturedEl.stringReference == null) continue;
                        string varName = GetRefVarName(capturedEl.stringReference);
                        if (!string.IsNullOrEmpty(varName)) shownString.Add(varName);
                        string capturedVar = varName;
                        AddTextRow(rows, lbl,
                            () => capturedEl.stringReference.Value,
                            v =>
                            {
                                if (!string.IsNullOrEmpty(capturedVar))
                                    PartOps.SetStringVariable(capturedPart, capturedVar, v);
                                else
                                {
                                    capturedEl.stringReference.Value = v;
                                    AdaptModule.UpdateAdaptation(new[] { capturedPart });
                                    capturedPart.RegenerateMesh();
                                }
                            });
                    }
                }
            }
            foreach (ToggleModule tm in tms)
            {
                if (tm.state == null) continue;
                ToggleModule capturedTm = tm;
                string lbl = SafeLabel(tm);
                AddToggleRow(rows, lbl, () => GetToggleState(capturedTm), () => { capturedTm.state.Toggle(); Undo.main?.CreateNewStep(lbl); });
            }
            foreach (string key in stringKeys)
            {
                if (shownString.Contains(key)) continue;
                string k = key;
                AddTextRow(rows, FriendlyName(k), () => GetCommonStringVar(k), val =>
                {
                    EditorCore.WithSuppressedEvents(() => PartOps.SetStringVariableMulti(EditorCore.Selection, k, val));
                    HandleSystem.RefreshHandles(); EditorCore.NotifyModified();
                });
            }
            foreach (string key in boolKeys)
            {
                if (shownBool.Contains(key)) continue;
                string k = key;
                AddToggleRow(rows, FriendlyName(k),
                    () => EditorCore.Selection.All(p => p.variablesModule?.boolVariables.Has(k) == true && p.variablesModule.boolVariables.GetValue(k)),
                    () =>
                    {
                        EditorCore.WithSuppressedEvents(() => PartOps.ToggleBoolVariableMulti(EditorCore.Selection, k));
                        HandleSystem.RefreshHandles(); EditorCore.NotifyModified();
                    });
            }
        }

        static void BuildMultiPartControls()
        {
            var boolDrawer = new Dictionary<string, (string lbl, List<(Part part, VariablesDrawer.DrawElement el)> list)>();
            var boolOrder = new List<string>();
            var stringDrawer = new Dictionary<string, (string lbl, List<(Part part, VariablesDrawer.DrawElement el)> list)>();
            var stringOrder = new List<string>();
            var toggles = new Dictionary<string, List<(Part part, ToggleModule tm)>>();
            var toggleOrder = new List<string>();
            var rawStr = new Dictionary<string, List<Part>>();
            var rawStrOrder = new List<string>();
            var rawBool = new Dictionary<string, List<Part>>();
            var rawBoolOrder = new List<string>();
            foreach (Part part in EditorCore.Selection)
            {
                foreach (VariablesDrawer vd in part.GetComponentsInChildren<VariablesDrawer>())
                {
                    if (vd.elements == null) continue;
                    foreach (VariablesDrawer.DrawElement el in vd.elements)
                    {
                        if (el.variableType == VariablesDrawer.VariableType.Float) continue;
                        if (el.variableType == VariablesDrawer.VariableType.Bool && el.boolReference != null)
                        {
                            string varName = GetRefVarName(el.boolReference);
                            if (string.IsNullOrEmpty(varName)) continue;
                            if (!boolDrawer.ContainsKey(varName)) { boolDrawer[varName] = (el.label?.Field ?? "?", new List<(Part, VariablesDrawer.DrawElement)>()); boolOrder.Add(varName); }
                            boolDrawer[varName].list.Add((part, el));
                        }
                        else if (el.variableType == VariablesDrawer.VariableType.String && el.stringReference != null)
                        {
                            string varName = GetRefVarName(el.stringReference);
                            if (string.IsNullOrEmpty(varName)) continue;
                            if (!stringDrawer.ContainsKey(varName)) { stringDrawer[varName] = (el.label?.Field ?? "?", new List<(Part, VariablesDrawer.DrawElement)>()); stringOrder.Add(varName); }
                            stringDrawer[varName].list.Add((part, el));
                        }
                    }
                }
                foreach (ToggleModule tm in part.GetComponentsInChildren<ToggleModule>())
                {
                    if (tm.state == null) continue;
                    string lbl = SafeLabel(tm);
                    if (!toggles.ContainsKey(lbl)) { toggles[lbl] = new List<(Part, ToggleModule)>(); toggleOrder.Add(lbl); }
                    toggles[lbl].Add((part, tm));
                }
                if (part.variablesModule != null)
                {
                    foreach (string k in part.variablesModule.stringVariables.GetSaveDictionary().Keys)
                    { if (!rawStr.ContainsKey(k)) { rawStr[k] = new List<Part>(); rawStrOrder.Add(k); } rawStr[k].Add(part); }
                    foreach (string k in part.variablesModule.boolVariables.GetSaveDictionary().Keys)
                    { if (!rawBool.ContainsKey(k)) { rawBool[k] = new List<Part>(); rawBoolOrder.Add(k); } rawBool[k].Add(part); }
                }
            }
            if (boolOrder.Count == 0 && stringOrder.Count == 0 && toggleOrder.Count == 0 && rawStrOrder.Count == 0 && rawBoolOrder.Count == 0) return;
            Transform rows = CreateSection("Controls");
            var shownBool = new HashSet<string>(boolOrder);
            var shownString = new HashSet<string>(stringOrder);
            foreach (string varName in boolOrder)
            {
                string capturedVar = varName;
                var entries = boolDrawer[varName].list;
                var parts = entries.Select(e => e.part).ToList();
                AddToggleRow(rows, boolDrawer[varName].lbl,
                    () => entries.All(e => e.el.boolReference.Value),
                    () => { EditorCore.WithSuppressedEvents(() => PartOps.ToggleBoolVariableMulti(parts, capturedVar)); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); });
            }
            foreach (string varName in stringOrder)
            {
                string capturedVar = varName;
                var entries = stringDrawer[varName].list;
                var parts = entries.Select(e => e.part).ToList();
                AddTextRow(rows, stringDrawer[varName].lbl,
                    () => { string first = entries[0].el.stringReference.Value; return entries.All(e => e.el.stringReference.Value == first) ? first : "…"; },
                    v => { EditorCore.WithSuppressedEvents(() => PartOps.SetStringVariableMulti(parts, capturedVar, v)); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); });
            }
            foreach (string lbl in toggleOrder)
            {
                string capturedLbl = lbl;
                var instances = toggles[lbl];
                AddToggleRow(rows, lbl,
                    () => instances.All(e => GetToggleState(e.tm)),
                    () => { foreach (var (_, tm) in instances) tm.state.Toggle(); Undo.main?.CreateNewStep(capturedLbl); });
            }
            foreach (string key in rawStrOrder)
            {
                if (shownString.Contains(key)) continue;
                string k = key;
                var parts = rawStr[k];
                AddTextRow(rows, FriendlyName(k),
                    () =>
                    {
                        string first = null;
                        foreach (Part p in parts)
                        {
                            var vm = p.variablesModule;
                            if (vm == null) continue;
                            var d = vm.stringVariables.GetSaveDictionary();
                            if (!d.TryGetValue(k, out string v)) continue;
                            if (first == null) { first = v; continue; }
                            if (first != v) return "…";
                        }
                        return first ?? "";
                    },
                    val => { EditorCore.WithSuppressedEvents(() => PartOps.SetStringVariableMulti(parts, k, val)); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); });
            }
            foreach (string key in rawBoolOrder)
            {
                if (shownBool.Contains(key)) continue;
                string k = key;
                var parts = rawBool[k];
                AddToggleRow(rows, FriendlyName(k),
                    () => parts.All(p => p.variablesModule?.boolVariables.Has(k) == true && p.variablesModule.boolVariables.GetValue(k)),
                    () => { EditorCore.WithSuppressedEvents(() => PartOps.ToggleBoolVariableMulti(parts, k)); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); });
            }
        }

        /** <summary>Alias for <see cref="Rebuild"/>; called by <see cref="EditorCore.NotifyModified"/>.</summary> */
        public static void RefreshValues() => Rebuild();

        static void Relayout()
        {
            if (window?.ChildrenHolder is RectTransform rt)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        static Transform CreateSection(string title)
        {
            Box box = Builder.CreateBox(window, WindowW - 10, 10, 0, 0, 0.20f);
            box.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 10, new RectOffset(SecPadH, SecPadH, 8, 8), true);
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label hdr = Builder.CreateLabel(box, RowW, 30, 0, 0, title);
            hdr.FontSize = 16f; hdr.Opacity = 0.9f;
            Container rowsCont = Builder.CreateContainer(box, 0, 0);
            rowsCont.Size = new Vector2(RowW, 10);
            rowsCont.CreateLayoutGroup(SFS.UI.ModGUI.Type.Vertical, TextAnchor.UpperLeft, 1, new RectOffset(0, 0, 0, 0), true);
            rowsCont.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rowsCont.gameObject.transform;
        }

        static void AddSubHeader(Transform parent, string text)
        {
            Container row = Builder.CreateContainer(parent, 0, 0);
            row.Size = new Vector2(RowW, 24);
            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 0, new RectOffset(4, 0, 0, 0), true);
            var lbl = Builder.CreateLabel(row.gameObject.transform, RowW, 24, 0, 0, "— " + text);
            lbl.FontSize = 11f; lbl.Opacity = 0.50f;
        }

        static Container MakeRow(Transform parent)
        {
            Container row = Builder.CreateContainer(parent, 0, 0);
            row.Size = new Vector2(RowW, RowH);
            row.CreateLayoutGroup(SFS.UI.ModGUI.Type.Horizontal, TextAnchor.MiddleLeft, 4, new RectOffset(0, 0, 0, 0), true);
            return row;
        }

        static void AddNumRow(Transform parent, string label, float step, Func<float> getter, Action<float> setter, Action<float> stepAction = null)
        {
            Container row = MakeRow(parent);
            Builder.CreateLabel(row.gameObject.transform, LabelW, RowH, 0, 0, label).FontSize = 12f;
            float initial = getter();
            string pendingText = float.IsNaN(initial) ? "" : FormatNum(initial);
            TextInput ti = Builder.CreateTextInput(row.gameObject.transform, InputW, RowH, 0, 0, pendingText,
                new UnityEngine.Events.UnityAction<string>(txt => pendingText = txt ?? ""));
            var field = ti.gameObject.GetComponentInChildren<InputField>();
            var tmpField = ti.gameObject.GetComponentInChildren<TMP_InputField>();
            if (field != null)
            {
                if (field.placeholder is UnityEngine.UI.Text ph && float.IsNaN(initial)) ph.text = "—";
                field.onEndEdit.AddListener(txt =>
                {
                    if (float.TryParse((txt ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        ApplySetter(setter, val);
                });
            }
            else if (tmpField != null)
            {
                tmpField.onEndEdit.AddListener(txt =>
                {
                    if (float.TryParse((txt ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        ApplySetter(setter, val);
                });
            }
            else
            {
                var sub = ti.gameObject.AddComponent<NumSubmitTracker>();
                sub.getPending = () => pendingText;
                sub.onSubmit = () =>
                {
                    if (float.TryParse(pendingText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        ApplySetter(setter, val);
                };
            }
            if (stepAction != null)
            {
                Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => stepAction(-step), "−");
                Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => stepAction(+step), "+");
            }
            else
            {
                Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0,
                    () => { float cur = getter(); ApplySetter(setter, (float.IsNaN(cur) ? 0f : cur) - step); }, "−");
                Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0,
                    () => { float cur = getter(); ApplySetter(setter, (float.IsNaN(cur) ? 0f : cur) + step); }, "+");
            }
        }

        static void ApplySetter(Action<float> setter, float value) { EditorCore.WithSuppressedEvents(() => setter(value)); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); }

        static void AddBtnRow(Transform parent, string aLabel, string bLabel, Action onA, Action onB)
        {
            int half = (RowW - 4) / 2;
            Container row = MakeRow(parent);
            Builder.CreateButton(row.gameObject.transform, half, RowH, 0, 0, onA, aLabel);
            Builder.CreateButton(row.gameObject.transform, half, RowH, 0, 0, onB, bLabel);
        }

        static void AddTextRow(Transform parent, string label, Func<string> getter, Action<string> setter)
        {
            Container row = MakeRow(parent);
            Builder.CreateLabel(row.gameObject.transform, LabelW, RowH, 0, 0, label).FontSize = 12f;
            int tiW = RowW - LabelW - 4;
            string pending = getter();
            TextInput ti = Builder.CreateTextInput(row.gameObject.transform, tiW, RowH, 0, 0, pending,
                new UnityEngine.Events.UnityAction<string>(txt => pending = txt ?? ""));
            var field = ti.gameObject.GetComponentInChildren<InputField>();
            if (field != null)
                field.onEndEdit.AddListener(txt => ApplyTextSetter(setter, txt ?? ""));
            else
            {
                var sub = ti.gameObject.AddComponent<NumSubmitTracker>();
                sub.getPending = () => pending;
                sub.onSubmit = () => ApplyTextSetter(setter, pending);
            }
        }

        static void ApplyTextSetter(Action<string> setter, string value) { EditorCore.WithSuppressedEvents(() => setter(value)); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); }

        static void AddToggleRow(Transform parent, string label, Func<bool> get, Action toggle)
        {
            Container row = MakeRow(parent);
            Builder.CreateLabel(row.gameObject.transform, LabelW + 60, RowH, 0, 0, label).FontSize = 12f;
            Builder.CreateToggle(row.gameObject.transform, get, 0, 0, () => { EditorCore.WithSuppressedEvents(toggle); HandleSystem.RefreshHandles(); EditorCore.NotifyModified(); });
        }

        static void AddGroupScaleRow(Transform parent)
        {
            Container row = MakeRow(parent);
            Builder.CreateLabel(row.gameObject.transform, LabelW, RowH, 0, 0, "Scale All").FontSize = 12f;
            Builder.CreateTextInput(row.gameObject.transform, InputW, RowH, 0, 0, FormatNum(groupScaleFactor),
                new UnityEngine.Events.UnityAction<string>(txt =>
                {
                    if (float.TryParse((txt ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f > 0.001f)
                        groupScaleFactor = f;
                }));
            Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => ApplyGroupScale(1f / Mathf.Max(0.001f, groupScaleFactor)), "−");
            Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => ApplyGroupScale(groupScaleFactor), "+");
        }

        static void ApplyGroupScale(float factor)
        {
            EditorCore.WithSuppressedEvents(() =>
            {
                foreach (Part p in EditorCore.Selection)
                {
                    Orientation o = p.orientation.orientation.Value;
                    float nx = Mathf.Sign(o.x) * Mathf.Max(0.05f, Mathf.Abs(o.x) * factor);
                    float ny = Mathf.Sign(o.y) * Mathf.Max(0.05f, Mathf.Abs(o.y) * factor);
                    p.orientation.orientation.Value = new Orientation(nx, ny, o.z);
                    p.RegenerateMesh();
                }
            });
            Undo.main?.CreateNewStep("Scale All");
            HandleSystem.RefreshHandles();
            EditorCore.NotifyModified();
        }

        static void AddGroupRotateRow(Transform parent)
        {
            Container row = MakeRow(parent);
            Builder.CreateLabel(row.gameObject.transform, LabelW, RowH, 0, 0, "Rotate All").FontSize = 12f;
            Builder.CreateTextInput(row.gameObject.transform, InputW, RowH, 0, 0, FormatNum(groupRotateDegrees),
                new UnityEngine.Events.UnityAction<string>(txt =>
                {
                    if (float.TryParse((txt ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f >= 0f)
                        groupRotateDegrees = f;
                }));
            Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => ApplyGroupRotate(-groupRotateDegrees), "−");
            Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => ApplyGroupRotate(groupRotateDegrees), "+");
        }

        static void ApplyGroupRotate(float delta)
        {
            EditorCore.WithSuppressedEvents(() =>
            {
                foreach (Part p in EditorCore.Selection)
                {
                    Orientation o = p.orientation.orientation.Value;
                    p.orientation.orientation.Value = new Orientation(o.x, o.y, o.z + delta);
                    p.RegenerateMesh();
                }
            });
            Undo.main?.CreateNewStep("Rotate All");
            HandleSystem.RefreshHandles();
            EditorCore.NotifyModified();
        }

        static void AddRadialRow(Transform parent)
        {
            Container row = MakeRow(parent);
            Builder.CreateLabel(row.gameObject.transform, LabelW, RowH, 0, 0, "Radial R").FontSize = 12f;
            Builder.CreateTextInput(row.gameObject.transform, InputW, RowH, 0, 0, FormatNum(radialRadius),
                new UnityEngine.Events.UnityAction<string>(txt =>
                {
                    if (float.TryParse((txt ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f > 0f)
                        radialRadius = f;
                }));
            Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => PartOps.ArrangeRadially(EditorCore.Selection, radialRadius, false), "○");
            Builder.CreateButton(row.gameObject.transform, BtnW, RowH, 0, 0, () => PartOps.ArrangeRadially(EditorCore.Selection, radialRadius, true), "⊙");
        }

        static string GetRefVarName(object refVar)
        {
            if (refVar == null) return "";
            try
            {
                var t = refVar.GetType();
                while (t != null && t != typeof(object))
                {
                    var f = t.GetField("variableName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
                    if (f != null) return (f.GetValue(refVar) as string) ?? "";
                    t = t.BaseType;
                }
            }
            catch { }
            return "";
        }

        static string SafeLabel(ToggleModule tm) { try { return tm.label?.Field ?? "Toggle"; } catch { return "Toggle"; } }

        static readonly System.Reflection.FieldInfo _targetTimeField =
            typeof(MoveModule).GetField("targetTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        static bool GetToggleState(ToggleModule tm)
        {
            if (tm?.state == null || _targetTimeField == null) return false;
            try
            {
                object tt = _targetTimeField.GetValue(tm.state);
                var valueProp = tt?.GetType().GetProperty("Value");
                return valueProp != null && (float)valueProp.GetValue(tt) > 0f;
            }
            catch { return false; }
        }

        static string PartDisplayName(Part part)
        {
            string n = part?.name ?? "Part";
            int idx = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return idx > 0 ? n.Substring(0, idx).TrimEnd() : n;
        }

        static string FormatNum(float v) =>
            Mathf.Abs(v) >= 1000f ? v.ToString("F0") :
            Mathf.Abs(v) >= 100f ? v.ToString("F1") :
            Mathf.Abs(v) >= 10f ? v.ToString("F2") : v.ToString("F3");

        static List<string> CollectKeys(Func<Part, IEnumerable<string>> selector)
        {
            var seen = new HashSet<string>();
            var order = new List<string>();
            foreach (string k in EditorCore.Selection.SelectMany(p => selector(p) ?? Enumerable.Empty<string>()))
                if (seen.Add(k)) order.Add(k);
            return order;
        }

        static string GetCommonStringVar(string key)
        {
            string first = null;
            foreach (Part p in EditorCore.Selection)
            {
                var d = p.variablesModule?.stringVariables.GetSaveDictionary();
                if (d == null || !d.TryGetValue(key, out string v)) continue;
                if (first == null) { first = v; continue; }
                if (first != v) return "…";
            }
            return first ?? "";
        }

        static string FriendlyName(string raw) =>
            string.Join(" ", raw.Split('_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));

        class NumSubmitTracker : MonoBehaviour
        {
            internal Func<string> getPending;
            internal Action onSubmit;

            void Update()
            {
                if (onSubmit == null) return;
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                    if (IsThisInputFocused()) onSubmit();
            }

            bool IsThisInputFocused()
            {
                GameObject sel = EventSystem.current?.currentSelectedGameObject;
                if (sel == null) return false;
                Transform t = sel.transform;
                while (t != null) { if (t.gameObject == gameObject) return true; t = t.parent; }
                return false;
            }
        }
    }
}
