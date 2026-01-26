using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HDRGammaController.Core.Calibration
{
    /// <summary>
    /// Service for communicating with colorimeters via ArgyllCMS spotread.
    /// Supports i1 Display Plus and other ArgyllCMS-compatible instruments.
    /// </summary>
    /// <remarks>
    /// This service uses ArgyllCMS's spotread utility for measurements.
    /// spotread is a command-line tool that reads a single color from a display
    /// using a connected colorimeter.
    ///
    /// Supported instruments (via ArgyllCMS):
    /// - X-Rite i1 Display Pro/Plus
    /// - X-Rite i1 Pro/Pro2
    /// - X-Rite ColorMunki
    /// - Datacolor Spyder series
    /// - And many others
    /// </remarks>
    public class ColorimeterService : IDisposable
    {
        private readonly string _argyllBinPath;
        private string? _spotreadPath;
        private bool _isInitialized;
        private bool _isInitializing; // Guards against concurrent InitializeAsync calls
        private ColorimeterInfo? _connectedColorimeter;
        private int _displayIndex = 1;
        private DisplayType _displayType = DisplayType.LcdLed;
        private readonly object _lock = new();

        // Log file for debugging spotread communication
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HDRGammaController", "colorimeter.log");

        private static void Log(string message)
        {
            try
            {
                string logDir = Path.GetDirectoryName(LogFilePath)!;
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(LogFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Ignore logging errors
            }
        }

        /// <summary>
        /// Gets the path to the log file for debugging.
        /// </summary>
        public static string GetLogFilePath() => LogFilePath;

        /// <summary>
        /// Event raised when a measurement completes.
        /// </summary>
        public event EventHandler<MeasurementEventArgs>? MeasurementCompleted;

        /// <summary>
        /// Event raised when there's an error during measurement.
        /// </summary>
        public event EventHandler<MeasurementErrorEventArgs>? MeasurementError;

        /// <summary>
        /// Event raised when colorimeter status changes.
        /// </summary>
        public event EventHandler<ColorimeterStatusEventArgs>? StatusChanged;

        /// <summary>
        /// Gets the currently connected colorimeter info.
        /// </summary>
        public ColorimeterInfo? ConnectedColorimeter => _connectedColorimeter;

        /// <summary>
        /// Gets whether a colorimeter is connected and ready.
        /// </summary>
        public bool IsReady => _isInitialized && _connectedColorimeter != null;

        /// <summary>
        /// Creates a new ColorimeterService.
        /// </summary>
        /// <param name="argyllBinPath">Path to ArgyllCMS bin directory (containing spotread.exe)</param>
        public ColorimeterService(string argyllBinPath)
        {
            _argyllBinPath = argyllBinPath;
        }

        /// <summary>
        /// Initializes the service and detects connected colorimeters.
        /// </summary>
        /// <remarks>
        /// Thread-safe: Multiple concurrent calls will only perform initialization once.
        /// Subsequent calls after successful initialization return immediately.
        /// Failed initialization can be retried by calling again.
        /// </remarks>
        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            // Fast path: already initialized successfully
            lock (_lock)
            {
                if (_isInitialized && _connectedColorimeter != null)
                    return true;
            }

            // Slow path: need to initialize (but only one thread at a time)
            // Note: We can't hold the lock across await, so we use a flag pattern
            bool shouldInitialize;
            lock (_lock)
            {
                // Double-check after acquiring lock
                if (_isInitialized && _connectedColorimeter != null)
                    return true;

                // Mark that we're about to initialize (prevent concurrent attempts)
                // _isInitialized remains false until we successfully complete
                shouldInitialize = !_isInitializing;
                if (shouldInitialize)
                    _isInitializing = true;
            }

            // If another thread is already initializing, wait for it
            if (!shouldInitialize)
            {
                // Poll until initialization completes, with a maximum timeout of 60 seconds
                const int maxWaitMs = 60000;
                const int pollIntervalMs = 50;
                int elapsed = 0;

                while (elapsed < maxWaitMs)
                {
                    await Task.Delay(pollIntervalMs, cancellationToken);
                    elapsed += pollIntervalMs;
                    lock (_lock)
                    {
                        if (!_isInitializing)
                            return _isInitialized && _connectedColorimeter != null;
                    }
                }

                // Timeout waiting for another thread to complete initialization
                throw new TimeoutException("Timeout waiting for colorimeter initialization to complete");
            }

            try
            {
                // Find spotread executable
                _spotreadPath = FindSpotread();
                if (string.IsNullOrEmpty(_spotreadPath))
                {
                    RaiseStatusChanged(ColorimeterStatus.NotFound,
                        "ArgyllCMS spotread not found. Please install ArgyllCMS.");
                    return false;
                }

                RaiseStatusChanged(ColorimeterStatus.Searching, "Searching for colorimeter...");

                // Detect connected colorimeter
                _connectedColorimeter = await DetectColorimeterAsync(cancellationToken);

                if (_connectedColorimeter != null)
                {
                    // Give the USB HID device time to fully release after detection
                    // This prevents "sharing violation" errors when measurement starts
                    await Task.Delay(500, cancellationToken);

                    lock (_lock)
                    {
                        _isInitialized = true;
                    }
                    RaiseStatusChanged(ColorimeterStatus.Ready,
                        $"Connected: {_connectedColorimeter.Model}");
                    return true;
                }
                else
                {
                    RaiseStatusChanged(ColorimeterStatus.NotConnected,
                        "No colorimeter detected. Please connect your i1 Display Plus.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RaiseStatusChanged(ColorimeterStatus.Error, $"Initialization failed: {ex.Message}");
                return false;
            }
            finally
            {
                lock (_lock)
                {
                    _isInitializing = false;
                }
            }
        }

        /// <summary>
        /// Sets the display index for measurements (1-based).
        /// </summary>
        public void SetDisplayIndex(int index)
        {
            if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
            _displayIndex = index;
        }

        /// <summary>
        /// Sets the display type for measurements (affects colorimeter compensation).
        /// </summary>
        public void SetDisplayType(DisplayType type)
        {
            _displayType = type;
            Log($"Display type set to: {type} (flag: -{type.ToSpotreadFlag()})");
        }

        /// <summary>
        /// Gets the current display type setting.
        /// </summary>
        public DisplayType DisplayType => _displayType;

        /// <summary>
        /// Takes a single color measurement.
        /// </summary>
        /// <param name="patch">The patch being measured (for context)</param>
        /// <param name="hdrMode">Whether to use HDR measurement mode</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The measurement result</returns>
        public async Task<MeasurementResult> MeasureAsync(
            ColorPatch patch,
            bool hdrMode = false,
            CancellationToken cancellationToken = default)
        {
            if (!IsReady)
                throw new InvalidOperationException("Colorimeter not initialized. Call InitializeAsync first.");

            Log($"=== Starting measurement for patch: {patch.Name} (RGB: {patch.DisplayRgb.R:F3},{patch.DisplayRgb.G:F3},{patch.DisplayRgb.B:F3}) HDR={hdrMode} ===");

            try
            {
                var xyz = await TakeMeasurementAsync(hdrMode, cancellationToken);

                var result = new MeasurementResult
                {
                    Patch = patch,
                    Xyz = xyz,
                    IsValid = true
                };

                MeasurementCompleted?.Invoke(this, new MeasurementEventArgs(result));
                return result;
            }
            catch (Exception ex)
            {
                var result = new MeasurementResult
                {
                    Patch = patch,
                    Xyz = new CieXyz(0, 0, 0),
                    IsValid = false,
                    ErrorMessage = ex.Message
                };

                MeasurementError?.Invoke(this, new MeasurementErrorEventArgs(ex.Message, patch));
                return result;
            }
        }

        /// <summary>
        /// Takes a raw XYZ measurement using spotread.
        /// </summary>
        private async Task<CieXyz> TakeMeasurementAsync(bool hdrMode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_spotreadPath))
                throw new InvalidOperationException("spotread path not configured");

            // Build spotread arguments for non-interactive scripted use:
            // -O       One measurement and exit (designed for scripted use)
            // -N       Skip calibration if valid (speeds up successive readings)
            // -c 1     Use instrument port 1 (the HID colorimeter, not serial ports)
            // -d N     Display number (1-based)
            // -e       Emissive measurement mode (absolute results) - for display measurement
            // -y X     Display type correction (l=LCD CCFL, e=LCD LED, o=OLED, etc.)
            // -H       HDR mode (higher brightness measurement range)
            string displayTypeFlag = _displayType.ToSpotreadFlag();
            var args = $"-O -N -c 1 -d{_displayIndex} -e -y {displayTypeFlag}";
            if (hdrMode)
                args += " -H";

            Log($"Running: {_spotreadPath} {args}");
            Log("Using ARGYLL_NOT_INTERACTIVE=1 for programmatic control");

            var psi = new ProcessStartInfo(_spotreadPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                // Set working directory to bin path to avoid path issues
                WorkingDirectory = Path.GetDirectoryName(_spotreadPath)
            };

            // CRITICAL: Set ARGYLL_NOT_INTERACTIVE=1 to enable programmatic control
            // This tells ArgyllCMS tools to expect character+return instead of single keystrokes
            // and changes progress output to use line feeds instead of carriage returns
            psi.Environment["ARGYLL_NOT_INTERACTIVE"] = "1";

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            bool promptDetected = false;

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    Log($"stdout> {e.Data}");

                    // Detect when spotread is ready for input
                    if (e.Data.Contains("to read") || e.Data.Contains("to take") ||
                        e.Data.Contains("Place instrument") || e.Data.Contains("key to"))
                    {
                        promptDetected = true;
                    }
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    Log($"stderr> {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Log("Process started, waiting for prompt...");

            // Wait for spotread to initialize and show prompt (or timeout after 5 seconds)
            var startTime = DateTime.UtcNow;
            while (!promptDetected && !process.HasExited && (DateTime.UtcNow - startTime).TotalSeconds < 5)
            {
                await Task.Delay(100, cancellationToken);
            }

            // Send space + newline to trigger measurement
            // In ARGYLL_NOT_INTERACTIVE mode on Windows, character and return must be written in single operation
            if (!process.HasExited)
            {
                try
                {
                    Log("Sending space+newline to trigger measurement...");
                    // Write space and newline as a single operation (important for Windows)
                    await process.StandardInput.WriteAsync(" \r\n");
                    await process.StandardInput.FlushAsync();
                }
                catch (Exception ex)
                {
                    Log($"Error sending trigger: {ex.Message}");
                }
            }

            // Wait for measurement to complete (with timeout)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("Measurement timed out");
            }

            string output = outputBuilder.ToString();
            string error = errorBuilder.ToString();
            string combined = output + "\n" + error;

            Log($"spotread exit code: {process.ExitCode}");
            Log($"stdout: {(string.IsNullOrEmpty(output) ? "(empty)" : output.Trim())}");
            Log($"stderr: {(string.IsNullOrEmpty(error) ? "(empty)" : error.Trim())}");

            // Note: "SetCommState failed with LastError 31" on serial ports (COM1, etc) is
            // just a warning during device enumeration - it doesn't mean the colorimeter failed.
            // The colorimeter uses USB HID, not serial ports. We only fail if we can't get XYZ data.

            // First, try to parse XYZ data - if successful, ignore serial port warnings
            try
            {
                return ParseSpotreadOutput(output, combined);
            }
            catch (InvalidOperationException parseEx)
            {
                // Parsing failed - now check for specific errors to give better messages
                Log($"Failed to parse XYZ data: {parseEx.Message}");

                // Check if there's a sharing violation (another app using device)
                if (combined.Contains("LastError 32", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Colorimeter is in use by another application.\n" +
                        "Close DisplayCAL, i1Profiler, or other calibration software and try again.");
                }

                // Re-throw the parse error with additional context
                throw;
            }
        }

        /// <summary>
        /// Parses XYZ values from spotread output.
        /// </summary>
        /// <remarks>
        /// spotread output format (relevant lines):
        /// Result is XYZ:  45.123  47.456  52.789, D50 Lab: 73.45  -2.34   5.67
        /// or
        /// XYZ:  45.123  47.456  52.789
        /// </remarks>
        private static CieXyz ParseSpotreadOutput(string output, string combinedOutput)
        {
            // Try to find XYZ values in the output
            // Pattern: "XYZ:" followed by three floating-point numbers
            var xyzPattern = new Regex(
                @"XYZ:\s*(-?\d+\.?\d*)\s+(-?\d+\.?\d*)\s+(-?\d+\.?\d*)",
                RegexOptions.IgnoreCase);

            var match = xyzPattern.Match(output);
            if (!match.Success)
            {
                // Try combined output as fallback
                match = xyzPattern.Match(combinedOutput);
            }

            if (!match.Success)
            {
                // Provide helpful error message based on output content
                string errorDetail = output.Length > 200 ? output.Substring(0, 200) + "..." : output;
                Log($"ERROR: No XYZ data found in output. Details: {errorDetail}");
                throw new InvalidOperationException(
                    $"Measurement failed - no color data received. Ensure colorimeter is positioned correctly.\n" +
                    $"Details: {errorDetail}");
            }

            double x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double z = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            Log($"SUCCESS: Parsed XYZ = ({x:F4}, {y:F4}, {z:F4})");
            return new CieXyz(x, y, z);
        }

        /// <summary>
        /// Detects connected colorimeters using spotread.
        /// </summary>
        private async Task<ColorimeterInfo?> DetectColorimeterAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_spotreadPath))
                return null;

            Log("=== Starting colorimeter detection ===");

            // First, try to get actual device list using -l flag (list instruments)
            // This is more reliable than parsing help text
            var listResult = await RunSpotreadCommandAsync("-l", TimeSpan.FromSeconds(10), cancellationToken);
            if (listResult != null)
            {
                Log($"spotread -l output: {listResult}");

                // Parse actual connected device from list
                var deviceInfo = ParseDeviceListOutput(listResult);
                if (deviceInfo != null)
                {
                    Log($"Detected device from -l: {deviceInfo.Model}");
                    return deviceInfo;
                }
            }

            // Fallback: try just -? to get help which shows connected devices
            var probeResult = await RunSpotreadCommandAsync("-?", TimeSpan.FromSeconds(10), cancellationToken);
            if (probeResult != null)
            {
                Log($"spotread -? output: {probeResult}");

                // Look for device identification in the probe output
                var deviceInfo = ParseDeviceListOutput(probeResult);
                if (deviceInfo != null)
                {
                    Log($"Detected device from probe: {deviceInfo.Model}");
                    return deviceInfo;
                }

                // If no errors, assume device is present
                if (!probeResult.Contains("No colorimeter", StringComparison.OrdinalIgnoreCase) &&
                    !probeResult.Contains("Error", StringComparison.OrdinalIgnoreCase) &&
                    !probeResult.Contains("failed", StringComparison.OrdinalIgnoreCase))
                {
                    Log("No explicit errors - assuming device present");
                    return new ColorimeterInfo
                    {
                        Model = "Colorimeter Detected",
                        IsHdrCapable = true // Assume capable, actual capability tested during measurement
                    };
                }
            }

            Log("No colorimeter detected");
            return null;
        }

        /// <summary>
        /// Runs a spotread command and returns the combined output.
        /// </summary>
        private async Task<string?> RunSpotreadCommandAsync(string args, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_spotreadPath))
                return null;

            var psi = new ProcessStartInfo(_spotreadPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return null;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return null;
                }

                string output, error;
                try
                {
                    output = await stdoutTask;
                    error = await stderrTask;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                return output + "\n" + error;
            }
            catch (Exception ex)
            {
                Log($"Error running spotread {args}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses device information from spotread output.
        /// </summary>
        private static ColorimeterInfo? ParseDeviceListOutput(string output)
        {
            // Common colorimeter patterns in ArgyllCMS output
            var devicePatterns = new[]
            {
                // X-Rite devices
                (@"i1\s*Display\s*(?:Pro|Plus|3)?", true),
                (@"i1\s*Pro\s*\d*", true),
                (@"ColorMunki\s*(?:Display|Design|Photo)?", true),
                // Datacolor devices
                (@"Spyder\s*(?:\d+|X|X2)?(?:\s*(?:Pro|Express|Elite))?", false),
                (@"Spyder\s*\w+", false),
                // Others
                (@"Huey\s*(?:Pro)?", false),
                (@"DTP94", false),
                (@"Eye-One", true),
                (@"i1 Studio", true),
            };

            foreach (var (pattern, isHdrCapable) in devicePatterns)
            {
                var match = Regex.Match(output, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return new ColorimeterInfo
                    {
                        Model = match.Value.Trim(),
                        IsHdrCapable = isHdrCapable
                    };
                }
            }

            // Also look for USB device strings that indicate a colorimeter is present
            if (output.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
                (output.Contains("HID", StringComparison.OrdinalIgnoreCase) ||
                 output.Contains("Instrument", StringComparison.OrdinalIgnoreCase)))
            {
                return new ColorimeterInfo
                {
                    Model = "USB Colorimeter",
                    IsHdrCapable = false
                };
            }

            return null;
        }

        /// <summary>
        /// Finds the spotread executable.
        /// </summary>
        private string? FindSpotread()
        {
            // Check configured path first
            string spotreadPath = Path.Combine(_argyllBinPath, "spotread.exe");
            if (File.Exists(spotreadPath))
                return spotreadPath;

            // Check without .exe (Unix compatibility)
            spotreadPath = Path.Combine(_argyllBinPath, "spotread");
            if (File.Exists(spotreadPath))
                return spotreadPath;

            // Use unified path finder for comprehensive search
            string? binPath = ArgyllPathFinder.FindArgyllBinPath();
            if (binPath != null)
            {
                spotreadPath = Path.Combine(binPath, "spotread.exe");
                if (File.Exists(spotreadPath))
                    return spotreadPath;

                // Unix compatibility
                spotreadPath = Path.Combine(binPath, "spotread");
                if (File.Exists(spotreadPath))
                    return spotreadPath;
            }

            return null;
        }

        private void RaiseStatusChanged(ColorimeterStatus status, string message)
        {
            StatusChanged?.Invoke(this, new ColorimeterStatusEventArgs(status, message));
        }

        /// <summary>
        /// Gets the path to the ArgyllCMS USB driver installer if available.
        /// </summary>
        private static string GetUsbDriverInstallerPath()
        {
            // Check our downloaded ArgyllCMS first
            string localArgyllDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HDRGammaController", "Argyll");

            string installerPath = Path.Combine(localArgyllDir, "usb", "ArgyllCMS_install_USB.exe");
            if (File.Exists(installerPath))
                return installerPath;

            // Check standard installation paths
            var searchPaths = new[]
            {
                @"C:\Program Files\ArgyllCMS",
                @"C:\Program Files (x86)\ArgyllCMS",
                @"C:\Program Files\Argyll_V3.3.0",
                @"C:\ArgyllCMS"
            };

            foreach (var basePath in searchPaths)
            {
                installerPath = Path.Combine(basePath, "usb", "ArgyllCMS_install_USB.exe");
                if (File.Exists(installerPath))
                    return installerPath;
            }

            return string.Empty;
        }

        public void Dispose()
        {
            // Nothing to dispose currently, but implementing for future use
        }
    }

    /// <summary>
    /// Information about a connected colorimeter.
    /// </summary>
    public class ColorimeterInfo
    {
        /// <summary>
        /// Colorimeter model name (e.g., "i1 Display Plus").
        /// </summary>
        public required string Model { get; init; }

        /// <summary>
        /// Whether this colorimeter supports HDR measurement modes.
        /// </summary>
        public bool IsHdrCapable { get; init; }

        /// <summary>
        /// Serial number if available.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// Firmware version if available.
        /// </summary>
        public string? FirmwareVersion { get; init; }

        public override string ToString() => Model;
    }

    /// <summary>
    /// Colorimeter connection status.
    /// </summary>
    public enum ColorimeterStatus
    {
        /// <summary>Status not yet determined.</summary>
        Unknown,

        /// <summary>ArgyllCMS/spotread not found.</summary>
        NotFound,

        /// <summary>Searching for connected colorimeter.</summary>
        Searching,

        /// <summary>No colorimeter connected.</summary>
        NotConnected,

        /// <summary>Colorimeter connected and ready.</summary>
        Ready,

        /// <summary>Currently taking a measurement.</summary>
        Measuring,

        /// <summary>Error occurred.</summary>
        Error
    }

    /// <summary>
    /// Event args for measurement completion.
    /// </summary>
    public class MeasurementEventArgs : EventArgs
    {
        public MeasurementResult Result { get; }

        public MeasurementEventArgs(MeasurementResult result)
        {
            Result = result;
        }
    }

    /// <summary>
    /// Event args for measurement errors.
    /// </summary>
    public class MeasurementErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public ColorPatch? Patch { get; }

        public MeasurementErrorEventArgs(string message, ColorPatch? patch = null)
        {
            Message = message;
            Patch = patch;
        }
    }

    /// <summary>
    /// Event args for colorimeter status changes.
    /// </summary>
    public class ColorimeterStatusEventArgs : EventArgs
    {
        public ColorimeterStatus Status { get; }
        public string Message { get; }

        public ColorimeterStatusEventArgs(ColorimeterStatus status, string message)
        {
            Status = status;
            Message = message;
        }
    }

    /// <summary>
    /// Exception thrown when colorimeter communication fails due to USB driver issues.
    /// This indicates the ArgyllCMS USB drivers need to be installed.
    /// </summary>
    public class UsbDriverException : Exception
    {
        public UsbDriverException(string message) : base(message) { }
        public UsbDriverException(string message, Exception innerException) : base(message, innerException) { }
    }
}
