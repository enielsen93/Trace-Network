using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows; // removed to avoid conflicts with ArcGIS Pro dialogs
using System.IO.Compression;
using Microsoft.Win32;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Internal.Catalog;

namespace TraceNetwork
{
    public class InstallMikeToolboxButton : Button
    {
        protected override async void OnClick()
        {
            
            const string downloadUrl = "https://github.com/enielsen93/MIKE-Toolbox/archive/refs/heads/main.zip";
            
            var result = ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show("Download MIKE Toolbox from GitHub to your Downloads folder and add toolboxes to ArcGIS Pro?",
                "Install MIKE Toolbox",
                MessageBoxButton.YesNo);
            
            if (result != MessageBoxResult.Yes)
                return;

            // Determine Downloads folder (user profile + Downloads)
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath))
                Directory.CreateDirectory(downloadsPath);

            var zipPath = Path.Combine(downloadsPath, "MIKE-Toolbox-main.zip");
            var extractPath = Path.Combine(downloadsPath, "MIKE-Toolbox-main");
            
            try
            {
                // Download
                using (var client = new HttpClient())
                {
                    //ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show("Starting download. This may take a while.", "Downloading");
                    using var resp = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();

                    using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await resp.Content.CopyToAsync(fs);
                }
                
                // Extract (overwrite if exists)
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractPath);
                
                // Find toolbox files
                var tbxFiles = Directory.GetFiles(extractPath, "*.tbx", SearchOption.AllDirectories).ToList();
                var pytFiles = Directory.GetFiles(extractPath, "*.pyt", SearchOption.AllDirectories).ToList();

                var added = new List<string>();
                var failed = new List<string>();

                // Determine a sensible folder to add: use the common directory among all found toolbox files
                string folderToAdd;
                var allFiles = tbxFiles.Concat(pytFiles).ToList();
                // local helper to compute common prefix
                string CommonPathPrefix(string a, string b)
                {
                    if (string.IsNullOrEmpty(a)) return b;
                    if (string.IsNullOrEmpty(b)) return a;
                    var aParts = a.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
                    var bParts = b.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
                    var len = Math.Min(aParts.Length, bParts.Length);
                    var parts = new List<string>();
                    for (int i = 0; i < len; i++)
                    {
                        if (!string.Equals(aParts[i], bParts[i], StringComparison.OrdinalIgnoreCase)) break;
                        parts.Add(aParts[i]);
                    }
                    return parts.Count == 0 ? null : string.Join(Path.DirectorySeparatorChar.ToString(), parts);
                }

                if (allFiles.Count == 0)
                {
                    folderToAdd = extractPath;
                }
                else
                {
                    var dirs = allFiles.Select(f => Path.GetDirectoryName(f)).Where(d => d != null).ToList();
                    string common = dirs.First();
                    foreach (var d in dirs)
                        common = CommonPathPrefix(common, d);
                    folderToAdd = common ?? extractPath;
                }
                
                // Create a FolderConnectionProjectItem and add it to the project on the QueuedTask
                try
                {
                    await QueuedTask.Run(() =>
                    {
                        if (!Path.EndsInDirectorySeparator(folderToAdd))
                            folderToAdd += Path.DirectorySeparatorChar;

                        var folderLocationItem = ItemFactory.Instance.Create(folderToAdd) as IProjectItem;
                        if (folderLocationItem == null)
                            throw new InvalidOperationException($"Unable to create project item for '{folderToAdd}'.");

                        var currentProject = Project.Current ?? throw new InvalidOperationException("No open ArcGIS Pro project (APRX) found.");
                        currentProject.AddItem(folderLocationItem);
                    });

                    added.Add(folderToAdd);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to add folder connection {folderToAdd}: {ex}");
                    failed.Add(folderToAdd);
                }

                var summary = $"Download and extraction completed.\nFound {tbxFiles.Count + pytFiles.Count} toolbox files.\nAdded: {added.Count}\nFailed: {failed.Count}";
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(summary, "MIKE Toolbox Install");
                
            }
            catch (Exception ex)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show($"Installation failed: {ex.Message}");
            }
            
        }
    }
}
