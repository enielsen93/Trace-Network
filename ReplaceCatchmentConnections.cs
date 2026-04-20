using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using TraceNetwork.Network;

namespace TraceNetwork
{
    internal class ReplaceCatchmentConnections : MapTool
    {
        public ReplaceCatchmentConnections()
        {
            IsSketchTool = true;
            SketchType = SketchGeometryType.Point;
            SketchOutputMode = SketchOutputMode.Map;
        }

        protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
        {
            return QueuedTask.Run(() =>
            {
                if (geometry is MapPoint point)
                {
                    // Find nearest node (implement your own logic or use NetworkService)
                    var nearest = NetworkService.FindNearestNode(point, 20);
                    if (nearest == null)
                    {
                        Debug.WriteLine("No nearby node found.");
                        return true; // Explicitly return a boolean value
                    }

                    string caption = CatchmentLayerComboBox.Current.SelectedItem.ToString();
                    var map = MapView.Active?.Map;

                    FeatureLayer selectedCatchments = null;
                    foreach (var layer in map.Layers)
                    {
                        string[] parts = caption.Split('\\');
                        if (parts.Length == 2 && layer is GroupLayer groupLayer)
                        {
                            foreach (var subLayer in groupLayer.Layers)
                            {
                                if (groupLayer.Name == parts[0] && subLayer.Name == parts[1])
                                {
                                    selectedCatchments = subLayer as FeatureLayer;
                                    break;
                                }
                            }
                        }
                        else if (parts.Length == 1 && layer is FeatureLayer featureLayer && layer.Name == parts[0])
                        {
                            selectedCatchments = featureLayer;
                            break;
                        }
                    }

                    if (selectedCatchments == null)
                    {
                        MessageBox.Show("Catchment layer not found.", "Replace Catchment Connections");
                        return false; // Explicitly return a boolean value
                    }

                    var dataConn = selectedCatchments.GetDataConnection() as CIMSqlQueryDataConnection;
                    string dbPath = null;
                    var connParts = dataConn.WorkspaceConnectionString
                        .Split(';');
                    foreach (var part in connParts)
                    {
                        var kv = part.Split('=');
                        if (kv.Length == 2 && kv[0].Equals("DATABASE", StringComparison.OrdinalIgnoreCase))
                        {
                            dbPath = kv[1];
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(dbPath))
                    {
                        MessageBox.Show("Database path not found.", "Replace Catchment Connections");
                        return false; // Explicitly return a boolean value
                    }

                    var selectedIDs = selectedCatchments.GetSelection().GetObjectIDs().ToList();

                    if (selectedIDs == null || selectedIDs.Count == 0)
                    {
                        MessageBox.Show("No catchments selected.", "Replace Catchment Connections");
                        return false;
                    }

                    List<string> selection = new List<string>();

                    var qf = new QueryFilter
                    {
                        ObjectIDs = selectedIDs.ToArray(),
                        SubFields = "muid",
                    };

                    using (var catchmentCursor = selectedCatchments.Search(qf))
                    {
                        while (catchmentCursor.MoveNext())
                        {
                            using (var row = (Feature)catchmentCursor.Current)
                            {
                                var muid = row["muid"]?.ToString();

                                if (!string.IsNullOrEmpty(muid))
                                    selection.Add(muid);
                            }
                        }
                    }

                    string node = nearest.Id;

                    var result = MessageBox.Show(
                                                    $"Do you want to change NodeID to {node} for:\n{string.Join(", ", selection.Select(s => $"'{s}'"))}",
                                                    "Confirm SQL Update",
                                                    MessageBoxButton.YesNo,
                                                    MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        Debug.WriteLine("User cancelled the update.");
                        return false; // Explicitly return a boolean value
                    }

                    using (var conn = new SqliteConnection($"Data Source={dbPath};"))
                    {
                        conn.Open();
                        conn.EnableExtensions(true);
                        conn.LoadExtension("mod_spatialite");

                        // get SRID for the geometry
                        int srid = 0;
                        using (var sridCmd = conn.CreateCommand())
                        {
                            sridCmd.CommandText = "SELECT srid FROM geometry_columns WHERE f_table_name = 'msm_CatchCon' AND f_geometry_column = 'geometry'";
                            using (var reader = sridCmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    srid = reader.GetInt32(0);
                                }
                            }
                        }

                        string nodeWkt = null;
                        using (var getNodeGeoSql = conn.CreateCommand())
                        {
                            getNodeGeoSql.CommandText = $"SELECT AsText(geometry) FROM msm_Node WHERE muid = '{node}'";
                            using (var reader = getNodeGeoSql.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    nodeWkt = reader.GetString(0);
                                }
                            }
                        }
                        MapPoint nodePoint = GeometryEngine.Instance.ImportFromWKT(WktImportFlags.WktImportDefaults, nodeWkt, point.SpatialReference) as MapPoint;

                        foreach (var catchmentId in selection)
                        {
                            // 1. Get the current geometry for this catchment
                            string selectSql = $"SELECT AsText(Centroid(geometry)) FROM msm_Catchment WHERE muid = '{catchmentId}'";
                            string wkt = null;
                            using (var selectCmd = conn.CreateCommand())
                            {
                                selectCmd.CommandText = selectSql;
                                using (var reader = selectCmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        wkt = reader.GetString(0);
                                    }
                                }
                            }

                            MapPoint firstPoint = GeometryEngine.Instance.ImportFromWKT(WktImportFlags.WktImportDefaults, wkt, point.SpatialReference) as MapPoint;
                            var points = new List<MapPoint> { firstPoint, nodePoint };
                            var newPolyline = PolylineBuilderEx.CreatePolyline(points, point.SpatialReference);
                            string newWkt = GeometryEngine.Instance.ExportToWKT(WktExportFlags.WktExportLineString, newPolyline);

                            if (string.IsNullOrEmpty(wkt))
                                continue;

                            using (var checkCmd = conn.CreateCommand())
                            {
                                checkCmd.CommandText = "SELECT 1 FROM msm_CatchCon WHERE catchid = @catchid LIMIT 1";
                                checkCmd.Parameters.AddWithValue("@catchid", catchmentId);

                                using (var reader = checkCmd.ExecuteReader())
                                {
                                    if (reader.Read())
                                    {
                                        using (var updateCmd = conn.CreateCommand())
                                        {
                                            updateCmd.CommandText = @"
                                                UPDATE msm_CatchCon 
                                                SET nodeid = @node, geometry = GeomFromText(@wkt, -1) 
                                                WHERE catchid = @catchid";

                                            updateCmd.Parameters.AddWithValue("@node", node);
                                            updateCmd.Parameters.AddWithValue("@wkt", newWkt);
                                            updateCmd.Parameters.AddWithValue("@catchid", catchmentId);

                                            updateCmd.ExecuteNonQuery();
                                        }
                                    }
                                    else
                                    {
                                        int newMuid = 1;

                                        using (var insertCmd = conn.CreateCommand())
                                        {
                                            insertCmd.CommandText = @"
                                                INSERT INTO msm_CatchCon 
                                                    (muid, catchid, nodeid, geometry, altid, active, rrfraction, pefraction, typeno, loadtypeno, routingtypeno, routingdelay, routingshape)
                                                VALUES 
                                                    (@catchid, @catchid, @node, GeomFromText(@wkt, -1), 0, 1, 1, 1, 1, 1, 1, 0, 0.2)";

                                            insertCmd.Parameters.AddWithValue("@catchid", catchmentId);
                                            insertCmd.Parameters.AddWithValue("@node", node);
                                            insertCmd.Parameters.AddWithValue("@wkt", newWkt);

                                            insertCmd.ExecuteNonQuery();
                                        }

                                    }
                                }
                            }

                        }
                    }

                    return true; // Explicitly return a boolean value
                }

                return false; // Explicitly return a boolean value if geometry is not a MapPoint
            });
        }
    }
}