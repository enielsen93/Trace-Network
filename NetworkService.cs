using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.PluginDatastore;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;  // for FeatureLayer
using Microsoft.Data.Sqlite;
using SQLitePCL;



namespace TraceNetwork.Network
{
    public static class NetworkService
    {
        public static Dictionary<string, Node> Nodes { get; private set; }
        public static Dictionary<string, Catchment> Catchments { get; private set; }
        public static List<Link> Links { get; private set; }

        public static Table CatchmentTable { get; private set; }

        public static string SqlitePath = null;

        /// <summary>
        /// Build the network graph from two already-loaded FeatureLayers.
        /// Must be called inside QueuedTask.Run().
        /// </summary>
        public static void BuildNetwork(FeatureLayer nodeLayer, FeatureLayer linkLayer, FeatureLayer msm_Catchment)
        {
            Table catchConTable;

            // Initialize containers
            Nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            Catchments = new Dictionary<string, Catchment>();
            Dictionary<string, HPara> HParATable = new Dictionary<string, HPara>();
            Links = new List<Link>();

            if (msm_Catchment != null)
            {
                CatchmentTable = msm_Catchment.GetTable();
                var dataConn = msm_Catchment.GetDataConnection() as CIMSqlQueryDataConnection;
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
                SQLitePCL.Batteries_V2.Init();
                {
                    using var conn = new SqliteConnection(connString);
                    conn.Open();

                    var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT MUID, REDFACTOR, CONCTIME FROM msm_HParA;";
                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        var id = reader.GetString(0);
                        HParATable[id] = new HPara() { Muid = id };
                        HParATable[id].Redfactor = reader.GetDouble(1);
                        HParATable[id].Conctime = reader.GetDouble(2);
                    }
                }



                // Read msm_Catchments
                using (var catchmentCursor = CatchmentTable.Search(null, false))
                {
                    while (catchmentCursor.MoveNext())
                    {
                        using (var row = (Feature)catchmentCursor.Current)
                        {
                            var id = row["muid"]?.ToString();
                            Catchments[id] = new Catchment() { Muid = id };

                            var geometry = row.GetShape() as Polygon; // Or appropriate geometry type
                            double geometry_area = geometry?.Area ?? 0;

                            // Calculate area in map units (e.g. square meters)
                            float area = (row["area"] != DBNull.Value && row["area"] != null)
                                ? Convert.ToSingle(row["area"])
                                : (float)geometry_area;

                            Catchments[id].Area = area;
                            Catchments[id].Persons = Convert.IsDBNull(row["Persons"]) ? 0.0 : Convert.ToDouble(row["Persons"]);
                            Catchments[id].Imperviousness = (double)row["modelaimparea"] * 100;
                            Catchments[id].UseLocalParameters = Convert.ToInt32(row["modelalocalno"]) == 1;
                            if (Catchments[id].UseLocalParameters)
                            {
                                Catchments[id].ConcentrationTime = (double)row["modelaconctime"];
                                Catchments[id].ReductionFactor = (double)row["modelarfactor"];
                            }
                            else
                            {
                                Catchments[id].Modelaparaid = (string)row["modelaparaid"];
                                Catchments[id].ConcentrationTime = HParATable[Catchments[id].Modelaparaid].Conctime;
                                Catchments[id].ReductionFactor = HParATable[Catchments[id].Modelaparaid].Redfactor;
                            }
                        }
                    }
                }

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
                        else
                        {
                            Debug.WriteLine("Could not find msm_CatchCon CatchID {nodeid} in msm_Catchment");
                        }

                    }
                }
            }

            if (nodeLayer != null)
            {
                var nodeTable = nodeLayer.GetTable();
                // Read nodes
                using (var nodeCursor = nodeTable.Search())
                {
                    while (nodeCursor.MoveNext())
                    {
                        using (var row = nodeCursor.Current)
                        {
                            var id = row["muid"]?.ToString();
                            var geom = row["geometry"] as MapPoint;
                            if (!string.IsNullOrEmpty(id) && geom != null)
                                Nodes[id] = new Node(id, geom);
                        }
                    }
                }
            }


            // Read links and wire them up
            if (linkLayer != null)
            {
                var linkTable = linkLayer.GetTable();
                using (var linkCursor = linkTable.Search())
                {
                    while (linkCursor.MoveNext())
                    {
                        using (var row = linkCursor.Current)
                        {
                            var fromId = row["fromnodeid"]?.ToString();
                            var toId = row["tonodeid"]?.ToString();
                            var geom = row["geometry"] as Polyline;

                            if (fromId != null && toId != null
                                && Nodes.TryGetValue(fromId, out var fromNode)
                                && Nodes.TryGetValue(toId, out var toNode))
                            {
                                var link = new Link(fromId, toId, geom)
                                {
                                    From = fromNode,
                                    To = toNode
                                };
                                fromNode.Outgoing.Add(link);
                                toNode.Incoming.Add(link);
                                Links.Add(link);
                            }
                        }
                    }
                }
            }


        }

        /// <summary>
        /// Trace downstream: follow Outgoing edges from the given start node.
        /// Must be called inside QueuedTask.Run().
        /// </summary>
        public static IEnumerable<Link> TraceDownstream(string startNodeId)
        {
            if (Nodes == null || !Nodes.ContainsKey(startNodeId))
                yield break;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<Node>();
            queue.Enqueue(Nodes[startNodeId]);
            visited.Add(startNodeId);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var link in node.Outgoing)
                {
                    yield return link;
                    if (visited.Add(link.ToId))
                        queue.Enqueue(link.To);
                }
            }
        }

        /// <summary>
        /// Trace upstream: follow Incoming edges from the given start node.
        /// Must be called inside QueuedTask.Run().
        /// </summary>
        public static List<Node> TraceUpstream(Node start)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Node>();
            var stack = new Stack<Node>();

            stack.Push(start);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!visited.Add(node.Id))
                    continue;

                result.Add(node);

                foreach (var link in node.Incoming)
                {
                    if (link.From != null && !visited.Contains(link.From.Id))
                        stack.Push(link.From);
                }
            }

            return result;
        }
        public static Node FindNearestNode(MapPoint clickPoint, double maxDistance = 10.0)
        {
            Node nearest = null;
            double minDist = double.MaxValue;

            foreach (var node in Nodes.Values)
            {
                double dist = GeometryEngine.Instance.Distance(clickPoint, node.Geometry);
                if (dist < minDist && dist <= maxDistance)
                {
                    minDist = dist;
                    nearest = node;
                }
            }

            return nearest;
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

                return Area * Imperviousness/100;
            }

            public double GetReducedArea()
            {
                // Basic sanity check
                if (Area < 0 || Imperviousness < 0)
                    throw new InvalidOperationException("Area and Imperviousness must be valid and Imperviousness between 0 and 1.");

                return Area * Imperviousness /100 * ReductionFactor;
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
    }
}
