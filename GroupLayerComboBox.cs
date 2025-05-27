using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using TraceNetwork.Network;

namespace TraceNetwork
{ 
    public class GroupLayerComboBox : ComboBox
    {
        private readonly List<LayerItem> _groupLayers = new();
        public static FeatureLayer msm_Node = null;
        public static FeatureLayer msm_Link = null;
        public static FeatureLayer msm_Catchment = null;


        protected override void OnDropDownOpened()
        {
            PopulateCombo();
        }

        private void PopulateCombo()
        {
            QueuedTask.Run(() =>
            {
                _groupLayers.Clear();
                var map = MapView.Active?.Map;
                if (map == null)
                    return;

                var groupLayers = map.Layers.OfType<GroupLayer>().ToList();

                foreach (var layer in groupLayers)
                {
                    _groupLayers.Add(new LayerItem
                    {
                        Name = layer.Name,
                        Layer = layer
                    });
                }

                ArcGIS.Desktop.Framework.FrameworkApplication.Current.Dispatcher.Invoke(() =>
                {
                    Clear();
                    foreach (var layerItem in _groupLayers)
                    {
                        Add(new ComboBoxItem(layerItem.Name));
                    }
                });
            });
        }

        public void SummarizeSelectedCatchments()
        {
            var muids = new List<string>();
            var CatchmentLayer = msm_Catchment;

            // Get the selection from the map or from the table itself
            var selection = CatchmentLayer.GetSelection();
            var selectedOids = selection.GetObjectIDs();

            var oidField = CatchmentLayer.GetDefinition();
        }

        protected override void OnSelectionChange(ComboBoxItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Text))
                return;

            var selected = _groupLayers.FirstOrDefault(l => l.Name == item.Text);
            if (selected?.Layer == null)
                return;

            var selectedGroup = selected.Layer;

            QueuedTask.Run(() =>
            {
                var allLayers = FlattenGroupLayer(selectedGroup)
                    .OfType<FeatureLayer>()
                    .ToList();

                msm_Node = allLayers.FirstOrDefault(l =>
                    NameMatch(l.Name, "msm_Node", "Manhole", "Brønd"));

                msm_Link = allLayers.FirstOrDefault(l =>
                    NameMatch(l.Name, "msm_Link", "Ledning", "Pipe"));

                msm_Catchment = allLayers.FirstOrDefault(l =>
                    NameMatch(l.Name, "msm_Catchment", "Delopland", "Catchment"));

                Debug.WriteLine(msm_Node != null
                    ? $"✅ Found msm_Node: {msm_Node.Name}"
                    : "❌ No msm_Node found.");

                Debug.WriteLine(msm_Link != null
                    ? $"✅ Found msm_Link: {msm_Link.Name}"
                    : "❌ No msm_Link found.");

                Debug.WriteLine(msm_Catchment != null
                    ? $"✅ Found msm_Catchment: {msm_Catchment.Name}"
                    : "❌ No msm_Catchment found.");

                if (msm_Catchment != null && CatchmentLayerComboBox.CatchmentLayerCaption == null)
                {
                    CatchmentLayerComboBox.CatchmentLayerCaption = selectedGroup.Name + "\\" + msm_Catchment.Name;


                }

                // To build:
                NetworkService.BuildNetwork(msm_Node, msm_Link, msm_Catchment);

            });
        }

        private static bool NameMatch(string name, params string[] targets)
        {
            return targets.Any(t =>
                string.Equals(name, t, System.StringComparison.OrdinalIgnoreCase));
        }

        private List<Layer> FlattenGroupLayer(GroupLayer group)
        {
            var list = new List<Layer>();
            foreach (var layer in group.Layers)
            {
                list.Add(layer);
                if (layer is GroupLayer subGroup)
                {
                    list.AddRange(FlattenGroupLayer(subGroup));
                }
            }
            return list;
        }


        private class LayerItem
        {
            public string Name { get; set; }
            public GroupLayer Layer { get; set; }
        }
    }
}

namespace TraceNetwork.Network
{
    // 1) Domain classes
    public class Node
    {
        public string Id { get; }
        public MapPoint Geometry { get; }
        public List<Link> Outgoing { get; } = new();
        public List<Link> Incoming { get; } = new();

        public Node(string id, MapPoint geom)
        {
            Id = id;
            Geometry = geom;
        }
    }

    public class Link
    {
        public string FromId { get; }
        public string ToId { get; }
        public Polyline Geometry { get; }
        public Node From { get; set; }
        public Node To { get; set; }

        public Link(string fromId, string toId, Polyline geom)
        {
            FromId = fromId;
            ToId = toId;
            Geometry = geom;
        }
    }

    namespace TraceNetwork.Network
    {
        public class Node
        {
            public string Id { get; }
            public MapPoint Geometry { get; }
            public List<Link> Outgoing { get; } = new();
            public List<Link> Incoming { get; } = new();

            public Node(string id, MapPoint geom)
            {
                Id = id;
                Geometry = geom;
            }
        }

        public class Link
        {
            public string FromId { get; }
            public string ToId { get; }
            public Polyline Geometry { get; }
            public Node From { get; set; }
            public Node To { get; set; }

            public Link(string fromId, string toId, Polyline geom)
            {
                FromId = fromId;
                ToId = toId;
                Geometry = geom;
            }
        }
    }
}
