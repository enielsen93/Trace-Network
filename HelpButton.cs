using System;
using System.Diagnostics;
using System.Windows;
using ArcGIS.Desktop.Framework.Contracts;

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
                    UseShellExecute = true // open URL in default browser
                });
            }
            catch (System.Exception ex)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show($"Unable to open help: {ex.Message}");
            }
        }
    }
}
