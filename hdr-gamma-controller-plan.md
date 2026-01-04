# Multi-Monitor HDR Gamma Controller

A fork and extension of [win11hdr-srgb-to-gamma2.2-icm](https://github.com/dylanraga/win11hdr-srgb-to-gamma2.2-icm) adding multi-gamma selection, per-monitor awareness, system tray UI, and hotkey control.

## Problem Statement

Windows 11 uses piecewise sRGB as the SDR-in-HDR transfer function. This is technically correct but practically wrong—virtually all content is mastered on gamma 2.2 displays. The result: washed-out shadows, reduced contrast, and a hazy appearance when HDR is enabled.

The original project solves this with MHC2 ICC profiles or ArgyllCMS LUT loading. This extension adds:

- Switchable gamma curves (2.2, 2.4, Windows default)
- Per-monitor configuration with HDR/SDR awareness
- System tray UI with quick access
- Global hotkey control
- Robust handling of sleep/wake, display changes, HDR toggling

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        System Tray UI                           │
│  (WPF with NotifyIcon)                                         │
│  • Per-monitor gamma dropdown                                   │
│  • HDR status indicators                                        │
│  • Global hotkey configuration                                  │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Core Service Layer                         │
│  • Monitor enumeration (EnumDisplayDevices + DXGI)             │
│  • HDR capability detection (DXGI_OUTPUT_DESC1)                │
│  • Configuration persistence (per-monitor settings)            │
│  • Event handling (display change, wake from sleep)            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                    LUT Generation Engine                        │
│  • Gamma 2.2 transform (general PC use)                        │
│  • Gamma 2.4 transform (BT.1886 / dark room)                   │
│  • Identity/bypass (Windows default sRGB)                      │
│  • SDR white level parameterization                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
┌─────────────────────────┐  ┌─────────────────────────────────────┐
│  MHC2 Profile Path      │  │  dispwin Path (ArgyllCMS)           │
│  • Higher precision     │  │  • Faster iteration                 │
│  • Persists across      │  │  • No profile installation          │
│    reboots              │  │  • Per-monitor targeting (-d flag)  │
│  • Requires admin for   │  │  • Requires running process         │
│    system-wide install  │  │                                     │
└─────────────────────────┘  └─────────────────────────────────────┘
```

---

## Technology Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Language | C# (.NET 8) | Strong Windows API interop, good UI frameworks, familiar ecosystem |
| UI Framework | WPF + Hardcodet.NotifyIcon | Mature, lightweight for tray apps |
| Windows APIs | DXGI 1.6, WCS via P/Invoke | Required for HDR detection and profile management |
| LUT Application | dispwin (ArgyllCMS) + MHC2 profiles | Proven approach from original project |
| Build/Package | Single-file publish (.exe) preferred | See packaging notes below |

**Packaging Notes:**

MSIX was initially considered but has friction with this use case:
- MSIX apps run in a container with file system virtualization
- Shelling out to external executables (dispwin.exe) that talk to hardware drivers can hit `runFullTrust` capability issues
- Debugging containerized behavior adds complexity

**Recommendation:** Use self-contained single-file publish for initial releases. Simpler to debug, no capability declarations needed, works reliably with dispwin. Revisit MSIX if/when distribution through Microsoft Store becomes a goal.

---

## Technical Risks & Challenges

### A. MHC2 Generation Complexity

The plan mentions porting MHC2Gen to C#. This is non-trivial.

**The Problem:** Windows is extremely picky about MHC2 tags in ICC profiles. They are essentially XML data embedded in private binary tags. If one float is off or the structure is slightly invalid, Windows will silently ignore the profile or revert to SDR behavior with no error message.

**Mitigation Strategy:**
- Do NOT attempt to generate MHC2 profiles from scratch initially
- Treat the working `.icm` files from the original project as binary templates
- Only modify the LUT payload data within the existing structure
- Alternatively, create a C# wrapper that shells out to the Python MHC2Gen
- Validate generated profiles against known-good references byte-by-byte in the header regions

### B. SDR White Level Synchronization

The `SdrWhiteLevel` property must stay synchronized with Windows settings.

**The Problem:** Windows' "SDR Content Brightness" slider (System > Display > HDR) changes the relationship between SDR code values and light output. If the app assumes 200 nits but the user has Windows set to 400 nits, the gamma curve will be applied incorrectly—resulting in crushed shadows or clipped highlights.

**The Difficulty:** The slider value is stored in obscure per-monitor registry keys that aren't part of any documented API.

**Mitigation Strategy:**
1. Attempt to read the registry value (likely under `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\` or similar per-monitor paths)
2. If registry reading proves unreliable, the UI must explicitly link the setting: *"Set this to match your Windows SDR Brightness slider (Settings > Display > HDR)"*
3. Consider adding a "Detect" button that shows the user how to find their current Windows value
4. Display the nits-to-slider mapping table from the original project in the settings UI

**Reference - Windows SDR Brightness Mapping:**

| Slider Value | SDR White Level |
|--------------|-----------------|
| 0 | 80 nits |
| 5 | 100 nits |
| 30 | 200 nits |
| 55 | 300 nits |
| 80 | 400 nits |
| 100 | 480 nits |

### C. Driver-Level LUT Conflicts

**The Problem:** When using dispwin (which loads 1D LUTs into GPU hardware via VCGT), GPU drivers behave inconsistently in HDR mode:

| Vendor | Behavior |
|--------|----------|
| NVIDIA | Often ignores VCGT updates in HDR mode unless specific APIs are used |
| AMD | Variable—some driver versions work, others don't |
| Intel | Variable behavior, especially on hybrid GPU laptops |

**Mitigation Strategy:**
- Prioritize MHC2 for production use—it operates at the OS compositor level (DWM), above driver hardware LUTs
- Use dispwin only for rapid prototyping and LUT validation
- Document known driver compatibility issues
- Consider detecting GPU vendor and warning users about potential dispwin limitations

### D. Night Light and f.lux Compatibility

**The Problem:** Windows Night Light and third-party tools like f.lux apply their own color transforms. These can conflict with custom gamma loaders.

**Behavior by Application Method:**
- **MHC2 profiles:** Night Light stacks correctly on top (applied after the profile in the compositor pipeline)
- **dispwin/VCGT:** Night Light may override the gamma LUT or vice versa, depending on driver implementation

**Recommendation:** Document MHC2 as the recommended method for users who also use Night Light. Consider detecting Night Light state and warning if using dispwin method.

---

## Component Specifications

### 1. Monitor Management Module

Enumerates all monitors, detects HDR state per-monitor, tracks display topology changes.

**Implementation:**

- Use `DXGI 1.6` with `IDXGIOutput6::GetDesc1()` returning `DXGI_OUTPUT_DESC1`:
  - `ColorSpace` indicates HDR active state (`DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020`)
  - `BitsPerColor` for bit depth
  - Monitor identifiers for matching
- Cross-reference with `EnumDisplayDevices` for device path correlation
- Subscribe to `WM_DISPLAYCHANGE` for topology changes
- Use `RegisterPowerSettingNotification` for wake events
- Leverage `ColorProfileGetDisplayList` API for Advanced Color profile enumeration

**⚠️ DXGI-to-GDI Correlation Complexity:**

This is the hardest integration point in the codebase. DXGI uses LUIDs (Locally Unique Identifiers) for adapters, while GDI (`EnumDisplayDevices`) uses Device Names (`\\.\DISPLAY1`).

**Mapping Strategies:**
1. Match Monitor Device Path strings from `DXGI_OUTPUT_DESC.DeviceName` against `DISPLAY_DEVICE.DeviceName`
2. For difficult cases, use `D3DKMTQueryAdapterInfo` (low-level kernel interface)
3. Multi-GPU setups (laptop iGPU + dGPU) make this matching non-linear—a monitor may be enumerated by one adapter in DXGI but controlled by another in GDI

**Recommendation:** Build a robust `MonitorCorrelator` class early with extensive logging. Test on multi-GPU systems before assuming the mapping works.

**Data Model:**

```csharp
public enum GammaMode
{
    Gamma22,        // 2.2 - general PC use
    Gamma24,        // 2.4 - BT.1886 dark room
    WindowsDefault  // Identity / bypass
}

public record MonitorInfo
{
    public string DevicePath { get; init; }        // \\.\DISPLAY1
    public string FriendlyName { get; init; }      // "LG OLED C2"
    public bool IsHdrCapable { get; init; }
    public bool IsHdrActive { get; init; }
    public GammaMode CurrentGamma { get; set; }
    public int SdrWhiteLevel { get; set; }         // 80-480 nits
}
```

### 2. LUT Generation Engine

Port of the original JavaScript implementation to C# with full precision.

**Algorithm:**

```
For each input value v in [0, 1023]:
    normalized = v / 1023
    linear = PQ_EOTF(normalized)                          // ST.2084 → linear nits
    srgb_normalized = sRGB_InverseEOTF(linear, white, black)
    gamma_applied = srgb_normalized ^ gamma               // gamma = 2.2 or 2.4
    output_linear = black + (white - black) * gamma_applied
    output = PQ_InverseEOTF(output_linear)               // linear nits → ST.2084
    
    // Shoulder blend for HDR headroom preservation
    blend_factor = min(1, linear / white)
    final = lerp(output, normalized, blend_factor)
```

**Gamma Profiles:**

| Mode | Gamma | Use Case |
|------|-------|----------|
| `Gamma22` | 2.2 | General PC use, most content |
| `Gamma24` | 2.4 | BT.1886 / dark room / film mastering |
| `WindowsDefault` | Identity | Native piecewise sRGB (bypass) |

**Precision Requirements:**

- `double` precision throughout all calculations
- 1024-point LUT (matches Windows MHC2 pipeline)
- Validate output against original project's reference LUTs

**PQ Transfer Function Constants:**

```csharp
private const double M1 = 2610.0 / 4096.0 / 4.0;
private const double M2 = 2523.0 / 4096.0 * 128.0;
private const double C1 = 3424.0 / 4096.0;
private const double C2 = 2413.0 / 4096.0 * 32.0;
private const double C3 = 2392.0 / 4096.0 * 32.0;
```

### 3. Profile Application Layer

Two application strategies, user-selectable:

#### A. MHC2 Profile Method

Best for persistence across reboots.

- ⚠️ **Do not generate profiles from scratch** — Windows is extremely picky about MHC2 tag structure
- Use existing `.icm` files from original project as binary templates
- Patch only the LUT payload bytes, preserving all header/metadata structure
- Use `ColorProfileAddDisplayAssociation` (Windows 10 2004+ API) for per-monitor binding
- Per-user install works without elevation; system-wide requires admin
- Survives reboots and HDR toggles
- Profile naming: `HDRGamma_<MonitorHash>_G22_W200.icm`

**Template-Based Generation Flow:**
1. Load reference `.icm` file as byte array
2. Locate LUT payload offset (fixed position in MHC2 structure)
3. Generate new LUT values based on user's gamma/white level settings
4. Write new LUT bytes into payload region
5. **Update or zero the Profile ID checksum** (see below)
6. Save as new `.icm` file
7. Install via WCS API

**⚠️ ICC Profile Checksum (bytes 84-99):**

The ICC header contains a Profile ID field which is typically an MD5 fingerprint of the profile data. If Windows validates this hash, a patched profile with stale checksum may be rejected as corrupt.

**Options:**
1. Recalculate MD5 after patching and update bytes 84-99
2. Zero out the ID field (ICC spec permits all-zero ID if not calculated)

Test both approaches against Windows Advanced Color pipeline. Zero-ID is simpler but behavior should be validated empirically.

#### B. dispwin Method

Best for rapid testing and preview. **Use primarily for development/validation.**

- Shell out to `dispwin.exe -d<monitor_index> <cal_file>`
- Per-monitor support via `-d` flag
- Ephemeral—lost on reboot/sleep unless reapplied
- Faster iteration during configuration

**⚠️ Driver Limitations in HDR Mode:**
- NVIDIA may ignore VCGT updates when HDR is active
- AMD/Intel behavior varies by driver version
- Use dispwin to validate LUT math, but recommend MHC2 for production use

**Recommended Workflow:**

Use dispwin for live preview/testing, then "commit" to MHC2 profile for persistence. User prompted to download dispwin from ArgyllCMS on first use (avoids bundling AGPL code).

### 4. System Tray Application

**Features:**

- Tray icon with color-coded state indication
- Right-click context menu:

```
─────────────────────────
Monitor 1: LG OLED C2 [HDR Active]
  ○ Gamma 2.2
  ● Gamma 2.4
  ○ Windows Default
  SDR White: [200▼] nits
─────────────────────────
Monitor 2: Dell S2721 [SDR]
  (HDR not active - no changes applied)
─────────────────────────
Settings...
Exit
```

- Tooltip displays current per-monitor configuration
- Left-click: cycle through gamma presets (configurable)

**Hotkey System:**

- Global hotkeys via `RegisterHotKey` Win32 API
- Default bindings:
  - `Win+Shift+G` — cycle gamma modes on focused monitor
  - `Win+Shift+1` — select Gamma 2.2
  - `Win+Shift+2` — select Gamma 2.4
  - `Win+Shift+3` — select Windows Default
  - `Win+Shift+Up/Down` — adjust SDR white level ±25 nits
  - `Ctrl+Alt+Shift+R` — **Panic/Safe Mode**: immediately unload all LUTs and profiles
- All hotkeys user-configurable via settings

**Visual Feedback (OSD Overlay):**

When a hotkey triggers a mode change, display a brief on-screen overlay (e.g., "Gamma 2.4 Applied") because:
- Visual changes may be subtle depending on current screen content
- User needs confirmation the hotkey was registered
- Essential for the panic hotkey to confirm recovery

**Why OSD overlay instead of native Windows notifications:**
- Native Action Center toasts are slow to appear (~500ms+)
- Toasts persist in notification history, cluttering it during rapid A/B comparisons
- Custom WPF overlay window appears instantly, fades automatically, feels native (like volume/brightness flyouts)

**Implementation:** Small borderless WPF window, centered on active monitor, auto-fade after 1.5s, no focus steal.

**Panic Hotkey Rationale:**

If a malformed LUT is loaded (e.g., all black output, severely crushed), the user needs a guaranteed recovery path without rebooting. The panic hotkey:
1. Calls `dispwin -c` to clear VCGT
2. Removes any active MHC2 profile association
3. Shows a high-contrast confirmation dialog

### 5. Configuration & Persistence

**Storage Location:** `%AppData%\HDRGammaController\config.json`

**Schema:**

```json
{
  "version": 1,
  "globalHotkeys": {
    "cycleGamma": "Win+Shift+G",
    "selectGamma22": "Win+Shift+1",
    "selectGamma24": "Win+Shift+2",
    "selectDefault": "Win+Shift+3",
    "increaseWhite": "Win+Shift+Up",
    "decreaseWhite": "Win+Shift+Down",
    "panic": "Ctrl+Alt+Shift+R"
  },
  "monitors": {
    "\\\\?\\DISPLAY#GSM5B08#...": {
      "enabled": true,
      "gammaMode": "Gamma24",
      "sdrWhiteLevel": 200
    }
  },
  "preferences": {
    "autoApplyOnWake": true,
    "useProfileMethod": true,
    "dispwinPath": null,
    "startWithWindows": true,
    "showNotifications": true
  }
}
```

**Settings UI:**

- Hotkey customization with conflict detection
- Application method toggle (MHC2 vs dispwin)
- Auto-start on login
- dispwin.exe path configuration
- Logging verbosity

### 6. Event Handling & Robustness

**Critical Events:**

| Event | Detection Method | Action |
|-------|------------------|--------|
| Display connect/disconnect | `WM_DISPLAYCHANGE` | Re-enumerate monitors, apply saved config to new displays |
| HDR toggle | Poll `DXGI_OUTPUT_DESC1` or monitor registry | Apply transform when HDR activates, remove when deactivated |
| Sleep/wake | `WM_POWERBROADCAST` | Reapply LUT (dispwin method gets cleared by driver) |
| Resolution/refresh change | `WM_DISPLAYCHANGE` | Reapply if needed |
| Driver crash/reset | `WM_DEVICECHANGE` | Reapply after brief delay |

**Implementation Details:**

- Background message loop maintains event handling when minimized to tray
- Debounce rapid events with 500ms delay before reapplication
- Log all events with timestamps for debugging
- Optional toast notifications for state changes

---

## Security Considerations

1. **No admin required for typical use** — Per-user profile installation via `WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER`

2. **dispwin.exe handling** — Not bundled; user prompted to download from official ArgyllCMS site. Optional SHA256 verification on first use.

3. **No network access** — Application operates entirely locally. No telemetry, no update checks unless explicitly enabled.

4. **Scheduled task sandboxing** — If using Task Scheduler for auto-apply, runs as current user, not SYSTEM.

5. **Code signing** — Release builds should be signed to avoid SmartScreen warnings.

---

## Performance Characteristics

| Operation | Expected Time | Notes |
|-----------|---------------|-------|
| LUT generation | < 1ms | 1024 floating-point operations |
| dispwin application | 50-100ms | Per monitor |
| MHC2 profile install | ~200ms | One-time per configuration change |
| Memory footprint | < 20MB | WPF tray app baseline |
| Idle CPU | ~0% | Pure event-driven architecture |

---

## Development Phases

### Phase 1: Core Engine

- Port LUT generation algorithm to C# with double precision
- Implement ST.2084 PQ EOTF and inverse functions
- Implement sRGB inverse EOTF with parameterized white/black levels
- Create command-line test harness: `hdrgamma.exe --monitor 1 --gamma 2.4 --white 200`
- Validate output against original project's reference LUT files
- Unit tests for mathematical accuracy

### Phase 2: Monitor Detection

- Implement DXGI 1.6 monitor enumeration via COM interop
- Detect HDR capability and active state per monitor
- Correlate DXGI outputs with `EnumDisplayDevices` paths
- Build monitor state change detection (polling + events)
- Handle multi-monitor topologies (extended, cloned, etc.)

**Critical: Build `DebugMonitors.exe` immediately.**

`MonitorCorrelator` cannot be unit tested—it depends on user hardware topology. Create a standalone diagnostic utility that dumps:
- All DXGI adapter/output information (LUIDs, device paths, HDR state)
- All GDI `DISPLAY_DEVICE` entries
- The correlation mapping your logic produces

This utility is essential for troubleshooting "not working on my second monitor" reports. Ship it alongside the main application.

### Phase 3: Application Layer

**Critical: dispwin first, MHC2 second.**

dispwin is the fastest path to validating LUT math. If the generated curves look wrong visually, the application method is irrelevant—fix the math first.

1. **dispwin Integration (do first)**
   - Implement .cal file generation matching ArgyllCMS format
   - Shell out to dispwin with per-monitor `-d` flag
   - Build the "panic" reset functionality (`dispwin -c`)
   - Validate LUT output visually against original project's reference
   - Test on both NVIDIA and AMD to document driver behavior differences

2. **MHC2 Profile Generation (do second)**
   - Start by treating original `.icm` files as binary templates
   - Identify the byte offsets of the LUT payload within the template
   - Implement binary patching of LUT data only (don't regenerate headers)
   - Validate against Windows by checking if profile appears in Color Management
   - Only consider full MHC2Gen port if template approach proves limiting

### Phase 4: System Tray UI

- WPF application with NotifyIcon integration
- Per-monitor context menu with gamma selection
- SDR white level adjustment UI
- Global hotkey registration and handling
- Settings dialog for configuration
- Configuration persistence (JSON)

### Phase 5: Robustness & Distribution

- Comprehensive event handling for all display change scenarios
- Debouncing and error recovery
- Logging system for diagnostics
- Single-file self-contained publish (see packaging notes in Technology Stack)
- Auto-start registration (Registry `Run` key or Task Scheduler)
- Bundle `DebugMonitors.exe` for user troubleshooting
- Documentation and README

---

## Project Structure

```
HDRGammaController/
├── src/
│   ├── HDRGammaController/           # Main WPF application
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml           # Settings window
│   │   ├── TrayIcon.cs
│   │   └── ViewModels/
│   ├── HDRGammaController.Core/      # Core library
│   │   ├── LutGenerator.cs
│   │   ├── TransferFunctions.cs
│   │   ├── MonitorManager.cs
│   │   ├── MonitorCorrelator.cs      # DXGI-to-GDI mapping
│   │   ├── ProfileManager.cs
│   │   ├── ProfileTemplatePatching.cs
│   │   ├── DispwinRunner.cs
│   │   └── Configuration.cs
│   ├── HDRGammaController.Interop/   # P/Invoke definitions
│   │   ├── Dxgi.cs
│   │   ├── User32.cs
│   │   └── Wcs.cs
│   ├── HDRGammaController.Cli/       # Command-line tool
│   │   └── Program.cs
│   └── HDRGammaController.DebugMonitors/  # Hardware diagnostics utility
│       └── Program.cs
├── tests/
│   └── HDRGammaController.Tests/
│       ├── LutGeneratorTests.cs       # Unit tests - mathematical accuracy
│       └── TransferFunctionTests.cs   # Unit tests - PQ/sRGB functions
├── docs/
│   └── PLAN.md                       # This document
└── README.md
```

---

## Related Resources

- [MHC2Gen](https://github.com/dantmnf/MHC2) — MHC2 ICC profile generator
- [ArgyllCMS](https://www.argyllcms.com/) — dispwin and color science utilities
- [Windows hardware display color calibration pipeline](https://learn.microsoft.com/en-us/windows/win32/wcs/display-calibration-mhc) — Microsoft documentation
- [ICC profile behavior with Advanced Color](https://learn.microsoft.com/en-us/windows/win32/wcs/advanced-color-icc-profiles) — Profile behavior in HDR mode
- [Use DirectX with Advanced Color](https://learn.microsoft.com/en-us/windows/win32/direct3darticles/high-dynamic-range) — DXGI HDR detection APIs

---

## License Considerations

- Original project: No explicit license (will need to clarify with author or treat as reference only)
- ArgyllCMS (dispwin): AGPL — do not bundle, prompt user to download
- MHC2Gen: MIT — can be ported or referenced freely
