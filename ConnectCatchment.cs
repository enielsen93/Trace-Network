/*using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using TraceNetwork.Network;


namespace TraceNetwork
{
    internal class ConnectCatchment : MapTool
    {
        public ConnectCatchment()
        {
            IsSketchTool = true;  // important: this means we respond to map clicks
            SketchType = SketchGeometryType.Point;
            SketchOutputMode = SketchOutputMode.Map;
        }

        protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
        {
            return QueuedTask.Run(() =>
            {
                if (geometry is MapPoint point)
                {
                    var nearest = NetworkService.FindNearestNode(point, 20);
                    if (nearest == null)
                    {
                        Debug.WriteLine("No nearby node found.");
                        return true;
                    }

                    string caption = CatchmentLayerComboBox.Current.SelectedItem.ToString();
                    var map = MapView.Active?.Map;

                    bool found = false;
                    FeatureLayer selectedCatchments = null;

                    // Run inside QueuedTask for safety

                    foreach (var layer in map.Layers)
                    {
                        if (found) break;
                        string[] parts = caption.Split('\\');

                        if (parts.Length == 2)
                        {
                            if (layer is GroupLayer groupLayer)
                            {
                                foreach (var subLayer in groupLayer.Layers.OfType<FeatureLayer>())
                                {
                                    if (groupLayer.Name == parts[0] && subLayer.Name == parts[1])
                                    {
                                        selectedCatchments = subLayer;
                                        found = true;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (parts.Length == 1)
                        {
                            if (layer is FeatureLayer featureLayer &&
                                featureLayer.ShapeType == esriGeometryType.esriGeometryPolygon &&
                                layer.Name == parts[0])
                            {
                                selectedCatchments = featureLayer;
                                found = true;
                                break;
                            }
                        }
                    }

                    // Read msm_Catchments
                    var selectedIDs = selectedCatchments.GetSelection().GetObjectIDs().ToList();

                    List<String> catchmentList = new List<String>();

                    using (var catchmentCursor = selectedCatchments.Search(null))
                    {
                        while (catchmentCursor.MoveNext())
                        {
                            using (var row = (Feature)catchmentCursor.Current)
                            {
                                catchmentList.Add(row["muid"]?.ToString());
                            }
                        }
                    }

                    // Get SQLITE PATH
                    var dataConn = selectedCatchments.GetDataConnection() as CIMSqlQueryDataConnection;
                    string dbPath = null;

                    var parts = dataConn.WorkspaceConnectionString
                        .Split(';')
                        .Select(part => part.Split('='))
                        .Where(p => p.Length == 2 && p[0].Equals("DATABASE", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (parts.Count > 0)
                        dbPath = parts[0][1];

                    SqlitePath = dbPath;
                    Debug.WriteLine($"SQLite file path: {dbPath}");

                    // Read HparA
                    string connString = $"Data Source={dbPath};";

                    // Read CatchCon
                    SQLitePCL.Batteries_V2.Init();

                    {
                        using var conn = new SqliteConnection(connString);
                        conn.Open();

                        var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT CATCHID, NODEID FROM msm_CatchCon;";
                        using var reader = cmd.ExecuteReader();

                        while (reader.Read())
                        {
                            var id = reader.GetString(0);
                            var nodeid = reader.GetString(1);
                            if (Catchments.ContainsKey(id))
                            {
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(nodeid))
                                    Catchments[id].NodeId = nodeid;
                            }

                        }
                    }

                    return true;
            });
        }
    }
}
*/