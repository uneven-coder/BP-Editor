using System;
using System.Collections.Generic;
using System.Linq;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BPEditor
{
    /** <summary>Renders and drives all interactive handles in the build scene.</summary> */
    public class HandleSystem : MonoBehaviour, I_GLDrawer
    {
        /** <summary>Singleton instance created on <see cref="Initialize"/>.</summary> */
        public static HandleSystem instance;
        /** <summary>True while a drag or action tap owns input; suppresses BuildManager.</summary> */
        public static bool IsCapturing;

        enum HandleType
        {
            None,
            CornerTL, CornerTR, CornerBL, CornerBR,
            Rotate,
            Height,
            WidthA, Width, WidthB,
            ActionFlipH, ActionFlipV, ActionLockAspect, ActionLockBounds,
            EdgeBottom, EdgeLeft, EdgeRight
        }

        struct Handle { public HandleType type; public Vector2 worldPos; public bool visible; }

        const float HandleHalf = 0.10f;
        const float HitRadius = 0.18f;
        const float RotateOffset = 0.42f;
        const float HeightOffset = 0.08f;
        const float WidthSideOff = 0.28f;
        const float LineW = 0.025f;
        const float MinRotDist = 0.10f;
        const int RotCircleSegs = 36;

        static readonly Color ColCorner = new(1.00f, 1.00f, 1.00f, 0.95f);
        static readonly Color ColHeight = new(0.55f, 0.80f, 1.00f, 0.95f);
        static readonly Color ColWidth = new(1.00f, 0.75f, 0.45f, 0.95f);
        static readonly Color ColRotate = new(0.55f, 1.00f, 0.55f, 0.95f);
        static readonly Color ColFlipH = new(0.60f, 0.82f, 1.00f, 0.92f);
        static readonly Color ColFlipV = new(0.80f, 0.65f, 1.00f, 0.92f);
        static readonly Color ColLockOn = new(0.40f, 1.00f, 0.65f, 0.97f);
        static readonly Color ColLockOff = new(0.65f, 0.65f, 0.72f, 0.88f);
        static readonly Color ColEdge = new(0.75f, 0.75f, 1.00f, 0.85f);

        List<Handle> handles = new();
        Bounds selBounds;
        bool boundsValid;
        bool isDragging;
        bool actionPending;
        HandleType dragHandle;
        Vector2 snapHandlePos;
        Vector2 snapCenter;
        Vector2 snapPartPos;
        bool snapLockBounds;
        float snapOriX, snapOriY, snapOriZ;
        float snapVarValue;
        List<(Part p, Vector2 pos, float oriX, float oriY, float oriZ)> snapMulti;

        /** <summary>Creates the singleton and registers it with GLDrawer.</summary> */
        public static void Initialize()
        {
            if (instance != null) return;
            instance = new GameObject("BPEditor_Handles").AddComponent<HandleSystem>();
            GLDrawer.Register(instance);
        }

        void OnDestroy() { GLDrawer.Unregister(this); instance = null; }

        /** <summary>Rebuilds all handle positions from the current selection.</summary> */
        public static void RefreshHandles() => instance?.RebuildHandles();

        void RebuildHandles()
        {
            handles.Clear();
            boundsValid = false;
            if (!EditorCore.HasSelection) return;
            selBounds = PartOps.GetCombinedBounds(EditorCore.Selection);
            boundsValid = true;
            PopulateHandles();
        }

        static (float hw, float hh, Vector2 vc) GetLocalExtents(Part p)
        {
            Bounds b = PartOps.GetBounds(p);
            Vector2 vc = new(b.center.x, b.center.y);
            float θ = p.orientation.orientation.Value.z * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(θ));
            float s = Mathf.Abs(Mathf.Sin(θ));
            float det = c * c - s * s;
            if (Mathf.Abs(det) > 0.01f)
                return (Mathf.Max(0.001f, (c * b.extents.x - s * b.extents.y) / det),
                        Mathf.Max(0.001f, (c * b.extents.y - s * b.extents.x) / det), vc);
            Orientation o = p.orientation.orientation.Value;
            float absX = Mathf.Abs(o.x), absY = Mathf.Abs(o.y);
            if (absX < 0.0001f || absY < 0.0001f) return (b.extents.x, b.extents.y, vc);
            float aspect = absY / absX;
            float denom = c + aspect * s;
            float hw = Mathf.Approximately(denom, 0f) ? b.extents.x : b.extents.x / denom;
            return (hw, hw * aspect, vc);
        }

        void PopulateHandles()
        {
            handles.Clear();
            if (EditorCore.IsSingle && !Config.S.LockBounds)
            {
                Part p = EditorCore.SinglePart;
                var (hw, hh, vc) = GetLocalExtents(p);
                float θ = p.orientation.orientation.Value.z;
                AddHandle(HandleType.CornerTL, LW(-hw, hh, vc, θ));
                AddHandle(HandleType.CornerTR, LW(hw, hh, vc, θ));
                AddHandle(HandleType.CornerBL, LW(-hw, -hh, vc, θ));
                AddHandle(HandleType.CornerBR, LW(hw, -hh, vc, θ));
                AddHandle(HandleType.Rotate, LW(0, hh + RotateOffset, vc, θ));
                AddHandle(HandleType.ActionFlipH, LW(-(hw + WidthSideOff), 0.30f, vc, θ));
                AddHandle(HandleType.ActionFlipV, LW(-(hw + WidthSideOff), 0f, vc, θ));
                AddHandle(HandleType.ActionLockAspect, LW(-(hw + WidthSideOff), -0.30f, vc, θ));
                AddHandle(HandleType.ActionLockBounds, LW(-(hw + WidthSideOff), -0.60f, vc, θ));
                bool hasH = HasVar("height", "h");
                bool hasWA = HasVar("width_top", "width_a", "width_original_a");
                bool hasW = HasVar("width_original", "width", "w", "radius");
                bool hasWB = HasVar("width_bottom", "width_b", "width_original_b");
                AddHandle(HandleType.Height, LW(0, hh + HeightOffset, vc, θ), hasH);
                AddHandle(HandleType.WidthA, LW(hw + WidthSideOff, hh * 0.50f, vc, θ), hasWA);
                AddHandle(HandleType.Width, LW(hw + WidthSideOff, 0f, vc, θ), hasW);
                AddHandle(HandleType.WidthB, LW(hw + WidthSideOff, -hh * 0.50f, vc, θ), hasWB);
                AddHandle(HandleType.EdgeBottom, LW(0f, -hh, vc, θ));
                AddHandle(HandleType.EdgeLeft, LW(-hw, 0f, vc, θ));
                AddHandle(HandleType.EdgeRight, LW(hw, 0f, vc, θ));
            }
            else
            {
                float cx = selBounds.center.x, cy = selBounds.center.y;
                float top = selBounds.max.y, bot = selBounds.min.y;
                float left = selBounds.min.x, right = selBounds.max.x;
                float lSide = left - WidthSideOff;
                AddHandle(HandleType.CornerTL, new Vector2(left, top));
                AddHandle(HandleType.CornerTR, new Vector2(right, top));
                AddHandle(HandleType.CornerBL, new Vector2(left, bot));
                AddHandle(HandleType.CornerBR, new Vector2(right, bot));
                AddHandle(HandleType.Rotate, new Vector2(cx, top + RotateOffset));
                AddHandle(HandleType.ActionFlipH, new Vector2(lSide, cy + 0.30f));
                AddHandle(HandleType.ActionFlipV, new Vector2(lSide, cy));
                AddHandle(HandleType.ActionLockAspect, new Vector2(lSide, cy - 0.30f));
                AddHandle(HandleType.ActionLockBounds, new Vector2(lSide, cy - 0.60f));
                if (EditorCore.IsSingle)
                {
                    Part p = EditorCore.SinglePart;
                    var (hw, hh, vc) = GetLocalExtents(p);
                    float θ = p.orientation.orientation.Value.z;
                    bool hasH = HasVar("height", "h");
                    bool hasWA = HasVar("width_top", "width_a", "width_original_a");
                    bool hasW = HasVar("width_original", "width", "w", "radius");
                    bool hasWB = HasVar("width_bottom", "width_b", "width_original_b");
                    AddHandle(HandleType.Height, LW(0, hh + HeightOffset, vc, θ), hasH);
                    AddHandle(HandleType.WidthA, LW(hw + WidthSideOff, hh * 0.50f, vc, θ), hasWA);
                    AddHandle(HandleType.Width, LW(hw + WidthSideOff, 0f, vc, θ), hasW);
                    AddHandle(HandleType.WidthB, LW(hw + WidthSideOff, -hh * 0.50f, vc, θ), hasWB);
                }
                AddHandle(HandleType.EdgeBottom, new Vector2(cx, bot));
                AddHandle(HandleType.EdgeLeft, new Vector2(left, cy));
                AddHandle(HandleType.EdgeRight, new Vector2(right, cy));
            }
        }

        void AddHandle(HandleType t, Vector2 pos, bool vis = true)
            => handles.Add(new Handle { type = t, worldPos = pos, visible = vis });

        bool HasVar(params string[] names)
        {
            if (EditorCore.SinglePart?.variablesModule == null) return false;
            var keys = EditorCore.SinglePart.variablesModule.doubleVariables.GetSaveDictionary().Keys;
            return names.Any(n => keys.Any(k => k.Equals(n, StringComparison.OrdinalIgnoreCase)));
        }

        /** <summary>Hit-tests handles; fires action handles immediately or begins a drag. Returns true if consumed.</summary> */
        public bool TryBeginDragExternal(Vector2 clickMouse)
        {
            if (!boundsValid || !EditorCore.HasSelection) return false;
            Handle? best = null;
            float minD = HitRadius;
            foreach (Handle h in handles)
            {
                if (!h.visible) continue;
                float d = Vector2.Distance(clickMouse, h.worldPos);
                if (d < minD) { minD = d; best = h; }
            }
            if (best == null) return false;
            switch (best.Value.type)
            {
                case HandleType.ActionFlipH:
                    if (EditorCore.IsSingle) PartOps.FlipHorizontal(EditorCore.SinglePart);
                    else PartOps.FlipHorizontalMulti(EditorCore.Selection);
                    EditorCore.NotifyModified();
                    MsgDrawer.main?.Log("Flipped Horizontal");
                    ConsumeAction(); return true;
                case HandleType.ActionFlipV:
                    if (EditorCore.IsSingle) PartOps.FlipVertical(EditorCore.SinglePart);
                    else PartOps.FlipVerticalMulti(EditorCore.Selection);
                    EditorCore.NotifyModified();
                    MsgDrawer.main?.Log("Flipped Vertical");
                    ConsumeAction(); return true;
                case HandleType.ActionLockAspect:
                    Config.S.LockAspect = !Config.S.LockAspect;
                    MsgDrawer.main?.Log(Config.S.LockAspect ? "Lock Aspect Enabled" : "Lock Aspect Disabled");
                    ConsumeAction(); return true;
                case HandleType.ActionLockBounds:
                    Config.S.LockBounds = !Config.S.LockBounds;
                    MsgDrawer.main?.Log(Config.S.LockBounds ? "Lock Bounds Enabled" : "Lock Bounds Disabled");
                    RebuildHandles();
                    ConsumeAction(); return true;
            }
            BeginDrag(best.Value);
            return true;
        }

        void BeginDrag(Handle handle)
        {
            isDragging = true;
            IsCapturing = true;
            dragHandle = handle.type;
            snapHandlePos = handle.worldPos;
            snapLockBounds = Config.S.LockBounds;
            bool isCorner = dragHandle == HandleType.CornerTL || dragHandle == HandleType.CornerTR ||
                            dragHandle == HandleType.CornerBL || dragHandle == HandleType.CornerBR;
            bool isEdge = dragHandle == HandleType.EdgeBottom ||
                          dragHandle == HandleType.EdgeLeft || dragHandle == HandleType.EdgeRight;
            if (EditorCore.IsSingle)
            {
                Part p = EditorCore.SinglePart;
                Orientation o = p.orientation.orientation.Value;
                snapOriX = o.x; snapOriY = o.y; snapOriZ = o.z;
                snapPartPos = p.Position;
                var (hw, hh, vc) = GetLocalExtents(p);
                snapVarValue = GetVarValue(p, PrimaryVarNames(handle.type));
                if (Mathf.Approximately(snapVarValue, 0f) && !isCorner && !isEdge && handle.type != HandleType.Rotate)
                    snapVarValue = handle.type == HandleType.Height ? hh * 2f : hw;
                if (isCorner)
                {
                    Vector2 localOpp = dragHandle switch
                    {
                        HandleType.CornerTR => new Vector2(-hw, -hh),
                        HandleType.CornerTL => new Vector2(hw, -hh),
                        HandleType.CornerBR => new Vector2(-hw, hh),
                        HandleType.CornerBL => new Vector2(hw, hh),
                        _ => Vector2.zero
                    };
                    snapCenter = snapLockBounds ? OppositeCorner(dragHandle, selBounds) : LW(localOpp.x, localOpp.y, vc, snapOriZ);
                }
                else if (isEdge)
                {
                    Vector2 localOpp = dragHandle switch
                    {
                        HandleType.EdgeRight => new Vector2(-hw, 0f),
                        HandleType.EdgeLeft => new Vector2(hw, 0f),
                        HandleType.EdgeBottom => new Vector2(0f, hh),
                        _ => Vector2.zero
                    };
                    snapCenter = snapLockBounds ? OppositeEdge(dragHandle, selBounds) : LW(localOpp.x, localOpp.y, vc, snapOriZ);
                }
                else
                {
                    snapCenter = snapPartPos;
                }
            }
            else
            {
                snapCenter = isCorner ? OppositeCorner(dragHandle, selBounds)
                           : isEdge ? OppositeEdge(dragHandle, selBounds)
                           : selBounds.center;
                snapMulti = EditorCore.Selection.Select(p =>
                {
                    Orientation o = p.orientation.orientation.Value;
                    return (p, p.Position, o.x, o.y, o.z);
                }).ToList();
            }
            Undo.main?.RecordStatChangeStep(EditorCore.Selection.ToArray(), () => { });
        }

        static Vector2 OppositeCorner(HandleType t, Bounds b) => t switch
        {
            HandleType.CornerTR => new Vector2(b.min.x, b.min.y),
            HandleType.CornerTL => new Vector2(b.max.x, b.min.y),
            HandleType.CornerBR => new Vector2(b.min.x, b.max.y),
            HandleType.CornerBL => new Vector2(b.max.x, b.max.y),
            _ => b.center
        };

        static Vector2 OppositeEdge(HandleType t, Bounds b) => t switch
        {
            HandleType.EdgeRight => new Vector2(b.min.x, b.center.y),
            HandleType.EdgeLeft => new Vector2(b.max.x, b.center.y),
            HandleType.EdgeBottom => new Vector2(b.center.x, b.max.y),
            _ => b.center
        };

        void ConsumeAction() { IsCapturing = true; actionPending = true; }

        void Update()
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            EditorCore.SetTyping(sel != null &&
                (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<InputField>() != null));
            if (!EditorCore.IsTyping)
            {
                Config.Data s = Config.S;
                if (s.CtrlIsToggle && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl)))
                    Config.CtrlToggled = !Config.CtrlToggled;
                if (s.AltIsToggle && (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt)))
                    Config.AltToggled = !Config.AltToggled;
            }
            if (!isDragging)
            {
                if (boundsValid && EditorCore.HasSelection) { RefreshBoundsOnly(); UpdateHandlePositions(); }
                return;
            }
            Vector2 mouse = Camera.main != null
                ? (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition)
                : snapHandlePos;
            if (Input.GetMouseButton(0)) UpdateDrag(mouse);
            else EndDrag();
        }

        void UpdateDrag(Vector2 mouse)
        {
            float sens = Config.GetSensitivity();
            switch (dragHandle)
            {
                case HandleType.CornerTL:
                case HandleType.CornerTR:
                case HandleType.CornerBL:
                case HandleType.CornerBR: ApplyCornerDrag(mouse, sens); break;
                case HandleType.Rotate: ApplyRotationDrag(mouse, sens); break;
                case HandleType.Height:
                    ApplyVarDelta(ProjLocalY(mouse - snapHandlePos), sens, double.MinValue, "height", "h"); break;
                case HandleType.WidthA:
                    ApplyVarDelta(ProjLocalX(mouse - snapHandlePos), sens, 0.01, "width_top", "width_a", "width_original_a"); break;
                case HandleType.Width:
                    ApplyVarDelta(ProjLocalX(mouse - snapHandlePos), sens, 0.01, "width_original", "width", "w", "radius"); break;
                case HandleType.WidthB:
                    ApplyVarDelta(ProjLocalX(mouse - snapHandlePos), sens, 0.01, "width_bottom", "width_b", "width_original_b"); break;
                case HandleType.EdgeRight:
                case HandleType.EdgeLeft: ApplyEdgeDrag(mouse, sens, scaleX: true, scaleY: false); break;
                case HandleType.EdgeBottom: ApplyEdgeDrag(mouse, sens, scaleX: false, scaleY: true); break;
            }
            RefreshBoundsOnly();
            UpdateHandlePositions();
        }

        float ProjLocalX(Vector2 v) { float c = Mathf.Cos(snapOriZ * Mathf.Deg2Rad), s = Mathf.Sin(snapOriZ * Mathf.Deg2Rad); return c * v.x + s * v.y; }
        float ProjLocalY(Vector2 v) { float c = Mathf.Cos(snapOriZ * Mathf.Deg2Rad), s = Mathf.Sin(snapOriZ * Mathf.Deg2Rad); return -s * v.x + c * v.y; }

        void EndDrag()
        {
            isDragging = false;
            IsCapturing = false;
            Undo.main?.CreateNewStep("BPEditor commit");
            RebuildHandles();
            EditorCore.NotifyModified();
        }

        void LateUpdate()
        {
            if (actionPending && !Input.GetMouseButton(0))
            {
                actionPending = false;
                IsCapturing = false;
                RebuildHandles();
            }
        }

        void RefreshBoundsOnly()
        {
            if (!EditorCore.HasSelection) return;
            selBounds = PartOps.GetCombinedBounds(EditorCore.Selection);
            boundsValid = true;
        }

        void UpdateHandlePositions()
        {
            if (!boundsValid || handles.Count == 0) return;
            if (EditorCore.IsSingle && !Config.S.LockBounds)
            {
                Part p = EditorCore.SinglePart;
                if (p == null) return;
                var (hw, hh, vc) = GetLocalExtents(p);
                float θ = p.orientation.orientation.Value.z;
                for (int i = 0; i < handles.Count; i++)
                {
                    Handle hnd = handles[i];
                    switch (hnd.type)
                    {
                        case HandleType.CornerTL: hnd.worldPos = LW(-hw, hh, vc, θ); break;
                        case HandleType.CornerTR: hnd.worldPos = LW(hw, hh, vc, θ); break;
                        case HandleType.CornerBL: hnd.worldPos = LW(-hw, -hh, vc, θ); break;
                        case HandleType.CornerBR: hnd.worldPos = LW(hw, -hh, vc, θ); break;
                        case HandleType.Rotate: hnd.worldPos = LW(0, hh + RotateOffset, vc, θ); break;
                        case HandleType.Height: hnd.worldPos = LW(0, hh + HeightOffset, vc, θ); break;
                        case HandleType.WidthA: hnd.worldPos = LW(hw + WidthSideOff, hh * 0.50f, vc, θ); break;
                        case HandleType.Width: hnd.worldPos = LW(hw + WidthSideOff, 0f, vc, θ); break;
                        case HandleType.WidthB: hnd.worldPos = LW(hw + WidthSideOff, -hh * 0.50f, vc, θ); break;
                        case HandleType.ActionFlipH: hnd.worldPos = LW(-(hw + WidthSideOff), 0.30f, vc, θ); break;
                        case HandleType.ActionFlipV: hnd.worldPos = LW(-(hw + WidthSideOff), 0f, vc, θ); break;
                        case HandleType.ActionLockAspect: hnd.worldPos = LW(-(hw + WidthSideOff), -0.30f, vc, θ); break;
                        case HandleType.ActionLockBounds: hnd.worldPos = LW(-(hw + WidthSideOff), -0.60f, vc, θ); break;
                        case HandleType.EdgeBottom: hnd.worldPos = LW(0f, -hh, vc, θ); break;
                        case HandleType.EdgeLeft: hnd.worldPos = LW(-hw, 0f, vc, θ); break;
                        case HandleType.EdgeRight: hnd.worldPos = LW(hw, 0f, vc, θ); break;
                    }
                    handles[i] = hnd;
                }
            }
            else
            {
                float cx = selBounds.center.x, cy = selBounds.center.y;
                float top = selBounds.max.y, bot = selBounds.min.y;
                float left = selBounds.min.x, right = selBounds.max.x;
                float lSide = left - WidthSideOff;
                float hw2 = 0, hh2 = 0;
                Vector2 vc2 = default;
                float θ2 = 0;
                if (EditorCore.IsSingle)
                {
                    Part p2 = EditorCore.SinglePart;
                    if (p2 != null) { (hw2, hh2, vc2) = GetLocalExtents(p2); θ2 = p2.orientation.orientation.Value.z; }
                }
                for (int i = 0; i < handles.Count; i++)
                {
                    Handle hnd = handles[i];
                    switch (hnd.type)
                    {
                        case HandleType.CornerTL: hnd.worldPos = new Vector2(left, top); break;
                        case HandleType.CornerTR: hnd.worldPos = new Vector2(right, top); break;
                        case HandleType.CornerBL: hnd.worldPos = new Vector2(left, bot); break;
                        case HandleType.CornerBR: hnd.worldPos = new Vector2(right, bot); break;
                        case HandleType.Rotate: hnd.worldPos = new Vector2(cx, top + RotateOffset); break;
                        case HandleType.ActionFlipH: hnd.worldPos = new Vector2(lSide, cy + 0.30f); break;
                        case HandleType.ActionFlipV: hnd.worldPos = new Vector2(lSide, cy); break;
                        case HandleType.ActionLockAspect: hnd.worldPos = new Vector2(lSide, cy - 0.30f); break;
                        case HandleType.ActionLockBounds: hnd.worldPos = new Vector2(lSide, cy - 0.60f); break;
                        case HandleType.Height: hnd.worldPos = LW(0, hh2 + HeightOffset, vc2, θ2); break;
                        case HandleType.WidthA: hnd.worldPos = LW(hw2 + WidthSideOff, hh2 * 0.50f, vc2, θ2); break;
                        case HandleType.Width: hnd.worldPos = LW(hw2 + WidthSideOff, 0f, vc2, θ2); break;
                        case HandleType.WidthB: hnd.worldPos = LW(hw2 + WidthSideOff, -hh2 * 0.50f, vc2, θ2); break;
                        case HandleType.EdgeBottom: hnd.worldPos = new Vector2(cx, bot); break;
                        case HandleType.EdgeLeft: hnd.worldPos = new Vector2(left, cy); break;
                        case HandleType.EdgeRight: hnd.worldPos = new Vector2(right, cy); break;
                    }
                    handles[i] = hnd;
                }
            }
        }

        void ApplyCornerDrag(Vector2 mouse, float sens)
        {
            float rawRX, rawRY;
            if (EditorCore.IsSingle && !snapLockBounds)
            {
                float cosθ = Mathf.Cos(snapOriZ * Mathf.Deg2Rad), sinθ = Mathf.Sin(snapOriZ * Mathf.Deg2Rad);
                Vector2 lX = new(cosθ, sinθ), lY = new(-sinθ, cosθ);
                Vector2 hV = snapHandlePos - snapCenter, mV = mouse - snapCenter;
                float hLX = Vector2.Dot(hV, lX), hLY = Vector2.Dot(hV, lY);
                rawRX = Mathf.Approximately(hLX, 0f) ? 1f : Vector2.Dot(mV, lX) / hLX;
                rawRY = Mathf.Approximately(hLY, 0f) ? 1f : Vector2.Dot(mV, lY) / hLY;
            }
            else
            {
                float dX = snapHandlePos.x - snapCenter.x, dY = snapHandlePos.y - snapCenter.y;
                rawRX = Mathf.Approximately(dX, 0f) ? 1f : (mouse.x - snapCenter.x) / dX;
                rawRY = Mathf.Approximately(dY, 0f) ? 1f : (mouse.y - snapCenter.y) / dY;
            }
            float rx = 1f + (rawRX - 1f) * sens, ry = 1f + (rawRY - 1f) * sens;
            if (Config.S.LockAspect)
            {
                if (Mathf.Abs(rx - 1f) >= Mathf.Abs(ry - 1f)) ry = rx;
                else rx = ry;
            }
            ApplyScaleDrag(rx, ry);
        }

        void ApplyEdgeDrag(Vector2 mouse, float sens, bool scaleX, bool scaleY)
        {
            float rawR;
            if (EditorCore.IsSingle && !snapLockBounds)
            {
                float cosθ = Mathf.Cos(snapOriZ * Mathf.Deg2Rad), sinθ = Mathf.Sin(snapOriZ * Mathf.Deg2Rad);
                Vector2 axis = scaleX ? new Vector2(cosθ, sinθ) : new Vector2(-sinθ, cosθ);
                float hDot = Vector2.Dot(snapHandlePos - snapCenter, axis);
                rawR = Mathf.Approximately(hDot, 0f) ? 1f : Vector2.Dot(mouse - snapCenter, axis) / hDot;
            }
            else
            {
                float dX = snapHandlePos.x - snapCenter.x, dY = snapHandlePos.y - snapCenter.y;
                rawR = scaleX
                    ? (Mathf.Approximately(dX, 0f) ? 1f : (mouse.x - snapCenter.x) / dX)
                    : (Mathf.Approximately(dY, 0f) ? 1f : (mouse.y - snapCenter.y) / dY);
            }
            float r = 1f + (rawR - 1f) * sens;
            ApplyScaleDrag(scaleX ? r : 1f, scaleY ? r : 1f);
        }

        void ApplyScaleDrag(float rx, float ry)
        {
            const float eps = 0.005f;
            if (EditorCore.IsSingle)
            {
                Part part = EditorCore.SinglePart;
                if (part == null) return;
                float newX = snapOriX * rx, newY = snapOriY * ry;
                if (newX > -eps && newX < eps) newX = snapOriX >= 0 ? eps : -eps;
                if (newY > -eps && newY < eps) newY = snapOriY >= 0 ? eps : -eps;
                part.orientation.orientation.Value = new Orientation(newX, newY, snapOriZ);
                if (!snapLockBounds)
                {
                    float cosθ = Mathf.Cos(snapOriZ * Mathf.Deg2Rad), sinθ = Mathf.Sin(snapOriZ * Mathf.Deg2Rad);
                    Vector2 d = snapCenter - snapPartPos;
                    float lx = cosθ * d.x + sinθ * d.y, ly = -sinθ * d.x + cosθ * d.y;
                    part.Position = snapCenter - new Vector2(cosθ * (lx * rx) - sinθ * (ly * ry), sinθ * (lx * rx) + cosθ * (ly * ry));
                }
                else
                {
                    part.Position = snapCenter + new Vector2((snapPartPos.x - snapCenter.x) * rx, (snapPartPos.y - snapCenter.y) * ry);
                }
                part.RegenerateMesh();
            }
            else if (snapMulti != null)
            {
                foreach (var snap in snapMulti)
                {
                    float newX = snap.oriX * rx, newY = snap.oriY * ry;
                    if (newX > -eps && newX < eps) newX = snap.oriX >= 0 ? eps : -eps;
                    if (newY > -eps && newY < eps) newY = snap.oriY >= 0 ? eps : -eps;
                    snap.p.orientation.orientation.Value = new Orientation(newX, newY, snap.oriZ);
                    snap.p.Position = snapCenter + new Vector2((snap.pos.x - snapCenter.x) * rx, (snap.pos.y - snapCenter.y) * ry);
                    snap.p.RegenerateMesh();
                }
            }
        }

        void ApplyRotationDrag(Vector2 mouse, float sens)
        {
            if (Vector2.Distance(mouse, snapCenter) < MinRotDist) return;
            float startAng = Mathf.Atan2(snapHandlePos.y - snapCenter.y, snapHandlePos.x - snapCenter.x) * Mathf.Rad2Deg;
            float curAng = Mathf.Atan2(mouse.y - snapCenter.y, mouse.x - snapCenter.x) * Mathf.Rad2Deg;
            float totalDelta = Mathf.DeltaAngle(startAng, curAng) * sens;
            if (EditorCore.IsSingle)
            {
                Part part = EditorCore.SinglePart;
                if (part == null) return;
                Orientation cur = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(cur.x, cur.y, snapOriZ + totalDelta);
                part.RegenerateMesh();
            }
            else if (snapMulti != null)
            {
                float rad = totalDelta * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                foreach (var snap in snapMulti)
                {
                    Vector2 off = snap.pos - snapCenter;
                    snap.p.Position = snapCenter + new Vector2(cos * off.x - sin * off.y, sin * off.x + cos * off.y);
                    snap.p.orientation.orientation.Value = new Orientation(snap.oriX, snap.oriY, snap.oriZ + totalDelta);
                    snap.p.RegenerateMesh();
                }
            }
        }

        void ApplyVarDelta(float delta, float sens, double minVal, params string[] names)
        {
            Part part = EditorCore.SinglePart;
            if (part == null) return;
            SetVarValue(part, System.Math.Max(minVal, (double)snapVarValue + delta * sens), names);
        }

        static void SetVarValue(Part part, double value, string[] names)
        {
            if (part?.variablesModule == null) return;
            var dict = part.variablesModule.doubleVariables.GetSaveDictionary();
            foreach (string name in names)
            {
                string key = FindKey(dict, name);
                if (key == null) continue;
                part.variablesModule.doubleVariables.SetValue(key, value, new System.ValueTuple<bool, bool>(false, false));
                AdaptModule.UpdateAdaptation([part]);
                part.RegenerateMesh();
                return;
            }
        }

        static float GetVarValue(Part part, string[] names)
        {
            if (part?.variablesModule == null || names == null) return 0f;
            var dict = part.variablesModule.doubleVariables.GetSaveDictionary();
            foreach (string n in names) { string k = FindKey(dict, n); if (k != null) return (float)dict[k]; }
            return 0f;
        }

        static string FindKey(System.Collections.Generic.Dictionary<string, double> dict, string name) =>
            dict.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));

        static string[] PrimaryVarNames(HandleType t) => t switch
        {
            HandleType.Height => ["height", "h"],
            HandleType.WidthA => ["width_top", "width_a", "width_original_a"],
            HandleType.Width => ["width_original", "width", "w", "radius"],
            HandleType.WidthB => ["width_bottom", "width_b", "width_original_b"],
            _ => System.Array.Empty<string>()
        };

        GUIStyle dimShadow;
        GUIStyle dimLabel;

        void OnGUI()
        {
            if (!boundsValid || !EditorCore.HasSelection || Camera.main == null) return;
            if (dimShadow == null)
            {
                dimShadow = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                dimShadow.normal.textColor = new Color(0f, 0f, 0f, 0.70f);
                dimLabel = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter };
                dimLabel.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            }
            bool isSingleOBB = EditorCore.IsSingle && !Config.S.LockBounds;
            float w, h;
            Vector2 labelPos;
            if (isSingleOBB)
            {
                Part p = EditorCore.SinglePart;
                var (hw, hh, vc) = GetLocalExtents(p);
                float θ = p.orientation.orientation.Value.z;
                w = hw * 2f; h = hh * 2f;
                labelPos = LW(0f, -(hh + 0.22f), vc, θ);
            }
            else
            {
                w = selBounds.size.x; h = selBounds.size.y;
                labelPos = new Vector2(selBounds.center.x, selBounds.min.y - 0.22f);
            }
            Vector3 s = Camera.main.WorldToScreenPoint(labelPos);
            if (s.z < 0) return;
            string text = FormatDim(w) + " × " + FormatDim(h);
            var rect = new Rect(s.x - 60f, Screen.height - s.y - 10f, 120f, 20f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, dimShadow);
            GUI.Label(rect, text, dimLabel);
        }

        static string FormatDim(float v) => Mathf.Abs(v) >= 10f ? v.ToString("F1") : v.ToString("F2");

        void I_GLDrawer.Draw()
        {
            if (!boundsValid || !EditorCore.HasSelection) return;
            const float depth = 5f;
            Config.Data cfg = Config.S;
            Color colOut = new(cfg.OutlineR, cfg.OutlineG, cfg.OutlineB, 0.20f);
            float bboxW = cfg.BBoxLineWidth;
            bool isSingleOBB = EditorCore.IsSingle && !Config.S.LockBounds;
            if (isSingleOBB)
            {
                Part p = EditorCore.SinglePart;
                var (hw, hh, vc) = GetLocalExtents(p);
                float θ = p.orientation.orientation.Value.z;
                Vector2 TL = LW(-hw, hh, vc, θ), TR = LW(hw, hh, vc, θ);
                Vector2 BL = LW(-hw, -hh, vc, θ), BR = LW(hw, -hh, vc, θ);
                GLDrawer.DrawLine(TL, TR, colOut, bboxW, depth);
                GLDrawer.DrawLine(TR, BR, colOut, bboxW, depth);
                GLDrawer.DrawLine(BR, BL, colOut, bboxW, depth);
                GLDrawer.DrawLine(BL, TL, colOut, bboxW, depth);
            }
            else
            {
                DrawRect(selBounds.min, selBounds.max, colOut, bboxW, depth);
            }
            if (isDragging && dragHandle == HandleType.Rotate)
            {
                float r = Vector2.Distance(snapCenter, snapHandlePos);
                DrawCircleOutline(snapCenter, r, RotCircleSegs,
                    new Color(ColRotate.r, ColRotate.g, ColRotate.b, 0.60f), LineW * 0.8f, depth);
            }
            bool ctrl = Config.IsCtrlActive(), alt = Config.IsAltActive();
            if (ctrl || alt)
                GLDrawer.DrawCircle(new Vector2(selBounds.max.x + 0.14f, selBounds.max.y + RotateOffset * 0.4f),
                    HandleHalf * 0.55f, 8, ctrl ? ColHeight : ColWidth, depth);
            float drawHW = 0, drawHH = 0;
            Vector2 drawVC = default;
            float drawΘ = 0;
            if (EditorCore.IsSingle)
            {
                Part p = EditorCore.SinglePart;
                (drawHW, drawHH, drawVC) = GetLocalExtents(p);
                drawΘ = p.orientation.orientation.Value.z;
            }
            foreach (Handle h in handles)
            {
                if (!h.visible) continue;
                Color col = ColorOf(h.type);
                if (h.type == HandleType.Rotate)
                {
                    GLDrawer.DrawCircle(h.worldPos, HandleHalf * 1.4f, 14, col, depth);
                    Vector2 topCenter = isSingleOBB
                        ? LW(0, drawHH, drawVC, drawΘ)
                        : new Vector2(selBounds.center.x, selBounds.max.y);
                    GLDrawer.DrawLine(topCenter, h.worldPos, colOut, bboxW, depth);
                }
                else if (h.type == HandleType.ActionFlipH || h.type == HandleType.ActionFlipV ||
                         h.type == HandleType.ActionLockAspect || h.type == HandleType.ActionLockBounds)
                {
                    string lbl = h.type == HandleType.ActionFlipH ? "FH"
                               : h.type == HandleType.ActionFlipV ? "FV"
                               : h.type == HandleType.ActionLockAspect ? "LA" : "LB";
                    DrawActionHandle(h.worldPos, HandleHalf * 1.3f, col, lbl, depth);
                    float localY = h.type == HandleType.ActionFlipH ? 0.30f
                                 : h.type == HandleType.ActionFlipV ? 0f
                                 : h.type == HandleType.ActionLockAspect ? -0.30f : -0.60f;
                    Vector2 edgeBase = isSingleOBB
                        ? LW(-drawHW, localY, drawVC, drawΘ)
                        : new Vector2(selBounds.min.x, h.worldPos.y);
                    GLDrawer.DrawLine(edgeBase, h.worldPos, colOut, bboxW, depth);
                }
                else if (h.type == HandleType.EdgeBottom || h.type == HandleType.EdgeLeft || h.type == HandleType.EdgeRight)
                {
                    DrawDiamond(h.worldPos, HandleHalf * 1.1f, col, depth);
                }
                else
                {
                    DrawSquare(h.worldPos, HandleHalf, col, depth);
                    if (h.type == HandleType.WidthA || h.type == HandleType.Width || h.type == HandleType.WidthB)
                    {
                        float localY2 = h.type == HandleType.WidthA ? drawHH * 0.50f : h.type == HandleType.WidthB ? -drawHH * 0.50f : 0f;
                        Vector2 edgeBase = EditorCore.IsSingle
                            ? LW(drawHW, localY2, drawVC, drawΘ)
                            : new Vector2(selBounds.max.x, h.worldPos.y);
                        GLDrawer.DrawLine(edgeBase, h.worldPos, colOut, bboxW, depth);
                    }
                    if (h.type == HandleType.Height)
                    {
                        Vector2 edgeBase = EditorCore.IsSingle
                            ? LW(0, drawHH, drawVC, drawΘ)
                            : new Vector2(h.worldPos.x, selBounds.max.y);
                        GLDrawer.DrawLine(edgeBase, h.worldPos, colOut, bboxW, depth);
                    }
                }
            }
        }

        static Color ColorOf(HandleType t) => t switch
        {
            HandleType.CornerTL or HandleType.CornerTR or
            HandleType.CornerBL or HandleType.CornerBR => ColCorner,
            HandleType.Height => ColHeight,
            HandleType.WidthA or HandleType.Width or HandleType.WidthB => ColWidth,
            HandleType.Rotate => ColRotate,
            HandleType.ActionFlipH => ColFlipH,
            HandleType.ActionFlipV => ColFlipV,
            HandleType.ActionLockAspect => Config.S.LockAspect ? ColLockOn : ColLockOff,
            HandleType.ActionLockBounds => Config.S.LockBounds ? ColLockOn : ColLockOff,
            HandleType.EdgeBottom or HandleType.EdgeLeft or HandleType.EdgeRight => ColEdge,
            _ => Color.white
        };

        static void DrawSquare(Vector2 c, float half, Color col, float depth)
        {
            Vector2[] pts = [c + new Vector2(-half, -half), c + new Vector2(half, -half), c + new Vector2(half, half), c + new Vector2(-half, half)];
            for (int i = 0; i < 4; i++) GLDrawer.DrawLine(pts[i], pts[(i + 1) % 4], col, LineW, depth);
            GLDrawer.DrawCircle(c, half * 0.3f, 6, col, depth);
        }

        static void DrawDiamond(Vector2 c, float half, Color col, float depth)
        {
            Vector2 T = c + new Vector2(0f, half), R = c + new Vector2(half, 0f);
            Vector2 B = c + new Vector2(0f, -half), L = c + new Vector2(-half, 0f);
            GLDrawer.DrawLine(T, R, col, LineW, depth); GLDrawer.DrawLine(R, B, col, LineW, depth);
            GLDrawer.DrawLine(B, L, col, LineW, depth); GLDrawer.DrawLine(L, T, col, LineW, depth);
            GLDrawer.DrawCircle(c, half * 0.3f, 6, col, depth);
        }

        static void DrawActionHandle(Vector2 c, float half, Color col, string label, float depth)
        {
            Vector2[] pts = [c + new Vector2(-half, -half), c + new Vector2(half, -half), c + new Vector2(half, half), c + new Vector2(-half, half)];
            for (int i = 0; i < 4; i++) GLDrawer.DrawLine(pts[i], pts[(i + 1) % 4], col, LineW * 1.4f, depth);
            float charW = half * 0.60f, charH = half * 0.86f, gap = half * 0.14f;
            float totalW = charW * label.Length + gap * (label.Length - 1);
            float startX = c.x - totalW * 0.5f + charW * 0.5f;
            for (int i = 0; i < label.Length; i++)
            {
                float cx = startX + i * (charW + gap), cy = c.y;
                Vector2 mn = new(cx - charW * 0.5f, cy - charH * 0.5f);
                float w = charW, h = charH;
                Vector2 P(float u, float v) => new(mn.x + u * w, mn.y + v * h);
                void GL(float x0, float y0, float x1, float y1) => GLDrawer.DrawLine(P(x0, y0), P(x1, y1), col, LineW * 1.1f, depth);
                switch (label[i])
                {
                    case 'F': GL(0.15f,0.1f,0.15f,0.9f); GL(0.15f,0.9f,0.85f,0.9f); GL(0.15f,0.52f,0.70f,0.52f); break;
                    case 'H': GL(0.15f,0.1f,0.15f,0.9f); GL(0.85f,0.1f,0.85f,0.9f); GL(0.15f,0.50f,0.85f,0.50f); break;
                    case 'V': GL(0.10f,0.9f,0.50f,0.1f); GL(0.90f,0.9f,0.50f,0.1f); break;
                    case 'L': GL(0.15f,0.1f,0.15f,0.9f); GL(0.15f,0.1f,0.85f,0.1f); break;
                    case 'B':
                        GL(0.15f,0.1f,0.15f,0.9f); GL(0.15f,0.9f,0.70f,0.9f);
                        GL(0.70f,0.9f,0.85f,0.72f); GL(0.85f,0.72f,0.70f,0.52f);
                        GL(0.15f,0.52f,0.70f,0.52f); GL(0.70f,0.52f,0.85f,0.32f);
                        GL(0.85f,0.32f,0.70f,0.1f); GL(0.15f,0.1f,0.70f,0.1f); break;
                    case 'A': GL(0.10f,0.1f,0.50f,0.9f); GL(0.90f,0.1f,0.50f,0.9f); GL(0.28f,0.48f,0.72f,0.48f); break;
                }
            }
        }

        static Vector2 LW(float lx, float ly, Vector2 pivot, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return pivot + new Vector2(c * lx - s * ly, s * lx + c * ly);
        }

        static void DrawCircleOutline(Vector2 center, float radius, int segs, Color col, float lineW, float depth)
        {
            float step = 2f * Mathf.PI / segs;
            for (int i = 0; i < segs; i++)
            {
                float a0 = step * i, a1 = step * (i + 1);
                GLDrawer.DrawLine(center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                                  center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, col, lineW, depth);
            }
        }

        static void DrawRect(Vector2 min, Vector2 max, Color col, float lineW, float depth)
        {
            Vector2[] pts = [new Vector2(min.x, min.y), new Vector2(max.x, min.y), new Vector2(max.x, max.y), new Vector2(min.x, max.y)];
            for (int i = 0; i < 4; i++) GLDrawer.DrawLine(pts[i], pts[(i + 1) % 4], col, lineW, depth);
        }
    }
}
