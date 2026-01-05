using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;

namespace HDRGammaController.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public bool IsUpdateAvailable { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    public class UpdateService
    {
        private const string RepoOwner = "davidtorcivia";
        private const string RepoName = "win11hdr-gamma-adjuster";
        private const string CurrentVersion = "1.0.0"; // Should match AssemblyInfo

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            var result = new UpdateInfo();
            
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HDRGammaController", "1.0"));
                
                // Get the release tagged 'latest' (our Auto-Build)
                // Note: The standard 'releases/latest' endpoint ONLY returns stable releases, not prereleases.
                // Since our auto-build is a prerelease tagged 'latest', we should fetch by tag.
                string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/latest";
                
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    // Fallback to latest stable release
                    url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                    response = await client.GetAsync(url);
                }
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    
                    string tagName = root.GetProperty("tag_name").GetString() ?? "";
                    string htmlUrl = root.GetProperty("html_url").GetString() ?? "";
                    string publishedAtStr = root.GetProperty("published_at").GetString() ?? "";
                    DateTime publishedAt = DateTime.TryParse(publishedAtStr, out var date) ? date : DateTime.MinValue;
                    
                    result.Version = tagName;
                    result.ReleaseUrl = htmlUrl;
                    result.PublishedAt = publishedAt;
                    
                    // Simple check: If the remote build is newer than our build time? 
                    // Or since we don't have auto-incrementing versions yet, simply notify if the release is very recent?
                    // For now, let's assume we want to notify if the release is newer than 24 hours AND we haven't seen it?
                    // Actually, best way with "latest" tag is to check published_at > BuildDate.
                    
                    DateTime buildDate = GetLinkerTime(Assembly.GetExecutingAssembly());
                    
                    // Allow 1 hour buffer for build server time differences
                    if (publishedAt > buildDate.AddHours(1))
                    {
                        result.IsUpdateAvailable = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update check failed: {ex.Message}");
            }
            
            return result;
        }
        
        // Helper to get build timestamp from assembly
        private static DateTime GetLinkerTime(Assembly assembly)
        {
            const string BuildVersionMetadataPrefix = "+build";
            
            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute?.InformationalVersion != null)
            {
                var value = attribute.InformationalVersion;
                var index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0)
                {
                     // Use semantic versioning metadata if available? 
                     // Currently we don't stamp it.
                }
            }
            
            // Fallback to file creation time as a rough proxy for local dev, 
            // but for CI builds we might need a better way. 
            // Return a default date if we can't determine.
            try
            {
                string location = assembly.Location;
                if (!string.IsNullOrEmpty(location))
                {
                    return System.IO.File.GetLastWriteTimeUtc(location);
                }
            }
            catch {}
            
            return DateTime.MinValue;
        }
    }
}
