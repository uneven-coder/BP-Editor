# BP Editor

A mod for **Spaceflight Simulator** that replaces the vanilla part editor with, visual editing tools. it has interactive handles, align & distribute tools, dimension labels, and property controls.

<img width="665" height="442" alt="image" src="https://github.com/user-attachments/assets/8fe960fd-f8b6-4557-b1f3-2efc1ca02d7b" />


## Features

### Visual Handles
- **Corner handles** - drag to scale X and Y simultaneously
- **Edge midpoint handles** - drag to scale a single axis (bottom, left, right edges)
- **Rotation handle** - drag to rotate freely; a guide circle tracks the pivot
- **Height / Width variable handles** - direct drag-editing of `height`, `width`, `width_top`, `width_bottom`, and `radius` part variables
- **Action handles** - tap to flip horizontal/vertical, toggle Lock Aspect, toggle Lock Bounds

### Align & Distribute (multi-select)
- Align Left, Right, Top, Bottom
- Centre Horizontally / Vertically
- Distribute evenly H / V
- **Radial arrange** - place parts in a circle at a given radius; optional face-outward rotation

### Dimension Labels
W × H readout rendered below each selection's bounding box, in world-space coordinates.

### Property Panel
A side panel that allows editing off:
- Position (X / Y)
- Orientation (Scale X, Scale Y, Rotation)
- Flip H / V, Rot ±90°
- All double, bool, and string part variables with +/− step buttons
- Toggle controls sourced from `VariablesDrawer` and `ToggleModule` components
- Group Scale All / Rotate All for multi-select

### Sensitivity Modes
Hold **Ctrl** for fine movement, **Alt** for ultra-fine. Both can be switched to toggle mode in settings.

### Keyboard Safety
All SFS keyboard shortcuts are paused while any panel text field has focus. A yellow "Keybinds Paused" banner appears as a reminder.

## Settings

Open the ModLoader settings menu → **BP Editor** to configure:

| Setting | Default | Description |
|---|---|---|
| Normal Sensitivity | 1.0 | Scale factor applied to all handle drags |
| Ctrl Fine | 0.1 | Sensitivity when Ctrl is held / toggled |
| Alt Ultra | 0.05 | Sensitivity when Alt is held / toggled |
| Ctrl as Toggle | off | Ctrl latches instead of requiring hold |
| Alt as Toggle | off | Alt latches instead of requiring hold |
| Handle Outline Colour | white | RGB colour of bounding-box lines |
| Box Line Width | 0.013 | Thickness of the bounding box lines |
