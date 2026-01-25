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
        private readonly object _lock = new();

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
                // Poll until initialization completes
                while (true)
                {
                    await Task.Delay(50, cancellationToken);
                    lock (_lock)
                    {
                        if (!_isInitializing)
                            return _isInitialized && _connectedColorimeter != null;
                    }
                }
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

            // Build spotread arguments
            // -v       Verbose output
            // -d N     Display number (1-based)
            // -Y n     Disable ambient light (measure display only)
            // -H       HDR mode (higher brightness measurement)
            // -e       Emit measured value to stdout
            // -x       Exit after measurement (don't wait for keypress)
            var args = $"-v -d{_displayIndex} -Y n -e -x";
            if (hdrMode)
                args += " -H";

            var psi = new ProcessStartInfo(_spotreadPath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };

            using var process = new Process { StartInfo = psi };
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Send a newline to trigger measurement (spotread waits for Enter)
            await process.StandardInput.WriteLineAsync("");

            // Wait for completion with timeout
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

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"spotread failed (exit code {process.ExitCode}): {error}");
            }

            // Parse XYZ values from output
            return ParseSpotreadOutput(output);
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
        private static CieXyz ParseSpotreadOutput(string output)
        {
            // Try to find XYZ values in the output
            // Pattern: "XYZ:" followed by three floating-point numbers
            var xyzPattern = new Regex(
                @"XYZ:\s*(-?\d+\.?\d*)\s+(-?\d+\.?\d*)\s+(-?\d+\.?\d*)",
                RegexOptions.IgnoreCase);

            var match = xyzPattern.Match(output);
            if (!match.Success)
            {
                throw new InvalidOperationException(
                    $"Failed to parse XYZ values from spotread output:\n{output}");
            }

            double x = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double y = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            double z = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

            return new CieXyz(x, y, z);
        }

        /// <summary>
        /// Detects connected colorimeters using spotread.
        /// </summary>
        private async Task<ColorimeterInfo?> DetectColorimeterAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_spotreadPath))
                return null;

            // Use -? to get help/device list without taking a measurement
            var psi = new ProcessStartInfo(_spotreadPath, "-?")
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

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(); } catch { }
                    return null;
                }

                // Parse device info from output
                // Look for patterns like "i1 Display" or "i1Display" or device names
                string combined = output + "\n" + error;

                // Common colorimeter patterns
                var devicePatterns = new[]
                {
                    @"i1\s*Display\s*(?:Pro|Plus|3)?",
                    @"i1\s*Pro\s*\d*",
                    @"ColorMunki\s*\w*",
                    @"Spyder\s*\d*\w*",
                    @"Huey\s*\w*"
                };

                foreach (var pattern in devicePatterns)
                {
                    var match = Regex.Match(combined, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return new ColorimeterInfo
                        {
                            Model = match.Value.Trim(),
                            IsHdrCapable = match.Value.Contains("i1", StringComparison.OrdinalIgnoreCase)
                        };
                    }
                }

                // If spotread ran without error about no device, assume something is connected
                if (!combined.Contains("No colorimeter", StringComparison.OrdinalIgnoreCase) &&
                    !combined.Contains("instrument error", StringComparison.OrdinalIgnoreCase))
                {
                    return new ColorimeterInfo
                    {
                        Model = "Unknown Colorimeter",
                        IsHdrCapable = false
                    };
                }

                return null;
            }
            catch
            {
                return null;
            }
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

            // Search common locations
            var searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HDRGammaController", "Argyll", "bin"),
                @"C:\Program Files\Argyll\bin",
                @"C:\Program Files (x86)\Argyll\bin",
                @"C:\Program Files\DisplayCAL\Argyll\bin",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DisplayCAL", "dl", "Argyll_V3.3.0", "bin")
            };

            foreach (var path in searchPaths)
            {
                spotreadPath = Path.Combine(path, "spotread.exe");
                if (File.Exists(spotreadPath))
                    return spotreadPath;
            }

            return null;
        }

        private void RaiseStatusChanged(ColorimeterStatus status, string message)
        {
            StatusChanged?.Invoke(this, new ColorimeterStatusEventArgs(status, message));
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
}
