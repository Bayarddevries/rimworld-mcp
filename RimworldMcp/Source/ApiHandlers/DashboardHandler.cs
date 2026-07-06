using System;
using System.IO;
using System.Net;
using System.Text;

namespace RimworldMcp
{
    /// <summary>
    /// Serves the full Colony Command dashboard HTML.
    /// Reads dashboard.html from the mod's Assemblies folder.
    /// </summary>
    public static class DashboardHandler
    {
        private static string _cachedHtml = null;
        private static DateTime _lastRead = DateTime.MinValue;

        public static string ServeDashboard(HttpListenerRequest req)
        {
            // Re-read every 30s in case the file is updated
            if (_cachedHtml == null || (DateTime.Now - _lastRead).TotalSeconds > 30)
            {
                try
                {
                    // Look for dashboard.html next to the DLL
                    string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    string dir = Path.GetDirectoryName(dllPath);
                    string htmlPath = Path.Combine(dir, "dashboard.html");
                    if (File.Exists(htmlPath))
                    {
                        _cachedHtml = File.ReadAllText(htmlPath);
                        _lastRead = DateTime.Now;
                    }
                    else
                    {
                        _cachedHtml = "<html><body><h1>Dashboard file not found</h1><p>Place dashboard.html next to RimworldMcp.dll</p></body></html>";
                    }
                }
                catch (Exception ex)
                {
                    _cachedHtml = $"<html><body><h1>Error loading dashboard</h1><p>{ex.Message}</p></body></html>";
                }
            }
            return _cachedHtml;
        }
    }
}
