using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using TraceNetwork;
using TraceNetwork.Network;
using static TraceNetwork.Network.NetworkService;

namespace TraceNetwork
{
    internal class ShowDisconnectedCatchments : Button
    {
        public static Dictionary<string, Catchment> Catchments { get; private set; }

        protected override async void OnClick()
        {
            try
            {
                ArcGIS.Desktop.Framework.FrameworkApplication.State.Activate("trace_network_CatchmentLayerComboBoxChanged");

                var muids = new List<string>();
                await QueuedTask.Run(() =>
                {
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


                    Catchments = new Dictionary<string, Catchment>();
                    Dictionary<string, HPara> HParATable = new Dictionary<string, HPara>();

                    if (selectedCatchments != null)
                    {
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

                        string connString = $"Data Source={dbPath};Mode=ReadOnly;";
                        SQLitePCL.Batteries_V2.Init();



                        // Read msm_Catchments
                        ;
                        using (var catchmentCursor = selectedCatchments.Search(null))
                        {
                            while (catchmentCursor.MoveNext())
                            {
                                using (var row = (Feature)catchmentCursor.Current)
                                {
                                    var id = row["muid"]?.ToString();

                                    Catchments[id] = new Catchment() { Muid = id };

                                }
                            }
                        }

                        // Read CatchCon
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
                    }

                    var disconnectedCatchments = Catchments.Values
                        .Where(c => c.NodeId == null)
                        .Select(c => c.Muid)
                        .ToList();

                    var catchmentClause = $"MUID IN ({string.Join(", ", disconnectedCatchments.Select(k => $"'{k}'"))})";
                    Console.WriteLine("Catchment WhereClause: " + catchmentClause);
                    var CatchmentFilter = new QueryFilter { WhereClause = catchmentClause };
                    Layers.msm_Catchment.Select(CatchmentFilter, SelectionCombinationMethod.Add);
                });

            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error");
            }
        }


        public class Catchment
        {
            // Unique ID for the catchment (assuming integer)
            public string Muid { get; set; }

            // Associated node ID (assuming integer)
            public string NodeId { get; set; }
            public string Modelaparaid { get; set; }

            // Area of the catchment, presumably in square meters (double for precision)
            public double Area { get; set; }

            // Shape area, could be different from area due to shape details (double)
            public double ShapeArea { get; set; }

            // Imperviousness as a fraction (0 to 1), so double is appropriate
            public double Imperviousness { get; set; }

            // Number of persons, whole number
            public double Persons { get; set; }

            // Net type number, likely an enum or int representing type
            public int NetTypeNo { get; set; }

            // Whether local parameters are used - boolean
            public bool UseLocalParameters { get; set; }

            // Concentration time in hours or minutes? Use double for flexibility
            public double ConcentrationTime { get; set; }

            public double ReductionFactor { get; set; }

            public double GetImperviousArea()
            {
                // Basic sanity check
                if (Area < 0 || Imperviousness < 0)
                    throw new InvalidOperationException("Area and Imperviousness must be valid and Imperviousness between 0 and 1.");

                return Area * Imperviousness / 100;
            }

            public double GetReducedArea()
            {
                // Basic sanity check
                if (Area < 0 || Imperviousness < 0)
                    throw new InvalidOperationException("Area and Imperviousness must be valid and Imperviousness between 0 and 1.");

                return Area * Imperviousness / 100 * ReductionFactor;
            }

            // Optional: constructor for quick initialization
            public Catchment(string muid)
            {
                Muid = muid;
            }

            // Parameterless constructor for serialization/deserialization or ORM frameworks
            public Catchment() { }
        }
        public class HPara
        {
            public string Muid { get; set; }
            public double Redfactor { get; set; }
            public double Conctime { get; set; }

            public HPara() { }

            public HPara(string muid, double redfactor, double conctime)
            {
                Muid = muid;
                Redfactor = redfactor;
                Conctime = conctime;
            }
        }

        public static (GroupLayer, FeatureLayer) FindLayerWhereGroupNameAndFeatureName(Map map, string caption)
        {
            if (map == null)
                return (null, null);

            // Run inside QueuedTask for safety
            return QueuedTask.Run(() =>
            {
                foreach (var layer in map.Layers)
                {
                    if (layer is GroupLayer groupLayer)
                    {
                        foreach (var subLayer in groupLayer.Layers.OfType<FeatureLayer>())
                        {
                            string[] parts = caption.Split('\\');
                            if (groupLayer.Name == parts[0] && subLayer.Name == parts[1])
                            {
                                return (groupLayer, subLayer);
                            }
                        }
                    }
                }
                return (null, null);
            }).Result; // Blocking wait for simplicity, adjust async handling if needed
        }
    }
}