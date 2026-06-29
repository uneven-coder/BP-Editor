using System;
using System.Collections.Generic;
using System.Linq;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using UnityEngine;

namespace BPEditor
{
    /** <summary>Tracks the current selection and coordinates panel, handle, and undo events.</summary> */
    public static class EditorCore
    {
        /** <summary>Currently selected parts.</summary> */
        public static List<Part> Selection { get; private set; } = new List<Part>();

        public static bool HasSelection => Selection.Count > 0;
        public static bool IsSingle => Selection.Count == 1;
        public static bool IsMulti => Selection.Count > 1;
        public static Part SinglePart => IsSingle ? Selection[0] : null;

        public static event Action SelectionChanged;
        public static event Action OnPartsModified;

        /** <summary>True while a text field has keyboard focus.</summary> */
        public static bool IsTyping { get; private set; }

        public static void SetTyping(bool value)
        {
            if (IsTyping == value) return;
            IsTyping = value;
            EditorPanel.SetTypingIndicator(value);
        }

        static bool suppressEvents;

        /** <summary>Creates and wires up the panel and handle subsystems.</summary> */
        public static void Initialize()
        {
            Selection = new List<Part>();
            EditorPanel.Initialize();
            HandleSystem.Initialize();
        }

        /** <summary>Called when BuildManager's selection changes.</summary> */
        public static void OnSelectionChanged()
        {
            Selection = BuildManager.main.selector.selected.ToList();
            SelectionChanged?.Invoke();
            EditorPanel.Rebuild();
            HandleSystem.RefreshHandles();
        }

        /** <summary>Fires <see cref="OnPartsModified"/> and refreshes UI; no-op when events are suppressed.</summary> */
        public static void NotifyModified()
        {
            if (suppressEvents) return;
            OnPartsModified?.Invoke();
            EditorPanel.RefreshValues();
            HandleSystem.RefreshHandles();
        }

        /** <summary>Runs <paramref name="action"/> without triggering <see cref="NotifyModified"/> on each step.</summary> */
        public static void WithSuppressedEvents(Action action)
        {
            suppressEvents = true;
            try { action(); }
            finally { suppressEvents = false; }
        }

        public static string GetCommonString<T>(Func<Part, T> getter, out bool allSame)
        {
            if (!HasSelection) { allSame = true; return ""; }
            T first = getter(Selection[0]);
            allSame = Selection.All(p => EqualityComparer<T>.Default.Equals(getter(p), first));
            return allSame ? (first?.ToString() ?? "") : "…";
        }

        public static float GetCommonFloat(Func<Part, float> getter, out bool allSame)
        {
            if (!HasSelection) { allSame = true; return 0f; }
            float first = getter(Selection[0]);
            allSame = Selection.All(p => Mathf.Approximately(getter(p), first));
            return allSame ? first : float.NaN;
        }

        public static double GetCommonDouble(Func<Part, double> getter, out bool allSame)
        {
            if (!HasSelection) { allSame = true; return 0.0; }
            double first = getter(Selection[0]);
            allSame = Selection.All(p => Math.Abs(getter(p) - first) < 1e-9);
            return allSame ? first : double.NaN;
        }
    }

    /** <summary>Stateless part mutation helpers; all writes go through Undo.</summary> */
    public static class PartOps
    {
        public static void SetPositionX(Part part, float x) =>
            WithUndo(new[] { part }, "Position X", () => part.Position = new Vector2(x, part.Position.y));

        public static void SetPositionY(Part part, float y) =>
            WithUndo(new[] { part }, "Position Y", () => part.Position = new Vector2(part.Position.x, y));

        public static void SetOrientationX(Part part, float x) =>
            WithUndo(new[] { part }, "Ori X", () =>
            {
                Orientation o = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(x, o.y, o.z);
                part.RegenerateMesh();
            });

        public static void SetOrientationY(Part part, float y) =>
            WithUndo(new[] { part }, "Ori Y", () =>
            {
                Orientation o = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(o.x, y, o.z);
                part.RegenerateMesh();
            });

        public static void SetRotation(Part part, float degrees) =>
            WithUndo(new[] { part }, "Rotation", () =>
            {
                Orientation o = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(o.x, o.y, degrees);
                part.RegenerateMesh();
            });

        public static void AddRotation(Part part, float delta) =>
            SetRotation(part, part.orientation.orientation.Value.z + delta);

        public static void FlipHorizontal(Part part) =>
            WithUndo(new[] { part }, "Flip H", () =>
            {
                Orientation o = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(-o.x, o.y, -o.z);
                part.RegenerateMesh();
            });

        public static void FlipVertical(Part part) =>
            WithUndo(new[] { part }, "Flip V", () =>
            {
                Orientation o = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(o.x, -o.y, -o.z);
                part.RegenerateMesh();
            });

        public static void SetDoubleVariable(Part part, string name, double value) =>
            WithVar(new[] { part }, () => { part.variablesModule.doubleVariables.SetValue(name, value, default); ApplyPartUpdate(part); });

