using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
            // 1. Search in local directory
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dispwin.exe");
            if (File.Exists(local)) return local;
            
            // 2. Search in our local app data directory
            string localAppData = Path.Combine(LocalArgyllDir, "bin", "dispwin.exe");
            if (File.Exists(localAppData)) return localAppData;
            
            // 3. Search in PATH
            if (IsInPath("dispwin.exe")) return "dispwin.exe";
            
            // 4. Search common locations (DisplayCAL, ArgyllCMS)
            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\DisplayCAL\Argyll\bin\dispwin.exe",
                @"C:\Program Files\DisplayCAL\Argyll\bin\dispwin.exe",
                @"C:\Argyll\bin\dispwin.exe",
                @"C:\Program Files (x86)\Argyll\bin\dispwin.exe"
            };
            
            foreach (var path in commonPaths)
            {
                if (File.Exists(path)) return path;
            }
            
            // 5. Search DisplayCAL's AppData download location
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
                            Console.WriteLine($"DispwinRunner: Found dispwin at {dispwinPath}");
                            return dispwinPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DispwinRunner: Error searching AppData: {ex.Message}");
            }
            
            return string.Empty;
        }
        
        private bool IsInPath(string fileName)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return false;
            
            foreach (var p in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (File.Exists(Path.Combine(p, fileName))) return true;
                }
                catch {}
            }
            return false;
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
        /// </summary>
        public async Task<bool> DownloadArgyllAsync()
        {
            try
            {
                Console.WriteLine($"DispwinRunner: Downloading ArgyllCMS from {ArgyllDownloadUrl}");
                
                // Create temp directory for download
                string tempDir = Path.Combine(Path.GetTempPath(), "HDRGammaController_ArgyllDownload");
                Directory.CreateDirectory(tempDir);
                string zipPath = Path.Combine(tempDir, "argyll.zip");
                
                // Download
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var response = await client.GetAsync(ArgyllDownloadUrl);
                    response.EnsureSuccessStatusCode();
                    
                    using (var fs = new FileStream(zipPath, FileMode.Create))
                    {
                        await response.Content.CopyToAsync(fs);
                    }
                }
                
                Console.WriteLine($"DispwinRunner: Downloaded to {zipPath}");
                
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
                
                // Cleanup temp
                try { Directory.Delete(tempDir, true); } catch {}
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DispwinRunner: DownloadArgyllAsync failed: {ex}");
                throw;
            }
        }

        public void ApplyGamma(MonitorInfo monitor, GammaMode mode, double whiteLevel)
        {
            Console.WriteLine($"DispwinRunner.ApplyGamma: monitor={monitor.DeviceName}, mode={mode}, whiteLevel={whiteLevel}");
            
            if (!EnsureConfigured())
            {
                Console.WriteLine("DispwinRunner.ApplyGamma: Not configured, aborting.");
                return;
            }

            // 1. Generate LUT (ensure we use the correct white level)
            double[] lut = LutGenerator.GenerateLut(mode, whiteLevel);
            Console.WriteLine($"DispwinRunner.ApplyGamma: Generated LUT with {lut.Length} entries");

            // 2. Create .cal file content
            string calContent = GenerateCalContent(lut);
            string tempFile = Path.GetTempFileName();
            string calFile = Path.ChangeExtension(tempFile, ".cal");
            Console.WriteLine($"DispwinRunner.ApplyGamma: Created temp file={calFile}");
            
            try
            {
                File.WriteAllText(calFile, calContent);
                
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
                try {
                    if (File.Exists(calFile)) File.Delete(calFile);
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                } catch {}
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

        private string GenerateCalContent(double[] lut)
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
            sb.AppendLine($"NUMBER_OF_SETS {lut.Length}");
            sb.AppendLine("BEGIN_DATA");
            
            for(int i=0; i<lut.Length; i++)
            {
                double input = i / (double)(lut.Length - 1);
                double val = lut[i];
                // Format: Input R G B (same value for all channels = grey ramp)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, 
                    "{0:G6} {1:G6} {1:G6} {1:G6}", input, val));
            }
            
            sb.AppendLine("END_DATA");
            return sb.ToString();
        }
    }
}
