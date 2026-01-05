# HDR Gamma Controller

A Windows System Tray application to manage HDR Gamma settings on a per-monitor basis. This tool addresses the "washed out" or incorrect dark levels often experienced when using Windows HDR on OLED and Mini-LED displays.

## Features

- **Per-Monitor Gamma Control**: Apply Gamma 2.2, 2.4, or Windows Default independently for each HDR monitor
- **Real Monitor Names**: Displays actual monitor names from EDID (e.g., "LG OLED TV") instead of generic identifiers
- **Profile Persistence**: Remembers your gamma settings per monitor and restores them automatically on startup
- **Night Mode**: 
  - Automatically adjusts color temperature based on sunset/sunrise at your location
  - Smooth fading transitions (configurable duration)
  - Manual toggle override via Tray or Hotkey (`Win + Shift + N`)
  - Adjustable color temperature (1900K - 6500K)
- **Advanced Color Control**: Fine-tune Brightness, Contrast, Saturation, Tint, and RGB Gains/Offsets per monitor
- **System Tray Integration**: Unobtrusive background operation with dark/light mode support
- **Start with Windows**: Toggle auto-start from the tray menu
- **Global Hotkeys**: Quickly switch profiles on the focused monitor
  - `Win + Shift + F1`: Gamma 2.2
  - `Win + Shift + F2`: Gamma 2.4
  - `Win + Shift + F3`: Windows Default
  - `Win + Shift + F4`: Panic Mode (clear all gamma tables)
  - `Win + Shift + N`: Toggle Night Mode (On/Off)
- **Auto-Recovery**: Automatically reapplies settings after display sleep/wake or configuration changes
- **HDR-Aware**: Only shows gamma options for HDR-active monitors (SDR monitors display informational message)
- **Auto-Download ArgyllCMS**: Automatically downloads required ArgyllCMS binaries if not found

## Requirements

1. **Windows 10/11** with HDR-capable display(s)
2. **ArgyllCMS** (specifically `dispwin.exe`):
   - The app will **automatically download** ArgyllCMS if not detected
   - Or if you have **DisplayCAL** installed, the app detects its bundled Argyll binaries
   - Or download from [ArgyllCMS](https://www.argyllcms.com/) and place `dispwin.exe` in the app folder
3. **.NET 8.0 Runtime** (for minimal build) or no dependencies (for self-contained build)

## Installation

### Option 1: Pre-built Release (Recommended)

1. Download the latest release
2. Extract to your preferred location (e.g., `C:\Program Files\HDRGammaController`)
3. Run `HDRGammaController.exe`
4. Right-click the tray icon and enable "Start with Windows" for auto-start

### Option 2: Build from Source

```powershell
# Clone the repository
git clone https://github.com/davidtorcivia/win11hdr-gamma-adjuster.git
cd win11hdr-gamma-adjuster

# Build minimal (requires .NET 8 runtime, ~1.4 MB)
dotnet publish src/HDRGammaController -c Release --self-contained false -o publish-minimal

# OR build self-contained (no dependencies, ~155 MB)
dotnet publish src/HDRGammaController -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## Usage

1. **Launch** the application - it appears in the system tray
2. **Right-click** the tray icon to see your monitors (numbered with actual names)
3. **Select a gamma mode** for each HDR monitor:
   - **Gamma 2.2**: General PC use, most content
   - **Gamma 2.4**: BT.1886 / dark room / film mastering
   - **Windows Default**: Native piecewise sRGB (bypass)
4. **Enable auto-start** via "Start with Windows" menu option
5. **Open Settings** (via Tray -> Settings) for advanced control:
   - **Night Mode**: Enable "Sunrise/Sunset" for automatic adjustment based on your location (Latitude/Longitude).
   - **Calibration**: Adjust Brightness, Temperature, Tint, and RGB Gains/Offsets for fine-tuning your display's white point and color balance.

   **Note**: All settings are saved automatically to `%LOCALAPPDATA%\HDRGammaController\settings.json`.

Your selections are automatically saved and restored on next launch.

## How It Works

Windows 11 uses piecewise sRGB for SDR content in HDR mode, but most content is mastered on gamma 2.2/2.4 displays. This causes washed-out shadows and reduced contrast. 

This tool generates corrective 1D LUTs that:
1. Decode the PQ (ST.2084) HDR signal
2. Apply proper gamma correction in the SDR range
3. Preserve HDR highlights above the SDR white level
4. Re-encode back to PQ for the display

The LUTs are applied via ArgyllCMS's `dispwin` utility.

## Acknowledgements

- **[dylanraga](https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm)**: Original research and Python implementation
- **[ArgyllCMS](https://www.argyllcms.com/)**: Low-level VCGT access via `dispwin`

## License

This project is licensed under the MIT License.

ArgyllCMS is licensed under the AGPL v3 license. This application calls `dispwin` as a separate process and does not link against AGPL code.
