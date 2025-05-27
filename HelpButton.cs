using ArcGIS.Desktop.Framework.Contracts;
using System.Diagnostics;

internal class HelpButton : Button
{
    protected override void OnClick()
    {
        // The URL of your online help/documentation
        string helpUrl = "https://github.com/yourusername/your-addon-readme";

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
