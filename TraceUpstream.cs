using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using ArcGIS.Desktop.Framework;
using TraceNetwork.Network;
using System.Windows.Input;


namespace TraceNetwork
{
    internal class TraceUpstreamTool : MapTool
    {
        public static TraceUpstreamTool Current { get; private set; }
        public TraceUpstreamTool()
        {
            IsSketchTool = true;  // important: this means we respond to map clicks
            SketchType = SketchGeometryType.Point;
            SketchOutputMode = SketchOutputMode.Map;
            Current = this;
        }

        protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
        {
            // Detect whether Ctrl is held at the time of the click so we can
            // choose between adding to or removing from the current selection.
            var ctrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            return QueuedTask.Run(() =>
            {
                var selectionMethod = ctrlPressed ? SelectionCombinationMethod.Subtract : SelectionCombinationMethod.Add;
                if (geometry is MapPoint point)
                {
                    var nearest = NetworkService.FindNearestNode(point, 20);
                    if (nearest == null)
                    {
                        Debug.WriteLine("No nearby node found.");
                        return true;
                    }

                    var upstream = NetworkService.TraceUpstream(nearest);
                    Debug.WriteLine($"Found {upstream.Count} upstream nodes from {nearest.Id}");
                    var whereClause = $"MUID IN ({string.Join(",", upstream.Select(n => $"'{n.Id}'"))})";
                    var filter = new QueryFilter { WhereClause = whereClause };
                    Layers.msm_Node.Select(filter, selectionMethod);

                    // Select links where fromNode or toNode is in the upstream set
                    var upstreamNodeIds = upstream.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var matchingLinks = NetworkService.Links
                        .Where(link => upstreamNodeIds.Contains(link.ToId))
                        .ToList();



                    if (matchingLinks.Count > 0)
                    {
                        var linkClause = $"{NetworkService.ToNodeField} IN ({string.Join(",", upstreamNodeIds.Select(id => $"'{id}'"))})";
                        var linkFilter = new QueryFilter { WhereClause = linkClause };
                        Layers.msm_Link.Select(linkFilter, selectionMethod);
                    }
                    else
                    {
                        Debug.WriteLine("No matching links found for upstream selection.");
                    }

                    var matchingCatchments = NetworkService.Catchments
                        .Where(kvp => upstreamNodeIds.Contains(kvp.Value.NodeId));

                    var keys = matchingCatchments.Select(kvp => kvp.Value.Muid)
                        .ToList();

                    if (matchingLinks.Count > 0 && Layers.msm_Catchment != null)
                    {
                        var catchmentClause = $"MUID IN ({string.Join(", ", keys.Select(k => $"'{k}'"))})";
                        var CatchmentFilter = new QueryFilter { WhereClause = catchmentClause };
                        Layers.msm_Catchment.Select(CatchmentFilter, selectionMethod);
                    }
                    else
                    {
                        Debug.WriteLine("No matching Catchments found for upstream selection.");
                    }
                }
                return true;
            });
        }

    }
}