        public static void SetDoubleVariableMulti(IEnumerable<Part> parts, string name, double value)
        {
            Part[] arr = parts.Where(p => p.variablesModule?.doubleVariables.Has(name) == true).ToArray();
            if (arr.Length == 0) return;
            WithVar(arr, () => { foreach (Part p in arr) { p.variablesModule.doubleVariables.SetValue(name, value, default); ApplyPartUpdate(p); } });
        }

        public static void StepDoubleVariableMulti(IEnumerable<Part> parts, string name, double delta)
        {
            Part[] arr = parts.Where(p => p.variablesModule?.doubleVariables.Has(name) == true).ToArray();
            if (arr.Length == 0) return;
            WithVar(arr, () =>
            {
                foreach (Part p in arr)
                {
                    p.variablesModule.doubleVariables.SetValue(name, p.variablesModule.doubleVariables.GetValue(name) + delta, default);
                    ApplyPartUpdate(p);
                }
            });
        }

        public static void SetStringVariable(Part part, string name, string value) =>
            WithVar(new[] { part }, () => { part.variablesModule.stringVariables.SetValue(name, value, default); ApplyPartUpdate(part); });

        public static void SetStringVariableMulti(IEnumerable<Part> parts, string name, string value)
        {
            Part[] arr = parts.Where(p => p.variablesModule?.stringVariables.Has(name) == true).ToArray();
            if (arr.Length == 0) return;
            WithVar(arr, () => { foreach (Part p in arr) { p.variablesModule.stringVariables.SetValue(name, value, default); ApplyPartUpdate(p); } });
        }

        public static void ToggleBoolVariable(Part part, string name) =>
            WithVar(new[] { part }, () =>
            {
                part.variablesModule.boolVariables.SetValue(name, !part.variablesModule.boolVariables.GetValue(name), default);
                ApplyPartUpdate(part);
            });

        public static void ToggleBoolVariableMulti(IEnumerable<Part> parts, string name)
        {
            Part[] arr = parts.Where(p => p.variablesModule?.boolVariables.Has(name) == true).ToArray();
            if (arr.Length == 0) return;
            WithVar(arr, () =>
            {
                foreach (Part p in arr)
                {
                    p.variablesModule.boolVariables.SetValue(name, !p.variablesModule.boolVariables.GetValue(name), default);
                    ApplyPartUpdate(p);
                }
            });
        }

        public static void FlipHorizontalMulti(List<Part> parts)
        {
            if (parts.Count == 0) return;
            WithUndo(parts.ToArray(), "Flip H", () =>
            {
                float cx = parts.Average(p => p.Position.x);
                foreach (Part p in parts)
                {
                    p.Position = new Vector2(2f * cx - p.Position.x, p.Position.y);
                    Orientation o = p.orientation.orientation.Value;
                    p.orientation.orientation.Value = new Orientation(-o.x, o.y, -o.z);
                    p.RegenerateMesh();
                }
            });
        }

        public static void FlipVerticalMulti(List<Part> parts)
        {
            if (parts.Count == 0) return;
            WithUndo(parts.ToArray(), "Flip V", () =>
            {
                float cy = parts.Average(p => p.Position.y);
                foreach (Part p in parts)
                {
                    p.Position = new Vector2(p.Position.x, 2f * cy - p.Position.y);
                    Orientation o = p.orientation.orientation.Value;
                    p.orientation.orientation.Value = new Orientation(o.x, -o.y, -o.z);
                    p.RegenerateMesh();
                }
            });
        }

        public static void RotateMulti(List<Part> parts, float deltaDegrees)
        {
            if (parts.Count == 0) return;
            WithUndo(parts.ToArray(), "Rotate", () =>
            {
                Vector2 pivot = GetCombinedBounds(parts).center;
                float rad = deltaDegrees * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                foreach (Part p in parts)
                {
                    Vector2 off = (Vector2)p.Position - pivot;
                    p.Position = pivot + new Vector2(cos * off.x - sin * off.y, sin * off.x + cos * off.y);
                    Orientation o = p.orientation.orientation.Value;
                    p.orientation.orientation.Value = new Orientation(o.x, o.y, o.z + deltaDegrees);
                    p.RegenerateMesh();
                }
            });
        }

        public static void ScaleMultiOri(List<Part> parts, float factor)
        {
            if (parts.Count == 0) return;
            WithUndo(parts.ToArray(), "Scale", () =>
            {
                foreach (Part p in parts)
                {
                    Orientation o = p.orientation.orientation.Value;
                    p.orientation.orientation.Value = new Orientation(ScaleOri(o.x, factor), ScaleOri(o.y, factor), o.z);
                    p.RegenerateMesh();
                }
            });
        }

