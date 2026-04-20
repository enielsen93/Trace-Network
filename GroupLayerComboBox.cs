﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using System.IO;
using System.Text;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Microsoft.Data.Sqlite;
using ArcGIS.Desktop.Framework;
using TraceNetwork.Network;

namespace TraceNetwork
{ 
    internal static class FileLogger
    {
        private static readonly object _sync = new();
        private static readonly string _logPath;

        static FileLogger()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraceNetwork");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, "tracenetwork.log");
            }
            catch
            {
                // swallow any exception - logging must not interfere with app
                _logPath = null;
            }
        }

        public static void Log(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                    return;
                var line = $"[{DateTime.Now:O}] {message}";
                lock (_sync)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // ignore
            }
        }

        public static void LogException(Exception ex, string context = null)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                    return;
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:O}] EXCEPTION: {context}");
                sb.AppendLine(ex.ToString());
                lock (_sync)
                {
                    File.AppendAllText(_logPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
    public static class Layers
    {
        public static FeatureLayer msm_Node = null;
        public static FeatureLayer msm_Link = null;
        public static FeatureLayer msm_Catchment = null;
    }
    public class GroupLayerComboBox : ComboBox
    {
        public static GroupLayerComboBox Current { get; private set; }
        private readonly List<LayerItem> _groupLayers = new();

        public GroupLayerComboBox()
        {
            Current = this;
            FileLogger.Log("GroupLayerComboBox constructed");
        }



        protected override void OnDropDownOpened()
        {
            FileLogger.Log("OnDropDownOpened");
            try
            {
                PopulateCombo();
            }
            catch (Exception ex)
            {
                FileLogger.LogException(ex, "OnDropDownOpened");
                throw;
            }
        }

        private void PopulateCombo()
        {
            FileLogger.Log("PopulateCombo start");
            try
            {
                SQLitePCL.Batteries_V2.Init();
            // MapView.Active must be accessed from the UI thread. Capture the reference
            // here (PopulateCombo is called from the UI) and then enumerate layers
            // on a QueuedTask

            var mapView = MapView.Active;
            if (mapView == null)
                {
                    FileLogger.Log("PopulateCombo: MapView.Active is null");
                    return;
                }

            var map = mapView.Map;  
            if (map == null)
                {
                    FileLogger.Log("PopulateCombo: map is null");
                    return;
                }

            // Fire-and-forget the queued task - the UI will be updated via Dispatcher.Invoke
            _ = QueuedTask.Run(() =>
            {
                    try
                    {
                        FileLogger.Log("PopulateCombo: QueuedTask running");
                        _groupLayers.Clear();

                        var groupLayers = map.Layers.OfType<GroupLayer>().ToList();

                        foreach (var layer in groupLayers)
                        {
                            _groupLayers.Add(new LayerItem
                            {
                                Name = layer.Name,
                                Layer = layer
                            });
                        }

                        FileLogger.Log($"PopulateCombo: found {_groupLayers.Count} group layers");

                        ArcGIS.Desktop.Framework.FrameworkApplication.Current.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                Clear();
                                foreach (var layerItem in _groupLayers)
                                {
                                    Add(new ComboBoxItem(layerItem.Name));
                                }
                                FileLogger.Log("PopulateCombo: UI updated with group layers");
                            }
                            catch (Exception uiEx)
                            {
                                FileLogger.LogException(uiEx, "PopulateCombo: Dispatcher.Invoke UI thread");
                                throw;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        FileLogger.LogException(ex, "PopulateCombo: QueuedTask body");
                        throw;
                    }
            });
            }
            catch (Exception ex)
            {
                FileLogger.LogException(ex, "PopulateCombo");
                throw;
            }
        }

        public void SummarizeSelectedCatchments()
        {
            FileLogger.Log("SummarizeSelectedCatchments start");
            try
            {
                var muids = new List<string>();
                var CatchmentLayer = Layers.msm_Catchment;

                if (CatchmentLayer == null)
                {
                    FileLogger.Log("SummarizeSelectedCatchments: CatchmentLayer is null");
                    return;
                }

                // Get the selection from the map or from the table itself
                var selection = CatchmentLayer.GetSelection();
                var selectedOids = selection.GetObjectIDs();
                FileLogger.Log($"SummarizeSelectedCatchments");

                var oidField = CatchmentLayer.GetDefinition();
            }
            catch (Exception ex)
            {
                FileLogger.LogException(ex, "SummarizeSelectedCatchments");
                throw;
            }
        }

        protected override void OnSelectionChange(ComboBoxItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Text))
                return;
            FileLogger.Log($"OnSelectionChange: item.Text = {item?.Text}");

            var selected = _groupLayers.FirstOrDefault(l => l.Name == item.Text);
            if (selected?.Layer == null)
                return;

            var selectedGroup = selected.Layer;

            QueuedTask.Run(() =>
            {
                try
                {
                    FileLogger.Log("OnSelectionChange: QueuedTask running");
                    var allLayers = FlattenGroupLayer(selectedGroup)
                        .OfType<FeatureLayer>()
                        .ToList();

                    FileLogger.Log($"OnSelectionChange: allLayers count = {allLayers.Count}");

                    Layers.msm_Node = allLayers.FirstOrDefault(l =>
                        NameMatch(l.Name, "msm_Node", "Manhole", "Brønd", "Node"));

                    Layers.msm_Link = allLayers.FirstOrDefault(l =>
                        NameMatch(l.Name, "msm_Link", "Ledning", "Pipe", "Reach"));

                    Layers.msm_Catchment = allLayers.FirstOrDefault(l =>
                        NameMatch(l.Name, "msm_Catchment", "ms_Catchment", "Delopland", "Catchment"));

                    FileLogger.Log($"OnSelectionChange: msm_Node={(Layers.msm_Node==null?"null":Layers.msm_Node.Name)}, msm_Link={(Layers.msm_Link==null?"null":Layers.msm_Link.Name)}, msm_Catchment={(Layers.msm_Catchment==null?"null":Layers.msm_Catchment.Name)}");

                    try
                    {
                        CatchmentLayerComboBox.Current.SelectedItem = selectedGroup.Name + "\\" + Layers.msm_Catchment?.Name;
                        CatchmentLayerComboBox.SelectedCatchment = (selectedGroup, Layers.msm_Catchment);
                        FrameworkApplication.State.Activate("trace_network_CatchmentLayerComboBoxValid");
                    }
                    catch (Exception exUi)
                    {
                        FileLogger.LogException(exUi, "OnSelectionChange: setting CatchmentLayerComboBox or state");
                        throw;
                    }

                    NetworkService.BuildNetwork(Layers.msm_Node, Layers.msm_Link, Layers.msm_Catchment);
                    FrameworkApplication.State.Activate("trace_network_TraceNetworkValid");
                    //TraceUpstreamTool.Current.Enabled = true;
                    SearchBox.Current.Enabled = true;
                    FileLogger.Log("OnSelectionChange: QueuedTask completed");
                }
                catch (Exception ex)
                {
                    FileLogger.LogException(ex, "OnSelectionChange: QueuedTask body");
                    throw;
                }
            });
        }

        private static bool NameMatch(string name, params string[] targets)
        {
            return targets.Any(t =>
                name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private List<Layer> FlattenGroupLayer(GroupLayer group)
        {
            var list = new List<Layer>();
            try
            {
                foreach (var layer in group.Layers)
                {
                    list.Add(layer);
                    if (layer is GroupLayer subGroup)
                    {
                        list.AddRange(FlattenGroupLayer(subGroup));
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.LogException(ex, "FlattenGroupLayer");
                throw;
            }
            return list;
        }


        private class LayerItem
        {
            public string Name { get; set; }
            public GroupLayer Layer { get; set; }
        }
        public void RefreshSelection()
        {
            FileLogger.Log("RefreshSelection called");
            try
            {
                // Trigger the OnSelChange logic with current selected item
                OnSelectionChange(SelectedItem);
            }
            catch (Exception ex)
            {
                FileLogger.LogException(ex, "RefreshSelection");
                throw;
            }
        }

        public void SelectFeatures(QueryFilter query)
        {

            Layers.msm_Node.Select(query, SelectionCombinationMethod.New);
            Layers.msm_Link.Select(query, SelectionCombinationMethod.Add);
            Layers.msm_Catchment.Select(query, SelectionCombinationMethod.Add);

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
        public string Id { get; }

        public Link(string id, string fromId, string toId, Polyline geom)
        {
            FromId = fromId;
            ToId = toId;
            Geometry = geom;
            Id = id;
            
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
