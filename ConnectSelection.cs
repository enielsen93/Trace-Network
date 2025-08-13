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
    public class ConnectSelection : Button
    {

        protected override async void OnClick()
        {
            try
            {
                var selectedNodes = Layers.msm_Node.GetSelection().GetGlobalIDs().ToList();
                var selectedLinks = Layers.msm_Link.GetSelection().GetGlobalIDs().ToList();

            }

            finally
            {
            }

        }

    }
}
