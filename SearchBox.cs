using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using TraceNetwork;

namespace TraceNetwork
{
    /// <summary>
    /// Represents the ComboBox
    /// </summary>
    internal class SearchBox : ComboBox
    {
        public static SearchBox Current { get; private set; }
        private bool _isInitialized;

        /// <summary>
        /// Combo Box constructor
        /// </summary>
        public SearchBox()
        {
            Current = this;
            this.Enabled = false;
            UpdateCombo();
        }

        /// <summary>
        /// Updates the combo box with all the items.
        /// </summary>

        private void UpdateCombo()
        {
        }

        /// <summary>
        /// The on comboBox selection change event. 
        /// </summary>
        /// <param name="item">The newly selected combo box item</param>

        protected override void OnEnter()
        {

            string text = this.Text;

            Debug.WriteLine("On Selection Change");
            Debug.WriteLine(text);

            if (text == null)
                return;

            if (string.IsNullOrEmpty(text))
                return;

            // TODO  Code behavior when selection changes.    
            // Heuristics: contains =, AND, OR, LIKE, >, <, etc.
            string[] sqlOperators = { "=", ">", "<", ">=", "<=", "<>", "LIKE", "IN", "IS", "BETWEEN" };
            string[] logicOperators = { "AND", "OR", "NOT" };

            var upper = text.ToUpperInvariant();
            var isSql = false;
            if (sqlOperators.Any(op => upper.Contains(op)) ||
                   logicOperators.Any(op => upper.Contains($" {op} ")))
            {
                isSql = true;
            }
            var query_filter = new QueryFilter { };
            if (!isSql)
            {
                query_filter = new QueryFilter { WhereClause = $"MUID = '{text}'" };
            }
            else
            {
                query_filter = new QueryFilter { WhereClause = text };
            }
            QueuedTask.Run(() =>
             {
                 Layers.msm_Node.Select(query_filter, SelectionCombinationMethod.New);
                 Layers.msm_Link.Select(query_filter, SelectionCombinationMethod.New);
                 Layers.msm_Catchment.Select(query_filter, SelectionCombinationMethod.New);

                 MapView.Active.ZoomToSelected();

             });
            base.OnTextChange(text);
        }
        protected override void OnSelectionChange(ComboBoxItem item)
        {
            Debug.WriteLine("On Selection Change");
            Debug.WriteLine(item.Text);

            if (item == null)
                return;

            if (string.IsNullOrEmpty(item.Text))
                return;

            // TODO  Code behavior when selection changes.    
            // Heuristics: contains =, AND, OR, LIKE, >, <, etc.
            string[] sqlOperators = { "=", ">", "<", ">=", "<=", "<>", "LIKE", "IN", "IS", "BETWEEN" };
            string[] logicOperators = { "AND", "OR", "NOT" };

            var upper = item.Text.ToUpperInvariant();
            var isSql = false;
            if( sqlOperators.Any(op => upper.Contains(op)) ||
                   logicOperators.Any(op => upper.Contains($" {op} ")))
            {
                isSql = true;
            }
            var query_filter = new QueryFilter { };
            if (!isSql)
            {
                query_filter = new QueryFilter { WhereClause = $"MUID = '{item.Text}'" }; 
            }
            else
            {
                query_filter = new QueryFilter { WhereClause = item.Text };
            }
            Layers.msm_Node.Select(query_filter, SelectionCombinationMethod.New);
            Layers.msm_Link.Select(query_filter, SelectionCombinationMethod.New);
            Layers.msm_Catchment.Select(query_filter, SelectionCombinationMethod.New);

        }

    }
}
