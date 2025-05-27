## Installation

1. **Download the Add-In File (`.esriAddInX`)**

   Download the latest release of the add-in from the [Releases](https://github.com/enielsen93/Trace-Network/releases) page.

2. **Install the Add-In**

   - **Option A: Double-click the `.esriAddInX` file**
     
     This will automatically open ArcGIS Pro and install the add-in.

   - **Option B: Manual installation**
     
     Copy the `.esriAddInX` file to the ArcGIS Pro add-ins folder:

     ```
     %LOCALAPPDATA%\ESRI\ArcGISPro\Addins
     ```

     You can paste this path into File Explorer and place the file there.

3. **Restart ArcGIS Pro** (if it was open during installation).

4. **Use the Add-In**

   The add-in will appear under the **Add-In** tab or wherever you placed your custom buttons/toolbars.

---

**Optional tips:**

- If you want to remove the add-in later, just delete the `.esriAddInX` file from the Addins folder.
- For more info, see [ESRI’s documentation on add-ins](https://pro.arcgis.com/en/pro-app/latest/sdk/overview/what-are-add-ins-.htm).

---

### Why this works

- Most users prefer **simple “double-click install”** steps.
- Including the manual folder path is a lifesaver for those who want more control or have ArcGIS Pro open.
- Clear, bullet-point format makes it skimmable and easy to follow.
- Link to releases and official docs adds credibility and helps with troubleshooting.

---

Want me to draft you a full README template including this section? Or help with how to automate your release packaging on GitHub?
