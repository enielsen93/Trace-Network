using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.PluginDatastore;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping; 
using Microsoft.Data.Sqlite;

namespace TraceNetwork.Network
{
    public static class NetworkService
    {
        public static Dictionary<string, Node> Nodes { get; private set; }
        public static Dictionary<string, Catchment> Catchments { get; private set; }
        public static List<Link> Links { get; private set; }
        public static string FromNodeField { get; private set; }
        public static string ToNodeField{ get; private set; }

        public static Table CatchmentTable { get; private set; }

        public static string SqlitePath = null;
        // KD-tree for fast nearest-neighbor queries
        private static KdTree _kdTree = null;

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
                var dataConn = msm_Catchment.GetDataConnection();

                string dbPath = null;

                if (dataConn != null)
                {
                    var conString = (string)dataConn.GetType().GetProperty("WorkspaceConnectionString")?.GetValue(dataConn);

                    if (!string.IsNullOrEmpty(conString))
                    {
                        var parts = conString
                            .Split(';')
                            .Select(part => part.Split('='))
                            .Where(p => p.Length == 2 && p[0].Equals("DATABASE", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (parts.Count > 0)
                        {
                            dbPath = parts[0][1];
                        }
                    }
                }

                
                var Path = dbPath;
                Debug.WriteLine($"SQLite file path: {dbPath}");

                string connString = $"Data Source={dbPath};Mode=ReadOnly;";
                // Read msm_Catchments
                using (var catchmentCursor = CatchmentTable.Search(null, false))
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
                string geometryField = null;
                var fieldNames = nodeLayer.GetFieldDescriptions().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (fieldNames.Contains("geometry"))
                    geometryField = "geometry";
                else if (fieldNames.Contains("shape"))
                    geometryField = "shape";
                else
                    throw new Exception("No suitable geometry field ('geometry' or 'shape') found.");

                // Read nodes
                using (var nodeCursor = nodeTable.Search())
                {
                    while (nodeCursor.MoveNext())
                    {
                        using (var row = nodeCursor.Current)
                        {
                            var id = row["muid"]?.ToString();
                            var geom = row[geometryField] as MapPoint;

                            if (!string.IsNullOrEmpty(id) && geom != null)
                                Nodes[id] = new Node(id, geom);
                        }
                    }
                }
                // Build spatial index (KD-tree) for fast nearest-node queries
                if (Nodes != null && Nodes.Count > 0)
                {
                    _kdTree = new KdTree(Nodes.Values);
                }
            }


            // Read links and wire them up
            if (linkLayer != null)
            {
                var linkTable = linkLayer.GetTable();
                var fieldNames = linkLayer.GetFieldDescriptions().Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (fieldNames.Contains("fromnodeid"))
                {
                    FromNodeField = "fromnodeid";
                    ToNodeField = "tonodeid";
                }
                else
                {
                    FromNodeField = "fromnode";
                    ToNodeField = "tonode";
                }

                string geometryField = null;
                if (fieldNames.Contains("geometry"))
                    geometryField = "geometry";
                else if (fieldNames.Contains("shape"))
                    geometryField = "shape";
                else
                    throw new Exception("No suitable geometry field ('geometry' or 'shape') found.");

                using (var linkCursor = linkTable.Search())
                {
                    while (linkCursor.MoveNext())
                    {
                        using (var row = linkCursor.Current)
                        {
                            var id = row["muid"]?.ToString();
                            var fromId = row[FromNodeField]?.ToString();
                            var toId = row[ToNodeField]?.ToString();
                            var geom = row[geometryField] as Polyline;

                            if (fromId != null && toId != null
                                && Nodes.TryGetValue(fromId, out var fromNode)
                                && Nodes.TryGetValue(toId, out var toNode))
                            {
                                var link = new Link(id, fromId, toId, geom)
                                {
                                    From = fromNode,
                                    To = toNode,
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

        public static (List<Node> Nodes, List<Link> Links) TraceBetween(Node start, Node end)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<Node>();
            var cameFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            queue.Enqueue(start);
            visited.Add(start.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (current.Id.Equals(end.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var nodePath = new List<Node> { current };
                    while (cameFrom.TryGetValue(current.Id, out var prevId))
                    {
                        current = NetworkService.Nodes[prevId];
                        nodePath.Add(current);
                    }

                    nodePath.Reverse();

                    var linkPath = new List<Link>();
                    for (int i = 0; i < nodePath.Count - 1; i++)
                    {
                        var from = nodePath[i];
                        var to = nodePath[i + 1];
                        var link = from.Outgoing
                                    .Concat(from.Incoming)
                                    .FirstOrDefault(l =>
                                        (l.From?.Id.Equals(from.Id, StringComparison.OrdinalIgnoreCase) == true &&
                                         l.To?.Id.Equals(to.Id, StringComparison.OrdinalIgnoreCase) == true) ||
                                        (l.To?.Id.Equals(from.Id, StringComparison.OrdinalIgnoreCase) == true &&
                                         l.From?.Id.Equals(to.Id, StringComparison.OrdinalIgnoreCase) == true));

                        if (link != null)
                            linkPath.Add(link);
                        else
                            throw new Exception($"No link found between {from.Id} and {to.Id}");
                    }

                    return (nodePath, linkPath);
                }

                foreach (var link in current.Outgoing.Concat(current.Incoming))
                {
                    var neighbor = link.From == current ? link.To : link.From;

                    if (neighbor != null && visited.Add(neighbor.Id))
                    {
                        cameFrom[neighbor.Id] = current.Id;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return (new List<Node>(), new List<Link>());
        }




        public static Node FindNearestNode(MapPoint clickPoint, double maxDistance = 10.0)
        {
            if (Nodes == null || Nodes.Count == 0)
                return null;

            // Use KD-tree if available
            if (_kdTree != null)
            {
                return _kdTree.FindNearest(clickPoint, maxDistance);
            }

            // Fallback to linear scan if kd-tree not built
            Node nearest = null;
            double minDist = double.MaxValue;

            foreach (var node in Nodes.Values)
            {
                double dx = clickPoint.X - node.Geometry.X;
                double dy = clickPoint.Y - node.Geometry.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < minDist && dist <= maxDistance)
                {
                    minDist = dist;
                    nearest = node;
                }
            }

            return nearest;
        }

        // Simple balanced KD-tree for 2D points (stores Node references)
        private class KdTree
        {
            private class KdNode
            {
                public Node Item;
                public double X, Y;
                public KdNode Left, Right;
            }

            private KdNode _root;

            public KdTree(IEnumerable<Node> items)
            {
                var list = items.Where(n => n?.Geometry != null).Select(n => new KdNode
                {
                    Item = n,
                    X = n.Geometry.X,
                    Y = n.Geometry.Y
                }).ToList();

                _root = Build(list, depth: 0);
            }

            private KdNode Build(List<KdNode> list, int depth)
            {
                if (list == null || list.Count == 0)
                    return null;

                int axis = depth % 2; // 0 = x, 1 = y
                if (axis == 0)
                    list.Sort((a, b) => a.X.CompareTo(b.X));
                else
                    list.Sort((a, b) => a.Y.CompareTo(b.Y));

                int mid = list.Count / 2;
                var node = list[mid];

                var leftList = list.GetRange(0, mid);
                var rightList = list.GetRange(mid + 1, list.Count - mid - 1);

                node.Left = Build(leftList, depth + 1);
                node.Right = Build(rightList, depth + 1);

                return node;
            }

            public Node FindNearest(MapPoint pt, double maxDistance)
            {
                double maxDistSq = maxDistance * maxDistance;
                Node best = null;
                double bestDistSq = double.MaxValue;

                void Search(KdNode node, int depth)
                {
                    if (node == null) return;

                    double dx = pt.X - node.X;
                    double dy = pt.Y - node.Y;
                    double distSq = dx * dx + dy * dy;

                    if (distSq < bestDistSq && distSq <= maxDistSq)
                    {
                        bestDistSq = distSq;
                        best = node.Item;
                    }

                    int axis = depth % 2;
                    double delta = (axis == 0) ? dx : dy;

                    KdNode first = delta <= 0 ? node.Left : node.Right;
                    KdNode second = delta <= 0 ? node.Right : node.Left;

                    if (first != null) Search(first, depth + 1);

                    // Check whether we need to search the other side
                    if (second != null && delta * delta <= Math.Min(bestDistSq, maxDistSq))
                        Search(second, depth + 1);
                }

                Search(_root, 0);
                return best;
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
