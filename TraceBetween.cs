using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    internal class TraceBetweenTool : MapTool
    {
        private Node _firstNode = null;

        public TraceBetweenTool()
        {
            IsSketchTool = true;  // important: this means we respond to map clicks
            SketchType = SketchGeometryType.Point;
            SketchOutputMode = SketchOutputMode.Map;
        }

        protected override Task OnToolActivateAsync(bool hasMapViewChanged)
        {
            _firstNode = null; // Reset state on activation
            return base.OnToolActivateAsync(hasMapViewChanged);
        }


        protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
        {
            return QueuedTask.Run(() =>
            {
                if (geometry is not MapPoint point)
                    return true;

                var nearest = NetworkService.FindNearestNode(point, 20);
                if (nearest == null)
                {
                    Debug.WriteLine("No nearby node found.");
                    _firstNode = null; // Reset if second click fails
                    return true;
                }

                if (_firstNode == null)
                {
                    // First click — store the node
                    _firstNode = nearest;
                    //Debug.WriteLine($"First node selected: {_firstNode.Id}");
                }
                else
                {
                    // Second click — perform trace
                    var secondNode = nearest;
                    Debug.WriteLine($"First node selected: {_firstNode.Id}");
                    Debug.WriteLine($"Second node selected: {secondNode.Id}");

                    var (nodes, links) = NetworkService.TraceBetween(_firstNode, secondNode); 
                    _firstNode = null; // Reset for next use

                    var node_whereClause = $"MUID IN ({string.Join(",", nodes.Select(n => $"'{n.Id}'"))})";
                    var node_filter = new QueryFilter { WhereClause = node_whereClause };
                    Layers.msm_Node.Select(node_filter, SelectionCombinationMethod.Add);

                    var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var link_whereClause = $"MUID IN ({string.Join(",", links.Select(l => $"'{l.Id}'"))})";
                    var link_filter = new QueryFilter { WhereClause = link_whereClause };
                    Layers.msm_Link.Select(link_filter, SelectionCombinationMethod.Add);

                    var matchingCatchments = NetworkService.Catchments
                        .Where(kvp => nodeIds.Contains(kvp.Value.NodeId));

                    var keys = matchingCatchments.Select(kvp => kvp.Value.Muid)
                        .ToList();

                    if (Layers.msm_Catchment != null)
                    {
                        var catchmentClause = $"MUID IN ({string.Join(", ", keys.Select(k => $"'{k}'"))})";
                        var CatchmentFilter = new QueryFilter { WhereClause = catchmentClause };
                        Layers.msm_Catchment.Select(CatchmentFilter, SelectionCombinationMethod.Add);
                    }
                    else
                    {
                        Debug.WriteLine("No matching Catchments found for selection.");
                    }
                }

                return true;

            });
        }
    }
}