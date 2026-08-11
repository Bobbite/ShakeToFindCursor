# 🔍 Shake to Find Cursor (Windows 11)

[![Windows 11 Compatible](https://img.shields.io/badge/OS-Windows%2011-0078D4?logo=windows11&logoColor=white)](https://github.com/Bobbite/ShakeToFindCursor)
[![Language](https://img.shields.io/badge/Language-C%23-239120?logo=c-sharp&logoColor=white)](https://github.com/Bobbite/ShakeToFindCursor)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/badge/Download-Standalone%20EXE-brightgreen)](https://github.com/Bobbite/ShakeToFindCursor/releases)

A fast, lightweight, native Windows 11 utility inspired by **Bazzite** and **macOS**. When you rapidly shake your mouse pointer back and forth, the cursor smoothly expands up to **300px** (or your preferred size cap) so you can instantly locate it on multi-monitor setups or high-DPI displays, then smoothly shrinks back down.

---

## ✨ Features

- 🔍 **Shake to Find Gesture**: Detects rapid mouse shaking and smoothly grows your cursor.
- 🎯 **Always Default Arrow Pointer**: When enlarged, it always displays a clean, recognizable default arrow pointer, ignoring text selection I-beams, link hands, crosshairs, or resize handles.
- 📈 **Progressive Growth Curve**: Grows incrementally on each shake oscillation so it feels natural and controlled rather than jumping to max size instantly.
- 🖥️ **Click-Through Layered Overlay**: Renders via a per-pixel alpha top-most window (`WS_EX_TRANSPARENT`). Never intercepts mouse clicks or steals keyboard focus. Zero interference with games or applications.
- 🎨 **Custom Cursor Support**: Full support for custom Windows mouse themes, `.cur`/`.ani` cursor files, and custom scale hotspots.
- ⚙️ **Windows 11 System Tray & Dark Mode Settings**:
  - **Max Cursor Size Slider**: Adjust size cap from 100px up to 500px (Default: **300px**).
  - **Shake Sensitivity**: Customize low/high shake detection thresholds.
  - **Shrink Animation Speed**: Adjust decay rate (Slow / Normal / Fast).
  - **Test Shake Button**: Live preview cursor expansion directly inside settings window.
  - **Start with Windows**: Toggle automatic startup on Windows boot.
- 🚀 **Zero External Dependencies**: Single standalone executable (`ShakeToFindCursor.exe`). No extra runtimes or installers needed.

  <p align="center" width="100%">
<img width="660" height="420" alt="cursorsizegithub" src="https://github.com/user-attachments/assets/bb2ef9ba-97fd-4f94-9257-7fe2b5cba69c" />
</p>
---

## 💾 Download & Installation

Direct Executable Download
1. Download [`ShakeToFindCursor.exe`](https://github.com/Bobbite/ShakeToFindCursor/releases/latest/download/ShakeToFindCursor.exe) from the [Releases Page](https://github.com/Bobbite/ShakeToFindCursor/releases).
2. Double-click `ShakeToFindCursor.exe` to run it.
3. Look for the blue mouse icon in your **Windows System Tray** (bottom right near the clock).

---

## 🛠️ Building from Source

The project includes an automated PowerShell build script that compiles `ShakeToFindCursor.cs` using the built-in Windows C# compiler (`csc.exe`):

```powershell
# Clone the repository
git clone https://github.com/Bobbite/ShakeToFindCursor.git
cd ShakeToFindCursor

# Compile the application
.\build.ps1
```

This generates `ShakeToFindCursor.exe` in your project folder.

---

## 📖 Settings & Usage

| Setting | Description |
| :--- | :--- |
| **Max Cursor Size** | Caps the maximum size of the enlarged cursor (Default: 300px, Range: 100px–500px). |
| **Shake Sensitivity** | Controls how easily mouse oscillations trigger growth. |
| **Shrink Speed** | Controls how quickly the cursor contracts when shaking stops. |
| **Enlargement Behavior** | Selects between Smooth Overlay Mode and System Swap Mode. |
| **Start with Windows** | Configures HKCU registry to auto-start on boot. |

---

## 📜 License

This project is licensed under the [MIT License](LICENSE).
