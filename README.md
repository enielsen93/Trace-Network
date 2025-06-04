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

   The add-in will appear under the **Trace Network** tab or wherever you placed your custom buttons/toolbars.

---

**Optional tips:**

- If you want to remove the add-in later, just delete the `.esriAddInX` file from the Addins folder.
- For more info, see [ESRI’s documentation on add-ins](https://pro.arcgis.com/en/pro-app/latest/sdk/overview/what-are-add-ins-.htm).

---

## How to Use

**Trace Network** is an ArcGIS Pro add-in designed to trace a MIKE+ Sewer Network.  
It is intended to be used **with** the `02) Display MIKE+.pyt` tool available here:  
[https://github.com/enielsen93/MIKE-Urban-Tools](https://github.com/enielsen93/MIKE-Urban-Tools)

---

### To Trace Network

1. Run **2.3 Display MIKE+** in ArcGIS Pro.
2. Open the **Trace Network** dockpane from the Ribbon.
3. Select the **Group Layer** under *GroupLayer*.
4. Choose **Trace Upstream**.
5. Click on the map near a node — the add-in will select all nodes, links, and catchments upstream of the selected node.

---

### To Summarize Catchments

1. Select catchments in ArcGIS Pro.  
   > **Important:** There is a limit to the number of catchments you can select.
2. Open the **Trace Network** dockpane in the Ribbon.
3. Click **Summarize Catchments**.

A message box will appear displaying the **total area**, **total impervious area**, and **total reduced area** of all selected catchments.

---