        public static Bounds GetBounds(Part part)
        {
            Bounds b = new Bounds((Vector3)part.Position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in part.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        public static Bounds GetCombinedBounds(IEnumerable<Part> parts)
        {
            Bounds combined = default;
            bool first = true;
            foreach (Part p in parts)
            {
                Bounds b = GetBounds(p);
                if (first) { combined = b; first = false; }
                else combined.Encapsulate(b);
            }
            return combined;
        }

        public static void AlignLeft(List<Part> parts)
        {
            if (parts.Count < 2) return;
            float target = parts.Min(p => GetBounds(p).min.x);
            WithUndo(parts.ToArray(), "Align Left", () =>
            {
                foreach (Part p in parts) p.Position = new Vector2(p.Position.x + (target - GetBounds(p).min.x), p.Position.y);
            });
        }

        public static void AlignRight(List<Part> parts)
        {
            if (parts.Count < 2) return;
            float target = parts.Max(p => GetBounds(p).max.x);
            WithUndo(parts.ToArray(), "Align Right", () =>
            {
                foreach (Part p in parts) p.Position = new Vector2(p.Position.x + (target - GetBounds(p).max.x), p.Position.y);
            });
        }

        public static void AlignTop(List<Part> parts)
        {
            if (parts.Count < 2) return;
            float target = parts.Max(p => GetBounds(p).max.y);
            WithUndo(parts.ToArray(), "Align Top", () =>
            {
                foreach (Part p in parts) p.Position = new Vector2(p.Position.x, p.Position.y + (target - GetBounds(p).max.y));
            });
        }

        public static void AlignBottom(List<Part> parts)
        {
            if (parts.Count < 2) return;
            float target = parts.Min(p => GetBounds(p).min.y);
            WithUndo(parts.ToArray(), "Align Bottom", () =>
            {
                foreach (Part p in parts) p.Position = new Vector2(p.Position.x, p.Position.y + (target - GetBounds(p).min.y));
            });
        }

        public static void AlignCentreH(List<Part> parts)
        {
            if (parts.Count < 2) return;
            float target = parts.Average(p => GetBounds(p).center.x);
            WithUndo(parts.ToArray(), "Centre H", () =>
            {
                foreach (Part p in parts) p.Position = new Vector2(p.Position.x + (target - GetBounds(p).center.x), p.Position.y);
            });
        }

        public static void AlignCentreV(List<Part> parts)
        {
            if (parts.Count < 2) return;
            float target = parts.Average(p => GetBounds(p).center.y);
            WithUndo(parts.ToArray(), "Centre V", () =>
            {
                foreach (Part p in parts) p.Position = new Vector2(p.Position.x, p.Position.y + (target - GetBounds(p).center.y));
            });
        }

        public static void DistributeH(List<Part> parts)
        {
            if (parts.Count < 3) return;
            var sorted = parts.OrderBy(p => GetBounds(p).center.x).ToList();
            var bounds = sorted.Select(GetBounds).ToList();
            float span = bounds[sorted.Count - 1].max.x - bounds[0].min.x;
            float totalW = bounds.Sum(b => b.size.x);
            float gap = (span - totalW) / (sorted.Count - 1);
            WithUndo(parts.ToArray(), "Distribute H", () =>
            {
                float x = bounds[0].min.x;
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].Position = new Vector2(sorted[i].Position.x + (x - bounds[i].min.x), sorted[i].Position.y);
                    x += bounds[i].size.x + gap;
                }
            });
        }

        public static void DistributeV(List<Part> parts)
        {
            if (parts.Count < 3) return;
            var sorted = parts.OrderBy(p => GetBounds(p).center.y).ToList();
            var bounds = sorted.Select(GetBounds).ToList();
            float span = bounds[sorted.Count - 1].max.y - bounds[0].min.y;
            float totalH = bounds.Sum(b => b.size.y);
            float gap = (span - totalH) / (sorted.Count - 1);
            WithUndo(parts.ToArray(), "Distribute V", () =>
            {
                float y = bounds[0].min.y;
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].Position = new Vector2(sorted[i].Position.x, sorted[i].Position.y + (y - bounds[i].min.y));
                    y += bounds[i].size.y + gap;
                }
            });
        }

        /** <summary>Places <paramref name="parts"/> evenly on a circle of <paramref name="radius"/> around their collective centre.</summary> */
        public static void ArrangeRadially(List<Part> parts, float radius, bool faceOutward)
        {
            if (parts.Count < 2) return;
            Vector2 center = GetCombinedBounds(parts).center;
            float step = 360f / parts.Count;
            WithUndo(parts.ToArray(), "Radial Arrange", () =>
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    float angleRad = i * step * Mathf.Deg2Rad;
                    parts[i].Position = center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;
                    if (faceOutward)
                    {
                        Orientation o = parts[i].orientation.orientation.Value;
                        parts[i].orientation.orientation.Value = new Orientation(o.x, o.y, i * step - 90f);
                        parts[i].RegenerateMesh();
                    }
                }
            });
        }

        static void ApplyPartUpdate(Part part) { AdaptModule.UpdateAdaptation(new[] { part }); part.RegenerateMesh(); }

        static void WithVar(Part[] parts, Action action)
        {
            if (Undo.main != null) Undo.main.RecordStatChangeStep(parts, action);
            else action();
        }

        static void WithUndo(Part[] parts, string stepName, Action action)
        {
            if (Undo.main != null) Undo.main.RecordStatChangeStep(parts, action);
            else action();
            EditorCore.NotifyModified();
        }

        static float ScaleOri(float val, float factor) => Mathf.Sign(val) * Mathf.Max(0.05f, Mathf.Abs(val) * factor);
    }
}
