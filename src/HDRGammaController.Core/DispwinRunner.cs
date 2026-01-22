using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;

namespace HDRGammaController.Core
{
    public class DispwinRunner
    {
        private string _dispwinPath;
        
        // URL for ArgyllCMS Windows binaries
        private const string ArgyllDownloadUrl = "https://www.argyllcms.com/Argyll_V3.3.0_win64_exe.zip";
        private const string ArgyllVersion = "Argyll_V3.3.0";
        // SHA256 checksum for integrity verification (update when ArgyllCMS version changes)
        private const string ArgyllExpectedSha256 = ""; // Empty = skip verification (set to actual hash when known)
        
        private static readonly string LocalArgyllDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HDRGammaController", "Argyll");

        public DispwinRunner()
        {
             _dispwinPath = FindDispwin();
             Console.WriteLine($"DispwinRunner: Initialized with path='{_dispwinPath}'");
        }
        
        private string FindDispwin()
        {
            // SECURITY: Do NOT search current directory first (DLL/EXE planting risk)
            // Only search controlled/admin-protected locations

            // 1. Search in our local app data directory (controlled by this app)
            string localAppData = Path.Combine(LocalArgyllDir, "bin", "dispwin.exe");
            if (File.Exists(localAppData))
            {
                Console.WriteLine($"DispwinRunner: Found dispwin in LocalAppData: {localAppData}");
                return localAppData;
            }

            // 2. Search common Program Files locations (admin-protected)
            var trustedPaths = new[]
            {
                @"C:\Program Files\DisplayCAL\Argyll\bin\dispwin.exe",
                @"C:\Program Files (x86)\DisplayCAL\Argyll\bin\dispwin.exe",
                @"C:\Program Files\Argyll\bin\dispwin.exe",
                @"C:\Program Files (x86)\Argyll\bin\dispwin.exe",
                @"C:\Argyll\bin\dispwin.exe"  // Less secure but common install location
            };

            foreach (var path in trustedPaths)
            {
                if (File.Exists(path))
                {
                    Console.WriteLine($"DispwinRunner: Found dispwin at trusted path: {path}");
                    return path;
                }
            }

            // 3. Search DisplayCAL's AppData download location (user-controlled but common)
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string displayCalDir = Path.Combine(appData, "DisplayCAL", "dl");
                if (Directory.Exists(displayCalDir))
                {
                    // Look for Argyll_V* directories
                    foreach (var argyllDir in Directory.GetDirectories(displayCalDir, "Argyll_*"))
                    {
                        string dispwinPath = Path.Combine(argyllDir, "bin", "dispwin.exe");
                        if (File.Exists(dispwinPath))
                        {
                            Console.WriteLine($"DispwinRunner: Found dispwin in DisplayCAL dir: {dispwinPath}");
                            return dispwinPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DispwinRunner: Error searching AppData: {ex.Message}");
            }

            // 4. Search in PATH (last resort, validates full path before returning)
            string pathResult = FindInPath("dispwin.exe");
            if (!string.IsNullOrEmpty(pathResult))
            {
                Console.WriteLine($"DispwinRunner: Found dispwin in PATH: {pathResult}");
                return pathResult;
            }

            return string.Empty;
        }
        
        /// <summary>
        /// Searches PATH for a file and returns its full path, or empty string if not found.
        /// </summary>
        private string FindInPath(string fileName)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return string.Empty;

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string fullPath = Path.Combine(dir, fileName);
                    if (File.Exists(fullPath))
                    {
                        // Return full resolved path, not just the filename
                        return Path.GetFullPath(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DispwinRunner: Error checking PATH entry '{dir}': {ex.Message}");
                }
            }
            return string.Empty;
        }

        public bool EnsureConfigured()
        {
            if (!string.IsNullOrEmpty(_dispwinPath)) return true;
            
            // Try detection again
            _dispwinPath = FindDispwin();
            if (!string.IsNullOrEmpty(_dispwinPath)) return true;

            // Offer to auto-download
            var result = MessageBox.Show(
                "ArgyllCMS 'dispwin.exe' not found.\n\n" +
                "This application requires ArgyllCMS to apply gamma tables.\n" +
                "Would you like to download ArgyllCMS automatically?\n\n" +
                "(This will download ~15MB from argyllcms.com)",
                "HDR Gamma Controller - Missing Dependency",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Run download async but block UI with a wait dialog
                bool success = false;
                try
                {
                    var downloadTask = DownloadArgyllAsync();
                    // Simple blocking wait - in production would show progress dialog
                    downloadTask.Wait();
                    success = downloadTask.Result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DispwinRunner: Download failed: {ex.Message}");
                    MessageBox.Show($"Download failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                
                if (success)
                {
                    _dispwinPath = FindDispwin();
                    if (!string.IsNullOrEmpty(_dispwinPath))
                    {
                        MessageBox.Show("ArgyllCMS downloaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        return true;
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Downloads and extracts ArgyllCMS binaries to LocalApplicationData.
        /// Verifies SHA256 checksum if configured.
        /// </summary>
        public async Task<bool> DownloadArgyllAsync()
        {
            string? tempDir = null;
            try
            {
                Console.WriteLine($"DispwinRunner: Downloading ArgyllCMS from {ArgyllDownloadUrl}");

                // Create temp directory with unique name to prevent collisions
                tempDir = Path.Combine(Path.GetTempPath(), $"HDRGammaController_ArgyllDownload_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                string zipPath = Path.Combine(tempDir, "argyll.zip");

                // Download
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var response = await client.GetAsync(ArgyllDownloadUrl);
                    response.EnsureSuccessStatusCode();

                    using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }

                Console.WriteLine($"DispwinRunner: Downloaded to {zipPath}");

                // SECURITY: Verify SHA256 checksum if configured
                if (!string.IsNullOrEmpty(ArgyllExpectedSha256))
                {
                    string actualHash = ComputeSha256(zipPath);
                    if (!actualHash.Equals(ArgyllExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"DispwinRunner: SECURITY WARNING - Hash mismatch! Expected: {ArgyllExpectedSha256}, Got: {actualHash}");
                        throw new InvalidOperationException(
                            $"Downloaded file failed integrity check.\nExpected SHA256: {ArgyllExpectedSha256}\nActual SHA256: {actualHash}");
                    }
                    Console.WriteLine($"DispwinRunner: SHA256 checksum verified: {actualHash}");
                }
                else
                {
                    Console.WriteLine("DispwinRunner: WARNING - No checksum configured, skipping verification");
                }
                
                // Extract
                Directory.CreateDirectory(LocalArgyllDir);
                
                // The ZIP contains Argyll_V3.3.0/bin/dispwin.exe
                // We want to extract to LocalArgyllDir so it becomes LocalArgyllDir/bin/dispwin.exe
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        // Strip the first directory component (Argyll_V3.3.0/)
                        string entryPath = entry.FullName;
                        if (entryPath.StartsWith(ArgyllVersion + "/") || entryPath.StartsWith(ArgyllVersion + "\\"))
                        {
                            entryPath = entryPath.Substring(ArgyllVersion.Length + 1);
                        }
                        
                        if (string.IsNullOrEmpty(entryPath)) continue;
                        
                        string destPath = Path.Combine(LocalArgyllDir, entryPath);
                        
                        if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                        {
                            // Directory
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            // File
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }
                }
                
                Console.WriteLine($"DispwinRunner: Extracted to {LocalArgyllDir}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DispwinRunner: DownloadArgyllAsync failed: {ex}");
                throw;
            }
            finally
            {
                // Always cleanup temp directory
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); }
                    catch (Exception ex) { Console.WriteLine($"DispwinRunner: Failed to cleanup temp dir: {ex.Message}"); }
                }
            }
        }

        /// <summary>
        /// Computes SHA256 hash of a file.
        /// </summary>
        private static string ComputeSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public void ApplyGamma(MonitorInfo monitor, GammaMode mode, double whiteLevel)
        {
            ApplyGamma(monitor, mode, whiteLevel, CalibrationSettings.Default);
        }
        
        public void ApplyGamma(MonitorInfo monitor, GammaMode mode, double whiteLevel, CalibrationSettings calibration)
        {
            Console.WriteLine($"DispwinRunner.ApplyGamma: monitor={monitor.DeviceName}, mode={mode}, whiteLevel={whiteLevel}, hasCalibration={calibration.HasAdjustments}");
            
            if (!EnsureConfigured())
            {
                Console.WriteLine("DispwinRunner.ApplyGamma: Not configured, aborting.");
                return;
            }

            // 1. Generate per-channel LUTs
            var (lutR, lutG, lutB, _) = LutGenerator.GenerateLut(mode, whiteLevel, calibration, monitor.IsHdrActive);
            Console.WriteLine($"DispwinRunner.ApplyGamma: Generated LUTs with {lutR.Length} entries");

            // 2. Create .cal file content
            // SECURITY: Use GUID-based filename to prevent race conditions
            // (GetTempFileName + ChangeExtension creates a race between file creation and use)
            string calContent = GenerateCalContent(lutR, lutG, lutB);
            string calFile = Path.Combine(Path.GetTempPath(), $"HDRGamma_{Guid.NewGuid():N}.cal");
            Console.WriteLine($"DispwinRunner.ApplyGamma: Created temp file={calFile}");

            try
            {
                // Write with exclusive access to prevent tampering
                using (var fs = new FileStream(calFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs))
                {
                    writer.Write(calContent);
                }

                int argIndex = (int)monitor.OutputId + 1;
                string args = $"-d {argIndex} \"{calFile}\"";
                Console.WriteLine($"DispwinRunner.ApplyGamma: Running dispwin with args: {args}");
                RunDispwin(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DispwinRunner.ApplyGamma: Exception: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(calFile)) File.Delete(calFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DispwinRunner.ApplyGamma: Failed to cleanup temp file: {ex.Message}");
                }
            }
        }

        public void ClearGamma(MonitorInfo monitor)
        {
             if (!EnsureConfigured()) return;

             int argIndex = (int)monitor.OutputId + 1; 
             RunDispwin($"-d {argIndex} -c"); 
        }

        private void RunDispwin(string args)
        {
            Console.WriteLine($"DispwinRunner.RunDispwin: Executing '{_dispwinPath}' with args '{args}'");
            try
            {
                var psi = new ProcessStartInfo(_dispwinPath, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var p = Process.Start(psi);
                if (p != null)
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);
                    Console.WriteLine($"DispwinRunner.RunDispwin: Exit code={p.ExitCode}");
                    if (!string.IsNullOrWhiteSpace(stdout))
                        Console.WriteLine($"DispwinRunner.RunDispwin: stdout={stdout}");
                    if (!string.IsNullOrWhiteSpace(stderr))
                        Console.WriteLine($"DispwinRunner.RunDispwin: stderr={stderr}");
                }
                else
                {
                    Console.WriteLine("DispwinRunner.RunDispwin: Process.Start returned null");
                }
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"DispwinRunner.RunDispwin: Exception: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private string GenerateCalContent(double[] lutR, double[] lutG, double[] lutB)
        {
            var sb = new StringBuilder();
            // Argyll CAL format - must start with "CAL" identifier
            sb.AppendLine("CAL    ");
            sb.AppendLine();
            sb.AppendLine("DESCRIPTOR \"HDRGammaController Generated Calibration\"");
            sb.AppendLine("ORIGINATOR \"HDRGammaController\"");
            sb.AppendLine($"CREATED \"{DateTime.Now:ddd MMM dd HH:mm:ss yyyy}\"");
            sb.AppendLine("DEVICE_CLASS \"DISPLAY\"");
            sb.AppendLine("COLOR_REP \"RGB\"");
            sb.AppendLine();
            sb.AppendLine("NUMBER_OF_FIELDS 4");
            sb.AppendLine("BEGIN_DATA_FORMAT");
            sb.AppendLine("RGB_I RGB_R RGB_G RGB_B");
            sb.AppendLine("END_DATA_FORMAT");
            sb.AppendLine();
            sb.AppendLine($"NUMBER_OF_SETS {lutR.Length}");
            sb.AppendLine("BEGIN_DATA");
            
            for(int i=0; i<lutR.Length; i++)
            {
                double input = i / (double)(lutR.Length - 1);
                // Per-channel RGB values
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, 
                    "{0:G6} {1:G6} {2:G6} {3:G6}", input, lutR[i], lutG[i], lutB[i]));
            }
            
            sb.AppendLine("END_DATA");
            return sb.ToString();
        }
    }
}
