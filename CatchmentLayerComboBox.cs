using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Internal.Catalog;
using ArcGIS.Desktop.Mapping;


namespace TraceNetwork
{
    /// <summary>
    /// ComboBox that lists every FeatureLayer inside every GroupLayer,
    /// displaying them as "GroupName\FeatureName" and storing both refs.
    /// </summary>
    internal class CatchmentLayerComboBox : ComboBox
    {
        public static CatchmentLayerComboBox Current { get; private set; }

        public CatchmentLayerComboBox()
        {
            Current = this;
        }
        // Public accessor for the selected pair
        public static (GroupLayer Group, FeatureLayer Feature) SelectedCatchment { get; private set; }
        public static string CatchmentLayerCaption;

        // Helper container so we can store both in one item
        private class LayerPair
        {
            public GroupLayer Group { get; }
            public FeatureLayer Feature { get; }
            public LayerPair(GroupLayer g, FeatureLayer f) { Group = g; Feature = f; }
        }

        protected override void OnDropDownOpened()
        {
            // fire-and-forget the async loader
            _ = PopulateAsync();
        }

        private async Task PopulateAsync()
        {
            Clear();   // ArcGIS Pro SDK method
            await QueuedTask.Run(() =>
            {
                var map = MapView.Active?.Map;
                if (map == null) return;

                var pairs = new List<LayerPair>();

                // Helper to collect from any Layer
                void CollectFeatureLayers(Layer layer, GroupLayer group = null)
                {
                    if (layer is GroupLayer g)
                    {
                        foreach (var sub in g.Layers)
                            CollectFeatureLayers(sub, g);
                    }
                    else if (layer is FeatureLayer f)
                    {
                        if (f.ShapeType == esriGeometryType.esriGeometryPolygon)
                            pairs.Add(new LayerPair(group, f));
                    }
                }

                // Start with top-level layers
                foreach (var layer in map.Layers)
                    CollectFeatureLayers(layer);

                // Back to UI thread
                ArcGIS.Desktop.Framework.FrameworkApplication.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var p in pairs)
                    {
                        string caption = p.Group != null
                            ? $"{p.Group.Name}\\{p.Feature.Name}"
                            : p.Feature.Name;

                        Add(new ComboBoxItem(caption));
                    }
                });
            });
        }

        protected override void OnSelectionChange(ComboBoxItem item)
        {
            CatchmentLayerCaption = item.Text;
            if (SelectedItem is LayerPair lp)
                SelectedCatchment = (lp.Group, lp.Feature);
            else
                SelectedCatchment = (null, null);
        }

        #region Helpers

        private void CollectGroupLayers(Layer lyr, List<GroupLayer> output)
        {
            if (lyr is GroupLayer gl)
            {
                output.Add(gl);
                foreach (var child in gl.Layers)
                    CollectGroupLayers(child, output);
            }
        }
        public void RefreshSelection()
        {
            OnSelectionChange(SelectedItem);
        }


        // Optional: deeper feature recursion if you have nested groups inside groups
        //private void CollectFeatureLayersRecursive(Layer lyr, List<LayerPair> output, GroupLayer parentGroup)
        //{
        //    if (lyr is FeatureLayer fl)
        //        output.Add(new LayerPair(parentGroup, fl));
        //    else if (lyr is GroupLayer gl)
        //        foreach (var child in gl.Layers)
        //            CollectFeatureLayersRecursive(child, output, parentGroup);
        //}

        #endregion
    }
}
