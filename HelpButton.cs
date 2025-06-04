using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using TraceNetwork;

namespace TraceNetwork
{
    public class HelpButton : Button
    {
        protected override void OnClick()
        {
            // The URL of your online help/documentation
            string helpUrl = "https://github.com/enielsen93/Trace-Network/blob/master/README.md";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = helpUrl,
                    UseShellExecute = true // important to open URL in default browser
                });
            }
            catch (System.Exception ex)
            {
                // Optional: log or show a message box that help failed to open
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show($"Unable to open help: {ex.Message}");
            }
        }
    }

}