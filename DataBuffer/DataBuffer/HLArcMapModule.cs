﻿// DataBuffer is an ArcGIS add-in used to create 'species alert'
// layers from existing species data.
//
// Copyright © 2017 SxBRC, 2017-2018 TVERC
//
// This file is part of DataBuffer.
//
// DataBuffer is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// DataBuffer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with DataBuffer.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;

using ESRI.ArcGIS.ArcMapUI;
using ESRI.ArcGIS.Desktop.AddIns;
using ESRI.ArcGIS.Framework;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.GeoDatabaseUI;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.Geoprocessing;
using ESRI.ArcGIS.AnalysisTools;
using ESRI.ArcGIS.DataManagementTools;
using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.DataSourcesFile;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.DataSourcesOleDB;
using ESRI.ArcGIS.Display;

using ESRI.ArcGIS.Catalog;
using ESRI.ArcGIS.CatalogUI;

using HLFileFunctions;

namespace HLArcMapModule
{
    public class ArcMapFunctions
    {
        #region Constructor
        private IApplication thisApplication;
        private FileFunctions myFileFuncs;
        // Class constructor.
        public ArcMapFunctions(IApplication theApplication)
        {
            // Set the application for the class to work with.
            // Note the application can be got at from a command / tool by using
            // IApplication pApp = ArcMap.Application - then pass pApp as an argument.
            this.thisApplication = theApplication;
            myFileFuncs = new FileFunctions();
        }
        #endregion

        public IMxDocument GetIMXDocument()
        {
            ESRI.ArcGIS.ArcMapUI.IMxDocument mxDocument = ((ESRI.ArcGIS.ArcMapUI.IMxDocument)(thisApplication.Document));
            return mxDocument;
        }

        public void UpdateTOC()
        {
            IMxDocument mxDoc = GetIMXDocument();
            mxDoc.UpdateContents();
        }

        public bool SaveMXD()
        {
            IMxDocument mxDoc = GetIMXDocument();
            IMapDocument pDoc = (IMapDocument)mxDoc;
            try
            {
                pDoc.Save(true, true);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot save mxd. Error is: " + ex.Message, "Error");
                return false;
            }
        }

        public IActiveView GetActiveView()
        {
            IMxDocument mxDoc = GetIMXDocument();
            return mxDoc.ActiveView;
        }

        public ESRI.ArcGIS.Carto.IMap GetMap()
        {
            if (thisApplication == null)
            {
                return null;
            }
            ESRI.ArcGIS.ArcMapUI.IMxDocument mxDocument = ((ESRI.ArcGIS.ArcMapUI.IMxDocument)(thisApplication.Document)); // Explicit Cast
            ESRI.ArcGIS.Carto.IActiveView activeView = mxDocument.ActiveView;
            ESRI.ArcGIS.Carto.IMap map = activeView.FocusMap;

            return map;
        }

        public void RefreshTOC()
        {
            IMxDocument theDoc = GetIMXDocument();
            theDoc.CurrentContentsView.Refresh(null);
        }

        public IWorkspaceFactory GetWorkspaceFactory(string aFilePath, bool aTextFile = false, bool Messages = false)
        {
            // This function decides what type of feature workspace factory would be best for this file.
            // it is up to the user to decide whether the file path and file names exist (or should exist).

            IWorkspaceFactory pWSF;
            // What type of output file it it? This defines what kind of workspace factory.
            if (aFilePath.Substring(aFilePath.Length - 4, 4) == ".gdb")
            {
                // It is a file geodatabase file.
                pWSF = new FileGDBWorkspaceFactory();
            }
            else if (aFilePath.Substring(aFilePath.Length - 4, 4) == ".mdb")
            {
                // Personal geodatabase.
                pWSF = new AccessWorkspaceFactory();
            }
            else if (aFilePath.Substring(aFilePath.Length - 4, 4) == ".sde")
            {
                // ArcSDE connection
                pWSF = new SdeWorkspaceFactory();
            }
            else if (aTextFile == true)
            {
                // Text file
                pWSF = new TextFileWorkspaceFactory();
            }
            else
            {
                pWSF = new ShapefileWorkspaceFactory();
            }
            return pWSF;
        }

        public bool IsGDB(string aWorkspace, bool Messages = false)
        {
            // simply checks the last four characters to see if it's either a .mdb or a .gdb
            string strTest = aWorkspace.Substring(aWorkspace.Length - 4, 4).ToLower();
            if (strTest == ".mdb" || strTest == ".gdb")
                return true;
            else
                return false;
        }

        public bool CreateWorkspace(string aWorkspace, bool Messages = false)
        {
            IWorkspaceFactory pWSF = GetWorkspaceFactory(aWorkspace);
            try
            {
                pWSF.Create(myFileFuncs.GetDirectoryName(aWorkspace), myFileFuncs.GetFileName(aWorkspace), null, 0);
            }
            catch
            {
                return false;
            }
            finally
            {
                pWSF = null;
            }
            return true;
        }

        #region FeatureclassExists
        public bool FeatureclassExists(string aFilePath, string aDatasetName)
        {
            
            if (aDatasetName.Length > 4 && aDatasetName.Substring(aDatasetName.Length - 4, 1) == ".")
            {
                // it's a file.
                if (myFileFuncs.FileExists(aFilePath + @"\" + aDatasetName))
                    return true;
                else
                    return false;
            }
            else if (aFilePath.Length > 3 && aFilePath.Substring(aFilePath.Length - 3, 3) == "sde")
            {
                // It's an SDE class
                // Not handled. We know the table exists.
                return true;
            }
            else // it is a geodatabase class.
            {
                bool blReturn = false;
                IWorkspaceFactory pWSF = GetWorkspaceFactory(aFilePath);
                if (pWSF != null)
                {
                    try
                    {
                        IWorkspace2 pWS = (IWorkspace2)pWSF.OpenFromFile(aFilePath, 0);
                        if (pWS.get_NameExists(ESRI.ArcGIS.Geodatabase.esriDatasetType.esriDTFeatureClass, aDatasetName))
                            blReturn = true;
                        Marshal.ReleaseComObject(pWS);
                    }
                    catch
                    {
                        // It doesn't exist
                        return false;
                    }
                }

                return blReturn;
            }
        }

        public bool FeatureclassExists(string aFullPath)
        {
            return FeatureclassExists(myFileFuncs.GetDirectoryName(aFullPath), myFileFuncs.GetFileName(aFullPath));
        }

        #endregion

        public bool LayerOrFeatureclassExists(string aName)
        {
            bool blResult = false;
            // Takes care of making sure the data exists for tools using the geoprocessor, which doesn't care where the input lives.
            if (FeatureclassExists(aName))
                blResult = true;
            else if (LayerExists(aName))
                blResult = true;

            return blResult;
        }

        public string GetFeatureClassType(IFeatureClass aFeatureClass, bool Messages = false)
        {
            // Sub returns a simplified list of FC types: point, line, polygon.

            IFeatureCursor pFC = aFeatureClass.Search(null, false); // Get all the objects.
            IFeature pFeature = pFC.NextFeature();
            string strReturnValue = "other";
            if (!(pFeature == null))
            {
                IGeometry pGeom = pFeature.Shape;
                if (pGeom.GeometryType == esriGeometryType.esriGeometryMultipoint || pGeom.GeometryType == esriGeometryType.esriGeometryPoint)
                {
                    strReturnValue = "point";
                }
                else if (pGeom.GeometryType == esriGeometryType.esriGeometryRing || pGeom.GeometryType == esriGeometryType.esriGeometryPolygon)
                {
                    strReturnValue = "polygon";
                }
                else if (pGeom.GeometryType == esriGeometryType.esriGeometryLine || pGeom.GeometryType == esriGeometryType.esriGeometryPolyline ||
                    pGeom.GeometryType == esriGeometryType.esriGeometryCircularArc || pGeom.GeometryType == esriGeometryType.esriGeometryEllipticArc ||
                    pGeom.GeometryType == esriGeometryType.esriGeometryBezier3Curve || pGeom.GeometryType == esriGeometryType.esriGeometryPath)
                {
                    strReturnValue = "line";
                }

            }

            return strReturnValue;
        }

        #region GetFeatureClass
        public IFeatureClass GetFeatureClass(string aFilePath, string aDatasetName, string aLogFile = "", bool Messages = false)
        // This is incredibly quick.
        {
            // Check input first.
            string aTestPath = aFilePath;
            if (aFilePath.Contains(".sde"))
            {
                aTestPath = myFileFuncs.GetDirectoryName(aFilePath);
            }
            if (myFileFuncs.DirExists(aTestPath) == false || aDatasetName == null)
            {
                if (Messages) MessageBox.Show("Please provide valid input", "Get Featureclass");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureClass returned the following error: Please provide valid input.");
                return null;
            }
            

            IWorkspaceFactory pWSF = GetWorkspaceFactory(aFilePath);
            IFeatureWorkspace pWS = (IFeatureWorkspace)pWSF.OpenFromFile(aFilePath, 0);
            if (FeatureclassExists(aFilePath, aDatasetName))
            {
                IFeatureClass pFC = pWS.OpenFeatureClass(aDatasetName);
                Marshal.ReleaseComObject(pWS);
                pWS = null;
                pWSF = null;
                GC.Collect();
                return pFC;
            }
            else
            {
                if (Messages) MessageBox.Show("The file " + aDatasetName + " doesn't exist in this location", "Open Feature Class from Disk");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureClass returned the following error: The file " + aDatasetName + " doesn't exist in this location");
                Marshal.ReleaseComObject(pWS);
                pWS = null;
                pWSF = null;
                GC.Collect();
                return null;
            }

        }


        public IFeatureClass GetFeatureClass(string aFullPath, string aLogFile = "", bool Messages = false)
        {
            string aFilePath = myFileFuncs.GetDirectoryName(aFullPath);
            string aDatasetName = myFileFuncs.GetFileName(aFullPath);
            IFeatureClass pFC = GetFeatureClass(aFilePath, aDatasetName, aLogFile, Messages);
            return pFC;
        }

        public IFeatureClass GetFeatureClassFromLayerName(string aLayerName, string aLogFile = "", bool Messages = false)
        {
            // Returns the feature class associated with a layer name if a. the layer exists and b. it's a feature layer, otherwise returns null.
            ILayer pLayer = GetLayer(aLayerName);
            if (pLayer == null)
            {
                if (Messages) MessageBox.Show("The layer " + aLayerName + " does not exist.");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureClassFromLayerName returned the following error: The layer " + aLayerName + " doesn't exist");
                return null;
            }
            IFeatureLayer pFL = null;
            try
            {
                pFL = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages) MessageBox.Show("The layer " + aLayerName + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureClassFromLayerName returned the following error: The layer " + aLayerName + " is not a feature layer");
                return null; // It is not a feature layer.
            }
            return pFL.FeatureClass;
        }

        #endregion

        public IFeatureLayer GetFeatureLayerFromString(string aFeatureClassName, string aLogFile, bool Messages = false)
        {
            // as far as I can see this does not work for geodatabase files.
            // firstly get the Feature Class
            // Does it exist?
            if (!myFileFuncs.FileExists(aFeatureClassName))
            {
                if (Messages)
                {
                    MessageBox.Show("The featureclass " + aFeatureClassName + " does not exist");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureLayerFromString returned the following error: The featureclass " + aFeatureClassName + " does not exist");
                return null;
            }
            string aFilePath = myFileFuncs.GetDirectoryName(aFeatureClassName);
            string aFCName = myFileFuncs.GetFileName(aFeatureClassName);

            IFeatureClass myFC = GetFeatureClass(aFilePath, aFCName, aLogFile, Messages);
            if (myFC == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Cannot open featureclass " + aFeatureClassName);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureLayerFromString returned the following error: Cannot open featureclass " + aFeatureClassName);
                return null;
            }

            // Now get the Feature Layer from this.
            FeatureLayer pFL = new FeatureLayer();
            pFL.FeatureClass = myFC;
            pFL.Name = myFC.AliasName;
            return pFL;
        }

        public ILayer GetLayer(string aName, string aLogFile = "", bool Messages = false)
        {
            // Gets existing layer in map.
            // Check there is input.
           if (aName == null)
           {
               if (Messages)
               {
                   MessageBox.Show("Please pass a valid layer name", "Find Layer By Name");
               }
               if (aLogFile != "")
                   myFileFuncs.WriteLine(aLogFile, "Function GetLayer returned the following error: Please pass a valid layer name");
               return null;
            }
        
            // Get map, and layer names.
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages)
                {
                    MessageBox.Show("No map found", "Find Layer By Name");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetLayer returned the following error: No map found");
                return null;
            }
            IEnumLayer pLayers = pMap.Layers;
            Boolean blFoundit = false;
            ILayer pTargetLayer = null;

            ILayer pLayer = pLayers.Next();

            // Look through the layers and carry on until found,
            // or we have reached the end of the list.
            while ((pLayer != null) && !blFoundit)
            {
                if (!(pLayer is ICompositeLayer))
                {
                    if (pLayer.Name == aName)
                    {
                        pTargetLayer = pLayer;
                        blFoundit = true;
                    }
                }
                pLayer = pLayers.Next();
            }

            if (pTargetLayer == null)
            {
                if (Messages) MessageBox.Show("The layer " + aName + " doesn't exist", "Find Layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetLayer returned the following error: The layer " + aName + " doesn't exist.");
                return null;
            }
            return pTargetLayer;
        }

        #region FieldExists
        public bool FieldExists(string aFilePath, string aDatasetName, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            // This function returns true if a field (or a field alias) exists, false if it doesn (or the dataset doesn't)
            IFeatureClass myFC = GetFeatureClass(aFilePath, aDatasetName, aLogFile, Messages);
            ITable myTab;
            if (myFC == null)
            {
                myTab = GetTable(aFilePath, aDatasetName, Messages);

                if (myTab == null)
                {
                    if (Messages)
                        MessageBox.Show("Cannot check for field in dataset " + aFilePath + @"\" + aDatasetName + ". Dataset does not exist");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function FieldExists returned the following error: Cannot check for field in dataset " + aFilePath + @"\" + aDatasetName + ". Dataset does not exist");
                    return false; // Dataset doesn't exist.
                }
            }
            else
            {
                myTab = (ITable)myFC;
            }

            int aTest;
            IFields theFields = myTab.Fields;
            aTest = theFields.FindField(aFieldName);
            if (aTest == -1)
            {
                aTest = theFields.FindFieldByAliasName(aFieldName);
            }

            if (aTest == -1) return false;
            return true;
        }

        public bool FieldExists(IFeatureClass aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {

            IFields theFields = aFeatureClass.Fields;
            return FieldExists(theFields, aFieldName, aLogFile, Messages);
        }

        public bool FieldExists(IFields theFields, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            int aTest;
            aTest = theFields.FindField(aFieldName);
            if (aTest == -1)
                aTest = theFields.FindFieldByAliasName(aFieldName);
            if (aTest == -1) return false;
            return true;
        }

        public bool FieldExists(string aFeatureClassOrLayer, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            if (FeatureclassExists(aFeatureClassOrLayer))
            {
                string aFilePath = myFileFuncs.GetDirectoryName(aFeatureClassOrLayer);
                string aDatasetName = myFileFuncs.GetFileName(aFeatureClassOrLayer);
                return FieldExists(aFilePath, aDatasetName, aFieldName, aLogFile, Messages);
            }
            else if (LayerExists(aFeatureClassOrLayer))
            {
                ILayer pLayer = GetLayer(aFeatureClassOrLayer);
                return FieldExists(pLayer, aFieldName, aLogFile, Messages);
            }
            else
                return false;
        }

        public bool FieldExists(ILayer aLayer, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            IFeatureLayer pFL = null;
            try
            {
                pFL = (IFeatureLayer)aLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldExists returned the following error: The input layer aLayer is not a feature layer.");
                return false;
            }
            IFeatureClass pFC = pFL.FeatureClass;
            return FieldExists(pFC, aFieldName);
        }
        #endregion

        public bool CheckFieldType(string aLayerOrFeatureClass, string aFieldName, string anExpectedType, string aLogFile = "", bool Messages = false)
        {
            // The following field types are recognised:
            // TEXT, FLOAT, DOUBLE, SHORT, LONG, DATE.
            bool blResult = false;
            IFeatureClass aFC = null;
            if (LayerExists(aLayerOrFeatureClass))
                aFC = GetFeatureClassFromLayerName(aLayerOrFeatureClass);
            else if (FeatureclassExists(aLayerOrFeatureClass))
                aFC = GetFeatureClass(aLayerOrFeatureClass);
            else
            {
                if (Messages) MessageBox.Show("The featureclass or layer " + aLayerOrFeatureClass + " doesn't exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "") myFileFuncs.WriteLine(aLogFile, "Function CheckFieldType returned the following error: The featureclass or layer " + aLayerOrFeatureClass + " doesn't exist.");
                return blResult;
            }
            IField aField = GetFCField(aFC, aFieldName, aLogFile, Messages);
            if (aField == null)
            {
                if (Messages) MessageBox.Show("The field " + aFieldName + " doesn't exist in " + aLayerOrFeatureClass, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "") myFileFuncs.WriteLine(aLogFile, "Function CheckFieldType returned the following error: The field " + aFieldName + " doesn't exist in " + aLayerOrFeatureClass + ".");
                return blResult;
            }
            // 
            switch (anExpectedType)
            {
                case "TEXT":
                    if (aField.Type == esriFieldType.esriFieldTypeString)
                        blResult = true;
                    else
                        blResult = false;
                    break;
                case "FLOAT":
                    if (aField.Type == esriFieldType.esriFieldTypeSingle)
                        blResult = true;
                    else
                        blResult = false;
                    break;
                case "DOUBLE":
                    if (aField.Type == esriFieldType.esriFieldTypeDouble)
                        blResult = true;
                    else
                        blResult = false;
                    break;  
                case "SHORT":
                    if (aField.Type == esriFieldType.esriFieldTypeSmallInteger)
                        blResult = true;
                    else
                        blResult = false;
                    break;
                case "LONG":
                    if (aField.Type == esriFieldType.esriFieldTypeInteger)
                        blResult = true;
                    else
                        blResult = false;
                    break;
                case "DATE":
                    if (aField.Type == esriFieldType.esriFieldTypeDate)
                        blResult = true;
                    else
                        blResult = false;
                    break;
            }
            return blResult;
        }

        public bool FieldIsNumeric(string aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            // Check the obvious.
            if (!FeatureclassExists(aFeatureClass))
            {
                if (Messages)
                    MessageBox.Show("The featureclass " + aFeatureClass + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldIsNumeric returned the following error: The featureclass " + aFeatureClass + " doesn't exist");
                return false;
            }

            if (!FieldExists(aFeatureClass, aFieldName))
            {
                if (Messages)
                    MessageBox.Show("The field " + aFieldName + " does not exist in featureclass " + aFeatureClass);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldIsNumeric returned the following error: the field " + aFieldName + " does not exist in feature class " + aFeatureClass);
                return false;
            }

            IField pField = GetFCField(aFeatureClass, aFieldName);
            if (pField == null)
            {
                if (Messages) MessageBox.Show("The field " + aFieldName + " does not exist in this layer", "Field Is Numeric");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldIsNumeric returned the following error: the field " + aFieldName + " does not exist in this layer");
                return false;
            }
            bool blResult = false;
            if (pField.Type == esriFieldType.esriFieldTypeDouble |
                pField.Type == esriFieldType.esriFieldTypeInteger |
                pField.Type == esriFieldType.esriFieldTypeSingle |
                pField.Type == esriFieldType.esriFieldTypeSmallInteger) blResult = true;

            pField = null;
            return blResult;

        }

        public bool FieldIsNumeric(IFeatureClass aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            bool blResult = false;

            int anIndex = aFeatureClass.FindField(aFieldName);
            if (anIndex < 0)
            {
                if (Messages) MessageBox.Show("Field " + aFieldName + " does not exist in this feature class", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "") myFileFuncs.WriteLine(aLogFile, "The function FieldIsNumeric returned the following error: Field " + aFieldName + " does not exist in this feature class");
                return blResult;
            }

            IField pField = aFeatureClass.Fields.get_Field(anIndex);

            if (pField.Type == esriFieldType.esriFieldTypeDouble |
                pField.Type == esriFieldType.esriFieldTypeInteger |
                pField.Type == esriFieldType.esriFieldTypeSingle |
                pField.Type == esriFieldType.esriFieldTypeSmallInteger) blResult = true;

            pField = null;
            return blResult;
        }

        public bool FieldIsString(IFeatureClass aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            bool blResult = false;

            int anIndex = aFeatureClass.FindField(aFieldName);
            if (anIndex < 0)
            {
                if (Messages) MessageBox.Show("Field " + aFieldName + " does not exist in this feature class", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "") myFileFuncs.WriteLine(aLogFile, "The function FieldIsNumeric returned the following error: Field " + aFieldName + " does not exist in this feature class");
                return blResult;
            }

            IField pField = aFeatureClass.Fields.get_Field(anIndex);

            if (pField.Type == esriFieldType.esriFieldTypeString) blResult = true;
            pField = null;
            return blResult;
        }

        public bool FieldIsDate(string aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            // Check the obvious.
            if (!FeatureclassExists(aFeatureClass))
            {
                if (Messages)
                    MessageBox.Show("The featureclass " + aFeatureClass + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldIsNumeric returned the following error: The featureclass " + aFeatureClass + " doesn't exist");
                return false;
            }

            if (!FieldExists(aFeatureClass, aFieldName))
            {
                if (Messages)
                    MessageBox.Show("The field " + aFieldName + " does not exist in featureclass " + aFeatureClass);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldIsNumeric returned the following error: the field " + aFieldName + " does not exist in feature class " + aFeatureClass);
                return false;
            }

            bool blResult = false;
            IField pField = GetFCField(aFeatureClass, aFieldName);
            if (pField == null)
            {
                if (Messages) MessageBox.Show("The field " + aFieldName + " does not exist in this layer", "Field Is Numeric");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function FieldIsNumeric returned the following error: the field " + aFieldName + " does not exist in this layer");
                return false;
            }

            if (pField.Type == esriFieldType.esriFieldTypeDate) blResult = true;
            pField = null;
            return blResult;

        }

        public bool FieldIsDate(IFeatureClass aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            bool blResult = false;

            int anIndex = aFeatureClass.FindField(aFieldName);
            if (anIndex < 0)
            {
                if (Messages) MessageBox.Show("Field " + aFieldName + " does not exist in this feature class", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "") myFileFuncs.WriteLine(aLogFile, "The function FieldIsNumeric returned the following error: Field " + aFieldName + " does not exist in this feature class");
                return blResult;
            }

            IField pField = aFeatureClass.Fields.get_Field(anIndex);

            if (pField.Type == esriFieldType.esriFieldTypeDate) blResult = true;

            pField = null;
            return blResult;
        }

        #region AddField
        public bool AddField(ref IFeatureClass aFeatureClass, string aFieldName, esriFieldType aFieldType, int aLength, string aLogFile = "", bool Messages = false)
        {
            // Validate input.
            if (aFeatureClass == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Please pass a valid feature class", "Add Field");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddField returned the following error: Please pass a valid feature class");
                return false;
            }
            if (aLength <= 0)
            {
                if (Messages)
                {
                    MessageBox.Show("Please enter a valid field length", "Add Field");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddField returned the following error: Please pass a valid field length");
                return false;
            }
            IFields pFields = aFeatureClass.Fields;
            int i = pFields.FindField(aFieldName);
            if (i > -1)
            {
                if (Messages)
                {
                    MessageBox.Show("This field already exists", "Add Field");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddField returned the following message: The field " + aFieldName + " already exists");
                return false;
            }

            ESRI.ArcGIS.Geodatabase.Field aNewField = new ESRI.ArcGIS.Geodatabase.Field();
            IFieldEdit anEdit = (IFieldEdit)aNewField;

            anEdit.AliasName_2 = aFieldName;
            anEdit.Name_2 = aFieldName;
            anEdit.Type_2 = aFieldType;
            anEdit.Length_2 = aLength;

            aFeatureClass.AddField(aNewField);
            return true;
        }

        public bool AddField(string aFeatureClass, string aFieldName, esriFieldType aFieldType, int aLength, string aLogFile = "", bool Messages = false)
        {
            IFeatureClass pFC = GetFeatureClass(aFeatureClass, aLogFile, Messages);
            bool blResult = AddField(ref pFC, aFieldName, aFieldType, aLength, aLogFile, Messages);
            pFC = null;
            return blResult;
        }

        public bool AddField(string aFeatureClass, string aFieldName, string aFieldType, int aLength, string aLogFile = "", bool Messages = false)
        {
            // This takes a strict list of field type strings:
            List<string> FieldTypes = new List<string>() { "TEXT", "FLOAT", "DOUBLE", "SHORT", "LONG", "DATE" };
            List<esriFieldType> EsriTypes = new List<esriFieldType>() {esriFieldType.esriFieldTypeString, esriFieldType.esriFieldTypeSingle, 
                                                                       esriFieldType.esriFieldTypeDouble, esriFieldType.esriFieldTypeSmallInteger,
                                                                       esriFieldType.esriFieldTypeInteger, esriFieldType.esriFieldTypeDate};

            if (!FieldTypes.Contains(aFieldType))
            {
                if (Messages) MessageBox.Show("The fieldtype " + aFieldType + " is not a valid type", "Add Field");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddField returned the following error: The fieldtype " + aFieldType + " is not a valid type");
                return false;
            }

            esriFieldType theType = esriFieldType.esriFieldTypeString; // the default.
            int a = 0;
            foreach (string aType in FieldTypes)
            {
                if (aType == aFieldType)
                {
                    theType = EsriTypes[a];
                    break;
                }
                a++;
            }
            bool blResult = AddField(aFeatureClass, aFieldName, theType, aLength, aLogFile, Messages);
            FieldTypes = null;
            EsriTypes = null;
            return blResult;
        }
        #endregion

        public bool AddLayerField(string aLayer, string aFieldName, esriFieldType aFieldType, int aLength, string aLogFile = "", bool Messages = false)
        {
            if (!LayerExists(aLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " could not be found in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddLayerField returned the following error: The layer " + aLayer + " could not be found in the map");
                return false;
            }

            ILayer pLayer = GetLayer(aLayer);
            IFeatureLayer pFL;
            try
            {
                pFL = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("Layer " + aLayer + " is not a feature layer.");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddLayerField returned the following error: The layer " + aLayer + " is not a feature layer");
                return false;
            }

            IFeatureClass pFC = pFL.FeatureClass;
            AddField(ref pFC, aFieldName, aFieldType, aLength, aLogFile, Messages);
            pFC = null;
            pFL = null;
            return true;
        }

        public bool DeleteLayerField(string aLayer, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            if (!LayerExists(aLayer))
            {
                if (Messages) MessageBox.Show("The layer " + aLayer + " doesn't exist in this map.");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteLayerField returned the following error: The layer " + aLayer + " could not be found in the map");
                return false;
            }

            ILayer pLayer = GetLayer(aLayer);
            if (!FieldExists(pLayer, aFieldName, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The field " + aFieldName + " doesn't exist in layer " + aLayer);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteLayerField returned the following error: The field " + aFieldName + " doesn't exist in layer " + aLayer);
                pLayer = null;
                return false;
            }
            pLayer = null;

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aLayer);
            parameters.Add(aFieldName);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("DeleteField_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteLayerField returned the following error: " + ex.Message);
                gp = null;
                return false;
            }
        }

        public bool CreateIndex(string aLayerOrFeatureClass, string IndexFields, string IndexName, string aLogFile = "", bool Messages = false)
        {
            bool blResult = false;
            if (!LayerOrFeatureclassExists(aLayerOrFeatureClass))
            {
                if (Messages) MessageBox.Show("Layer or feature class " + aLayerOrFeatureClass + " doesn't exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CreateIndex returned the following error: Layer or feature class " + aLayerOrFeatureClass + " doesn't exist.");
                return blResult;
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aLayerOrFeatureClass);
            parameters.Add(IndexFields);
            parameters.Add(IndexName);

            // Execute the tool.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("AddIndex_management", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "Function CreateIndex returned the following errors: " + ex.Message);
                        myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                    }

                }
            }
            finally
            {
                gp = null;
                myresult = null;
                sev = null;
                parameters = null;
            }
            return blResult;
        }
        
        public bool AddLayerFromFClass(IFeatureClass theFeatureClass, string aLogFile = "", bool Messages = false)
        {
            // Check we have input
            if (theFeatureClass == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Please pass a feature class", "Add Layer From Feature Class");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddLayerFromFClass returned the following error: Please pass a feature class");
                return false;
            }
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages)
                {
                    MessageBox.Show("No map found", "Add Layer From Feature Class");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddLayerFromFClass returned the following error: No map found");
                return false;
            }
            FeatureLayer pFL = new FeatureLayer();
            pFL.FeatureClass = theFeatureClass;
            pFL.Name = theFeatureClass.AliasName;
            pMap.AddLayer(pFL);

            return true;
        }

        public bool AddFeatureLayerFromString(string aFeatureClassName, string aLogFile = "", bool Messages = false)
        {
            // firstly get the Feature Class
            // Does it exist?
            if (!myFileFuncs.FileExists(aFeatureClassName))
            {
                if (Messages)
                {
                    MessageBox.Show("The featureclass " + aFeatureClassName + " does not exist");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddFeatureLayerFromString returned the following error: The featureclass " + aFeatureClassName + " does not exist");
                return false;
            }
            string aFilePath = myFileFuncs.GetDirectoryName(aFeatureClassName);
            string aFCName = myFileFuncs.GetFileName(aFeatureClassName);

            IFeatureClass myFC = GetFeatureClass(aFilePath, aFCName, aLogFile, Messages);
            if (myFC == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Cannot open featureclass " + aFeatureClassName);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddFeatureLayerFromString returned the following error: Cannot open featureclass " + aFeatureClassName);
                return false;
            }

            // Now add it to the view.
            bool blResult = AddLayerFromFClass(myFC, aLogFile, Messages);
            if (blResult)
            {
                return true;
            }
            else
            {
                if (Messages)
                {
                    MessageBox.Show("Cannot add featureclass " + aFeatureClassName);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddFeatureLayerFromString returned the following error: Cannot add featureclass " + aFeatureClassName);
                return false;
            }
        }

        #region TableExists
        public bool TableExists(string aFilePath, string aDatasetName)
        {

            if (aDatasetName.Substring(aDatasetName.Length - 4, 1) == ".")
            {
                // it's a file.
                if (myFileFuncs.FileExists(aFilePath + @"\" + aDatasetName))
                    return true;
                else
                    return false;
            }
            else if (aFilePath.Substring(aFilePath.Length - 3, 3) == "sde")
            {
                // It's an SDE class
                // Not handled. We know the table exists.
                return true;
            }
            else // it is a geodatabase class.
            {
                IWorkspaceFactory pWSF = GetWorkspaceFactory(aFilePath);
                IWorkspace2 pWS = (IWorkspace2)pWSF.OpenFromFile(aFilePath, 0);
                if (pWS.get_NameExists(ESRI.ArcGIS.Geodatabase.esriDatasetType.esriDTTable, aDatasetName))
                    return true;
                else
                    return false;
            }
        }

        public bool TableExists(string aFullPath)
        {
            return TableExists(myFileFuncs.GetDirectoryName(aFullPath), myFileFuncs.GetFileName(aFullPath));
        }
        #endregion

        #region GetTable
        public ITable GetTable(string aFilePath, string aDatasetName, string aLogFile = "", bool Messages = false)
        {
            // Check input first.
            string aTestPath = aFilePath;
            if (aFilePath.Contains(".sde"))
            {
                aTestPath = myFileFuncs.GetDirectoryName(aFilePath);
            }
            if (myFileFuncs.DirExists(aTestPath) == false || aDatasetName == null)
            {
                if (Messages) MessageBox.Show("Please provide valid input", "Get Table");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetTable returned the following error: Please provide valid input");
                return null;
            }
            bool blText = false;
            string strExt = aDatasetName.Substring(aDatasetName.Length - 4, 4);
            if (strExt == ".txt" || strExt == ".csv" || strExt == ".tab")
            {
                blText = true;
            }

            IWorkspaceFactory pWSF = GetWorkspaceFactory(aFilePath, blText);
            IFeatureWorkspace pWS = (IFeatureWorkspace)pWSF.OpenFromFile(aFilePath, 0);
            ITable pTable = pWS.OpenTable(aDatasetName);
            if (pTable == null)
            {
                if (Messages) MessageBox.Show("The file " + aDatasetName + " doesn't exist in this location", "Open Table from Disk");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetTable returned the following error: The file " + aDatasetName + " doesn't exist in this location");
                Marshal.ReleaseComObject(pWS);
                pWSF = null;
                pWS = null;
                GC.Collect();
                return null;
            }
            Marshal.ReleaseComObject(pWS);
            pWSF = null;
            pWS = null;
            GC.Collect();
            return pTable;
        }

        public ITable GetTable(string aTableLayer, string aLogFile = "", bool Messages = false)
        {
            IMap pMap = GetMap();
            IStandaloneTableCollection pColl = (IStandaloneTableCollection)pMap;
            IStandaloneTable pThisTable = null;

            for (int I = 0; I < pColl.StandaloneTableCount; I++)
            {
                pThisTable = pColl.StandaloneTable[I];
                if (pThisTable.Name == aTableLayer)
                {
                    ITable myTable = pThisTable.Table;
                    return myTable;
                }
            }
            if (Messages)
            {
                MessageBox.Show("The table layer " + aTableLayer + " could not be found in this map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetTable returned the following error: The table layer " + aTableLayer + " could not be found in this map");
            }
            return null;
        }
        #endregion

        public bool AddTableLayerFromString(string aTableName, string aLayerName, string aLogFile = "", bool Messages = false)
        {
            // firstly get the Table
            // Does it exist? // Does not work for GeoDB tables!!
            if (!myFileFuncs.FileExists(aTableName))
            {
                if (Messages)
                {
                    MessageBox.Show("The table " + aTableName + " does not exist");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function AddTableLayerFromString returned the following error: The table " + aTableName + " does not exist");
                }
                return false;
            }
            string aFilePath = myFileFuncs.GetDirectoryName(aTableName);
            string aTabName = myFileFuncs.GetFileName(aTableName);

            ITable myTable = GetTable(aFilePath, aTabName, aLogFile, Messages);
            if (myTable == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Cannot open table " + aTableName);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddTableLayerFromString returned the following error: Cannot open table " + aTableName);
                return false;
            }

            // Now add it to the view.
            bool blResult = AddLayerFromTable(myTable, aLayerName);
            if (blResult)
            {
                return true;
            }
            else
            {
                if (Messages)
                {
                    MessageBox.Show("Cannot add table " + aTabName);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddTableLayerFromString returned the following error: Cannot add table " + aTableName);
                return false;
            }
        }

        public bool AddLayerFromTable(ITable theTable, string aName, string aLogFile = "", bool Messages = false)
        {
            // check we have input
            if (theTable == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Please pass a table", "Add Layer From Table");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddLayerFromTable returned the following error: Please pass a table");
                return false;
            }
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages)
                {
                    MessageBox.Show("No map found", "Add Layer From Table");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddLayerFromTable returned the following error: No map found");
                return false;
            }
            IStandaloneTableCollection pStandaloneTableCollection = (IStandaloneTableCollection)pMap;
            IStandaloneTable pTable = new StandaloneTable();
            IMxDocument mxDoc = GetIMXDocument();

            pTable.Table = theTable;
            pTable.Name = aName;

            // Remove if already exists
            if (TableLayerExists(aName, aLogFile, Messages))
                RemoveStandaloneTable(aName, aLogFile, Messages);

            mxDoc.UpdateContents();
            
            pStandaloneTableCollection.AddStandaloneTable(pTable);
            mxDoc.UpdateContents();
            return true;
        }

        public bool TableLayerExists(string aLayerName, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aLayerName == null)
            {
                if (Messages) MessageBox.Show("Please pass a valid layer name", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function TableLayerExists returned the following error: Please pass a valid layer name");
                return false;
            }

            // Get map, and layer names.
            IMxDocument mxDoc = GetIMXDocument();
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function TableLayerExists returned the following error: No map found");
                return false;
            }

            IStandaloneTableCollection pColl = (IStandaloneTableCollection)pMap;
            IStandaloneTable pThisTable = null;
            for (int I = 0; I < pColl.StandaloneTableCount; I++)
            {
                pThisTable = pColl.StandaloneTable[I];
                if (pThisTable.Name == aLayerName)
                {
                    return true;
                }
            }
            return false;
        }

        public bool RemoveStandaloneTable(string aTableName, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aTableName == null)
            {
                if (Messages) MessageBox.Show("Please pass a valid table name", "Remove Standalone Table");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveStandaloneTable returned the following error: Please pass a valid table name");
                return false;
            }

            // Get map, and layer names.
            IMxDocument mxDoc = GetIMXDocument();
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveStandaloneTable returned the following error: No map found");
                return false;
            }

            IStandaloneTableCollection pColl = (IStandaloneTableCollection)pMap;
            IStandaloneTable pThisTable = null;
            for (int I = 0; I < pColl.StandaloneTableCount; I++)
            {
                pThisTable = pColl.StandaloneTable[I];
                if (pThisTable.Name == aTableName)
                {
                    try
                    {
                        pColl.RemoveStandaloneTable(pThisTable);
                        mxDoc.UpdateContents();
                        return true; // important: get out now, the index is no longer valid
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        if (aLogFile != "")
                            myFileFuncs.WriteLine(aLogFile, "Function RemoveStandaloneTable returned the following error: " + ex.Message);
                        return false;
                    }
                }
            }
            return false;
        }

        public bool LayerExists(string aLayerName, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aLayerName == null)
            {
                if (Messages) MessageBox.Show("Please pass a valid layer name", "Layer Exists");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function LayerExists returned the following error: Please pass a valid layer name");
                return false;
            }

            // Get map, and layer names.
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Layer Exists");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function LayerExists returned the following error: No map found");
                return false;
            }
            IEnumLayer pLayers = pMap.Layers;

            ILayer pLayer = pLayers.Next();

            // Look through the layers and carry on until found,
            // or we have reached the end of the list.
            while (pLayer != null)
            {
                if (!(pLayer is IGroupLayer))
                {
                    if (pLayer.Name == aLayerName)
                    {
                        // Check that the data is there
                        if (pLayer.Valid)
                            return true;
                        else
                            return false;
                    }

                }
                pLayer = pLayers.Next();
            }
            return false;
        }

        public bool GroupLayerExists(string aGroupLayerName, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aGroupLayerName == null)
            {
                if (Messages) MessageBox.Show("Please pass a valid layer name", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GroupLayerExists returned the following error: Please pass a valid layer name");
                return false;
            }

            // Get map, and layer names.
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GroupLayerExists returned the following error: No map found");
                return false;
            }
            IEnumLayer pLayers = pMap.Layers;

            ILayer pLayer = pLayers.Next();

            // Look through the layers and carry on until found,
            // or we have reached the end of the list.
            while (pLayer != null)
            {
                if (pLayer is IGroupLayer)
                {
                    if (pLayer.Name == aGroupLayerName)
                    {
                        return true;
                    }

                }
                pLayer = pLayers.Next();
            }
            return false;
        }

        public ILayer GetGroupLayer(string aGroupLayerName, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aGroupLayerName == null)
            {
                if (Messages) MessageBox.Show("Please pass a valid layer name", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetGroupLayer returned the following error: Please pass a valid layer name");
                return null;
            }

            // Get map, and layer names.
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Find Layer By Name");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetGroupLayer returned the following error: No map found");
                return null;
            }
            IEnumLayer pLayers = pMap.Layers;

            ILayer pLayer = pLayers.Next();

            // Look through the layers and carry on until found,
            // or we have reached the end of the list.
            while (pLayer != null)
            {
                if (pLayer is IGroupLayer)
                {
                    if (pLayer.Name == aGroupLayerName)
                    {
                        return pLayer;
                    }

                }
                pLayer = pLayers.Next();
            }
            return null;
        }

        public bool LayerExistsInGroupLayer(string LayerName, string GroupLayerName, string aLogFile = "", bool Messages = false)
        {
            if (!GroupLayerExists(GroupLayerName))
            {
                if (Messages) MessageBox.Show("Group layer " + GroupLayerName + " doesn't exist", "Layer Exists In Group");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function LayerExistsInGroupLayer returned the following error: Group layer " + GroupLayerName + " doesn't exist");
            }

            if (!LayerExists(LayerName))
            {
                if (Messages) MessageBox.Show("Layer " + LayerName + " doesn't exist", "Layer Exists In Group");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function LayerExistsInGroupLayer returned the following error: Layer " + LayerName + " doesn't exist");
            }

            ICompositeLayer myCL = (ICompositeLayer)GetGroupLayer(GroupLayerName);
            bool blFoundIt = false;
            for (int i = 0; i < myCL.Count; i++)
            {
                ILayer pLayer = myCL.Layer[i];
                if (pLayer.Name == LayerName)
                    blFoundIt = true;
            }

            return blFoundIt;
        }

        public List<ILayer> GetLayersInGroup(string GroupLayerName)
        {
            // Untested
            ICompositeLayer myCL = (ICompositeLayer)GetGroupLayer(GroupLayerName);
            List<ILayer> theLayers = new List<ILayer>();
            for (int i = 0; i < myCL.Count; i++)
            {
                theLayers.Add(myCL.Layer[i]);
            }
            return theLayers;
        }

        public bool MoveToGroupLayer(string theGroupLayerName, ILayer aLayer, string aLogFile = "", bool Messages = false)
        {
            bool blExists = false;
            IGroupLayer myGroupLayer = new GroupLayer(); 
            // Does the group layer exist?
            if (GroupLayerExists(theGroupLayerName, aLogFile, Messages))
            {
                myGroupLayer = (IGroupLayer)GetGroupLayer(theGroupLayerName, aLogFile, Messages);
                blExists = true;
                
            }
            else
            {
                myGroupLayer.Name = theGroupLayerName;
            }
            string theOldName = aLayer.Name;

            // Remove the original instance, then add it to the group.
            RemoveLayer(aLayer, aLogFile, Messages);
            myGroupLayer.Add(aLayer);
            
            if (!blExists)
            {
                // Add the layer to the map.
                IMap pMap = GetMap();
                pMap.AddLayer(myGroupLayer);
            }
            RefreshTOC();
            return true;
        }

        public bool MoveToSubGroupLayer(string theGroupLayerName, string theSubGroupLayerName, ILayer aLayer, string aLogFile = "", bool Messages = false)
        {
            bool blGroupLayerExists = false;
            bool blSubGroupLayerExists = false;
            IGroupLayer myGroupLayer = new GroupLayer();
            IGroupLayer mySubGroupLayer = new GroupLayer();
            // Does the group layer exist?
            if (GroupLayerExists(theGroupLayerName))
            {
                myGroupLayer = (IGroupLayer)GetGroupLayer(theGroupLayerName, aLogFile, Messages);
                blGroupLayerExists = true;
            }
            else
            {
                myGroupLayer.Name = theGroupLayerName;
            }


            if (GroupLayerExists(theSubGroupLayerName, aLogFile, Messages))
            {
                mySubGroupLayer = (IGroupLayer)GetGroupLayer(theSubGroupLayerName, aLogFile, Messages);
                blSubGroupLayerExists = true;
            }
            else
            {
                mySubGroupLayer.Name = theSubGroupLayerName;
            }

            // Remove the original instance, then add it to the group.
            string theOldName = aLayer.Name; 
            RemoveLayer(aLayer, aLogFile, Messages);
            mySubGroupLayer.Add(aLayer);

            if (!blSubGroupLayerExists)
            {
                // Add the subgroup layer to the group layer.
                myGroupLayer.Add(mySubGroupLayer);
            }
            if (!blGroupLayerExists)
            {
                // Add the layer to the map.
                IMap pMap = GetMap();
                pMap.AddLayer(myGroupLayer);
            }
            RefreshTOC();
            return true;
        }

        #region RemoveLayer
        public bool RemoveLayer(string aLayerName, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aLayerName == null)
            {
                MessageBox.Show("Please pass a valid layer name", "Remove Layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveLayer returned the following error: Please pass a valid layer name");
                return false;
            }

            // Get map, and layer names.
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Remove Layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveLayer returned the following error: No map found");
                return false;
            }
            IEnumLayer pLayers = pMap.Layers;

            ILayer pLayer = pLayers.Next();

            // Look through the layers and carry on until found,
            // or we have reached the end of the list.
            bool blResult = false;
            while (pLayer != null)
            {
                if (!(pLayer is IGroupLayer))
                {
                    if (pLayer.Name == aLayerName)
                    {
                        pMap.DeleteLayer(pLayer);
                        blResult = true;
                        break;
                        //return true;
                    }

                }
                pLayer = pLayers.Next();
            }
            pLayer = null;
            pMap = null;
            pLayers = null;
            return blResult;
            //return false;
        }

        public bool RemoveLayer(ILayer aLayer, string aLogFile = "", bool Messages = false)
        {
            // Check there is input.
            if (aLayer == null)
            {
                MessageBox.Show("Please pass a valid layer ", "Remove Layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveLayer returned the following error: Please pass a valid layer name");
                return false;
            }

            // Get map, and layer names.
            IMap pMap = GetMap();
            if (pMap == null)
            {
                if (Messages) MessageBox.Show("No map found", "Remove Layer"); 
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveLayer returned the following error: No map found");
                return false;
            }
            pMap.DeleteLayer(aLayer);
            return true;
        }
        #endregion

        public string GetOutputFileName(string aFileType, string anInitialDirectory = @"C:\")
        {
            // This would be done better with a custom type but this will do for the momment.
            IGxDialog myDialog = new GxDialogClass();
            myDialog.set_StartingLocation(anInitialDirectory);
            IGxObjectFilter myFilter;


            //switch (aFileType)
            //{
            //    case "Geodatabase FC":
            //        myFilter = new GxFilterFGDBFeatureClasses();
            //        break;
            //    case "Geodatabase Table":
            //        myFilter = new GxFilterFGDBTables();
            //        break;
            //    case "Shapefile":
            //        myFilter = new GxFilterShapefiles();
            //        break;
            //    case "DBASE file":
            //        myFilter = new GxFilterdBASEFiles();
            //        break;
            //    case "Text file":
            //        myFilter = new GxFilterTextFiles();
            //        break;
            //    default:
            //        myFilter = new GxFilterDatasets();
            //        break;
            //}

            // Simplified version; shape or gdb only.
            if (aFileType == "gdb")
            {
                myFilter = new GxFilterFGDBFeatureClasses();
            }
            else if (aFileType == "shape")
            {
                myFilter = new GxFilterShapefiles();
            }
            else
            {
                myFilter = new GxFilterDatasets();
            }

            myDialog.ObjectFilter = myFilter;
            myDialog.Title = "Save Output As...";
            myDialog.ButtonCaption = "OK";

            string strOutFile = "None";
            if (myDialog.DoModalSave(thisApplication.hWnd))
            {
                strOutFile = myDialog.FinalLocation.FullName + @"\" + myDialog.Name;
            }
            myDialog = null;
            myFilter = null;
            return strOutFile; // "None" if user pressed exit
        }

        #region CopyFeatures
        public bool CopyFeatures(string InFeatureClassOrLayer, string OutFeatureClass, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            // This function can work on either feature classes or layers.
            bool blResult = false;
            if (!LayerOrFeatureclassExists(InFeatureClassOrLayer))
            {
                if (Messages) MessageBox.Show("Layer or feature class " + InFeatureClassOrLayer + " doesn't exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CreateIndex returned the following error: Layer or feature class " + InFeatureClassOrLayer + " doesn't exist.");
                return blResult;
            }

            
            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(InFeatureClassOrLayer);
            parameters.Add(OutFeatureClass);

            // Execute the tool.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("CopyFeatures_management", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "Function CopyFeatures returned the following errors: " + ex.Message);
                        myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                    }

                }
            }
            finally
            {
                gp = null;
                myresult = null;
                sev = null;
                parameters = null;
            }
            return blResult;
        }

        public bool CopyFeatures(string InWorkspace, string InDatasetName, string OutFeatureClass, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            string inFeatureClass = InWorkspace + @"\" + InDatasetName;
            return CopyFeatures(inFeatureClass, OutFeatureClass, aLogFile, Messages);
        }

        public bool CopyFeatures(string InWorkspace, string InDatasetName, string OutWorkspace, string OutDatasetName, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            string inFeatureClass = InWorkspace + @"\" + InDatasetName;
            string outFeatureClass = OutWorkspace + @"\" + OutDatasetName;
            return CopyFeatures(inFeatureClass, outFeatureClass, aLogFile, Messages);
        }
        #endregion

        #region ClipFeatures
        public bool ClipFeatures(string InFeatureClassOrLayer, string ClipFeatureClassOrLayer, string OutFeatureClass, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            // check the input
            if (!FeatureclassExists(InFeatureClassOrLayer) && !LayerExists(InFeatureClassOrLayer, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The input layer or feature class " + InFeatureClassOrLayer + " doesn't exist", "Clip Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The function ClipFeatures returned the following error: The input layer or feature class " + InFeatureClassOrLayer + " doesn't exist");
                return false;
            }

            if (!FeatureclassExists(ClipFeatureClassOrLayer) && !LayerExists(ClipFeatureClassOrLayer, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The clip layer or feature class " + ClipFeatureClassOrLayer + " doesn't exist", "Clip Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The function ClipFeatures returned the following error: The clip layer or feature class " + ClipFeatureClassOrLayer + " doesn't exist");
                return false;
            }

            if (!Overwrite && FeatureclassExists(OutFeatureClass))
            {
                if (Messages) MessageBox.Show("The output feature class " + OutFeatureClass + " already exists. Can't overwrite", "Clip Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The function ClipFeatures returned the following error: The output feature class " + OutFeatureClass + " already exists. Can't overwrite");
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(InFeatureClassOrLayer);
            parameters.Add(ClipFeatureClassOrLayer);
            parameters.Add(OutFeatureClass);

            // Execute the tool.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Clip_analysis", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function ClipFeatures returned the following errors: " + ex.Message);
                    myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                }
                gp = null;
                return false;
            }
        }

        public bool ClipFeatures(string InWorkspace, string InDatasetName, string ClipWorkspace, string ClipDatasetName, string OutFeatureClass, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            string inFeatureClass = InWorkspace + @"\" + InDatasetName;
            string ClipFeatureClass = ClipWorkspace + @"\" + ClipDatasetName;
            return ClipFeatures(inFeatureClass, ClipFeatureClass, OutFeatureClass, Overwrite, aLogFile, Messages);
        }

        public bool ClipFeatures(string InWorkspace, string InDatasetName, string ClipWorkspace, string ClipDatasetName, string OutWorkspace, string OutDatasetName, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            string inFeatureClass = InWorkspace + @"\" + InDatasetName;
            string clipFeatureClass = ClipWorkspace + @"\" + ClipDatasetName;
            string outFeatureClass = OutWorkspace + @"\" + OutDatasetName;
            return ClipFeatures(inFeatureClass, clipFeatureClass, outFeatureClass, Overwrite, aLogFile, Messages);
        }

        #endregion

        #region IntersectFeatures
        public bool IntersectFeatures(string InFeatureClassOrLayer, string IntersectFeatureClassOrLayer, string OutFeatureClass, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            // check the input
            if (!FeatureclassExists(InFeatureClassOrLayer) && !LayerExists(InFeatureClassOrLayer, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The input layer or feature class " + InFeatureClassOrLayer + " doesn't exist", "Intersect Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The function IntersectFeatures returned the following error: The input layer or feature class " + InFeatureClassOrLayer + " doesn't exist");
                return false;
            }

            if (!FeatureclassExists(IntersectFeatureClassOrLayer) && !LayerExists(IntersectFeatureClassOrLayer, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The Intersect layer or feature class " + IntersectFeatureClassOrLayer + " doesn't exist", "Intersect Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The function IntersectFeatures returned the following error: The intersect layer or feature class " + IntersectFeatureClassOrLayer + " doesn't exist");
                return false;
            }

            if (!Overwrite && FeatureclassExists(OutFeatureClass))
            {
                if (Messages) MessageBox.Show("The output feature class " + OutFeatureClass + " already exists. Can't overwrite", "Intersect Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The function IntersectFeatures returned the following error: The output feature class " + OutFeatureClass + " already exists. Can't overwrite");
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(String.Concat('"', InFeatureClassOrLayer, '"', ";", '"', IntersectFeatureClassOrLayer, '"'));
            parameters.Add(OutFeatureClass);
            parameters.Add("ALL");

            // Execute the tool.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Intersect_analysis", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function IntersectFeatures returned the following errors: " + ex.Message);
                    myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                }
                gp = null;
                return false;
            }
        }

        public bool IntersectFeatures(string InWorkspace, string InDatasetName, string IntersectWorkspace, string IntersectDatasetName, string OutFeatureClass, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            string inFeatureClass = InWorkspace + @"\" + InDatasetName;
            string IntersectFeatureClass = IntersectWorkspace + @"\" + IntersectDatasetName;
            return IntersectFeatures(inFeatureClass, IntersectFeatureClass, OutFeatureClass, Overwrite, aLogFile, Messages);
        }

        public bool IntersectFeatures(string InWorkspace, string InDatasetName, string IntersectWorkspace, string IntersectDatasetName, string OutWorkspace, string OutDatasetName, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            string inFeatureClass = InWorkspace + @"\" + InDatasetName;
            string intersectFeatureClass = IntersectWorkspace + @"\" + IntersectDatasetName;
            string outFeatureClass = OutWorkspace + @"\" + OutDatasetName;
            return IntersectFeatures(inFeatureClass, intersectFeatureClass, outFeatureClass, Overwrite, aLogFile, Messages);
        }

        #endregion

        public bool CopyTable(string InTable, string OutTable, bool Overwrite = true, string aLogFile = "", bool Messages = false)
        {
            // This works absolutely fine for dbf and geodatabase but does not export to CSV.
            if (!TableExists(InTable))
            {
                if (Messages) MessageBox.Show("The input table " + InTable + " doesn't exist.", "Copy Table");
                if (aLogFile !="")
                    myFileFuncs.WriteLine(aLogFile, "Function CopyTable returned the following error: The input table " + InTable + " doesn't exist");
                return false;
            }

            if (TableExists(OutTable) && !Overwrite)
            {
                if (Messages) MessageBox.Show("The output table " + OutTable + " already exists. Can't overwrite", "Copy Table");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CopyTable returned the following error: The output table " + OutTable + " already exists. Can't overwrite");
                return false;
            }

            // Note the csv export already removes ghe geometry field; in this case it is not necessary to check again.

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(InTable);
            parameters.Add(OutTable);

            // Execute the tool.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("CopyRows_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                    // Wait for 1 second.

                
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages) MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CopyTable returned the following errors: " + ex.Message);
                gp = null;
                return false;
            }
        }

        public bool AlterFieldAliasName(string aDatasetName, string aFieldName, string theAliasName, string aLogFile = "", bool Messages = false)
        {
            // This script changes the field alias of a the named field in the layer.
            // It assumes that all input is already checked (because it's pretty far down the line of a process).

            IObjectClass myObject = (IObjectClass)GetFeatureClass(aDatasetName);
            IClassSchemaEdit myEdit = (IClassSchemaEdit)myObject;
            try
            {
                myEdit.AlterFieldAliasName(aFieldName, theAliasName);
                myObject = null;
                myEdit = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AlterFieldAliasName returned the following error: " + ex.Message);
                myObject = null;
                myEdit = null;
                return false;
            }
        }

        public IField GetFCField(IFeatureClass aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            // Find the index of the requested field.
            int fieldIndex = aFeatureClass.FindField(aFieldName);

            // Get the field from the feature class's fields collection.
            if (fieldIndex > -1)
            {
                IFields fields = aFeatureClass.Fields;
                IField field = fields.get_Field(fieldIndex);
                return field;
            }
            else
            {
                if (Messages)
                {
                    MessageBox.Show("The field " + aFieldName + " was not found in the featureclass ");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFCField returned the following error: The field " + aFieldName + " was not found in the featureclass " );
                return null;
            }
        }

        public IField GetFCField(string InputDirectory, string FeatureclassName, string FieldName, string aLogFile = "", bool Messages = false)
        {
            IFeatureClass featureClass = GetFeatureClass(InputDirectory, FeatureclassName, aLogFile, Messages);
            return GetFCField(featureClass, FieldName, aLogFile, Messages);
        }

        public IField GetFCField(string aFeatureClass, string FieldName, string aLogFile = "", bool Messages = false)
        {
            string strInputDir = myFileFuncs.GetDirectoryName(aFeatureClass);
            string strInputShape = myFileFuncs.GetFileName(aFeatureClass);
            return GetFCField(strInputDir, strInputShape, FieldName, aLogFile, Messages);
        }

        public IField GetTableField(string TableName, string FieldName, string aLogFile = "", bool Messages = false)
        {
            ITable theTable = GetTable(myFileFuncs.GetDirectoryName(TableName), myFileFuncs.GetFileName(TableName), aLogFile, Messages);
            int fieldIndex = theTable.FindField(FieldName);

            // Get the field from the feature class's fields collection.
            if (fieldIndex > -1)
            {
                IFields fields = theTable.Fields;
                IField field = fields.get_Field(fieldIndex);
                return field;
            }
            else
            {
                if (Messages)
                {
                    MessageBox.Show("The field " + FieldName + " was not found in the table " + TableName);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetTableField returned the following error: The field " + FieldName + " was not found in the table " + TableName);
                return null;
            }
        }

        public bool AppendFeatures(string aLayerOrFeatureClass, string aTargetLayerOrFeatureClass, string aLogFile = "", bool Messages = false)
        {
            bool blResult = false;
            // Check the input.
            if (!LayerOrFeatureclassExists(aLayerOrFeatureClass))
            {
                if (Messages) MessageBox.Show("The input " + aLayerOrFeatureClass + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendFeatures returned the following error: The input table " + aLayerOrFeatureClass + " doesn't exist");
                return blResult;
            }

            if (!LayerOrFeatureclassExists(aTargetLayerOrFeatureClass))
            {
                if (Messages) MessageBox.Show("The target table " + aTargetLayerOrFeatureClass + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: The target table table " + aTargetLayerOrFeatureClass + " doesn't exist");
                return blResult;
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aLayerOrFeatureClass);
            parameters.Add(aTargetLayerOrFeatureClass);

            // Execute the tool.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Append_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: " + ex.Message);
            }

            gp = null;
            parameters = null;
            return blResult;
        }

        public bool AppendTable(string InTable, string TargetTable, string aLogFile = "", bool Messages = false)
        {
            // Check the input.
            if (!TableExists(InTable))
            {
                if (Messages) MessageBox.Show("The input table " + InTable + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: The input table " + InTable + " doesn't exist");
                return false;
            }

            if (!TableExists(TargetTable))
            {
                if (Messages) MessageBox.Show("The target table " + TargetTable + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: The target table table " + TargetTable + " doesn't exist");
                return false;
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(InTable);
            parameters.Add(TargetTable);

            // Execute the tool. Note this only works with geodatabase tables.
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Append_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: " + ex.Message);
                gp = null;
                return false;
            }
        }

        public int CopyToCSV(string InTable, string OutTable, string Columns, string OrderByColumns, bool Spatial, bool Append, bool ExcludeHeader = false, string aLogFile = "", bool Messages = false)
        {
            // This sub copies the input table to CSV.
            // Changed 29/11/2016 to no longer include the where clause - this has already been taken care of when 
            // selecting features and refining this selection.

            // Check the input.
            
            if (!TableExists(InTable))
            {
                if (Messages) MessageBox.Show("The input table " + InTable + " doesn't exist", "Copy To CSV");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: The input table " + InTable + " doesn't exist");
                return -1;
            }
            

            string aFilePath = myFileFuncs.GetDirectoryName(InTable);
            string aTabName = myFileFuncs.GetFileName(InTable);

            
            ITable pTable = GetTable(myFileFuncs.GetDirectoryName(InTable), myFileFuncs.GetFileName(InTable), aLogFile, Messages);

            ICursor myCurs = null;
            IFields fldsFields = null;
            if (Spatial)
            {
                
                IFeatureClass myFC = GetFeatureClass(aFilePath, aTabName, aLogFile, Messages); 
                myCurs = (ICursor)myFC.Search(null, false);
                fldsFields = myFC.Fields;
            }
            else
            {
                ITable myTable = GetTable(aFilePath, aTabName, aLogFile, Messages);
                myCurs = myTable.Search(null, false);
                fldsFields = myTable.Fields;
            }

            if (myCurs == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Cannot open table " + InTable);
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AppendTable returned the following error: Cannot open table " + InTable);
                return -1;
            }

            // Align the columns with what actually exists in the layer.
            // Return if there are no columns left.
            
            if (Columns != "")
            {
                List<string> strColumns = Columns.Split(',').ToList();
                Columns = "";
                foreach (string strCol in strColumns)
                {
                    string aColNameTr = strCol.Trim();
                    if ((aColNameTr.Substring(0, 1) == "\"") || (FieldExists(fldsFields, aColNameTr)))
                        Columns = Columns + aColNameTr + ",";
                }
                if (Columns != "")
                    Columns = Columns.Substring(0, Columns.Length - 1);
                else
                    return 0;
            }
            else
                return 0; // Technically we're finished as there is nothing to write.

            if (OrderByColumns != "")
            {
                List<string> strOrderColumns = OrderByColumns.Split(',').ToList();
                OrderByColumns = "";
                foreach (string strCol in strOrderColumns)
                {
                    if (FieldExists(fldsFields, strCol.Trim()))
                        OrderByColumns = OrderByColumns + strCol.Trim() + ",";
                }
                if (OrderByColumns != "")
                {
                    OrderByColumns = OrderByColumns.Substring(0, OrderByColumns.Length - 1);

                    ITableSort pTableSort = new TableSortClass();
                    pTableSort.Table = pTable;
                    pTableSort.Cursor = myCurs; 
                    pTableSort.Fields = OrderByColumns;

                    pTableSort.Sort(null);

                    myCurs = pTableSort.Rows;
                    Marshal.ReleaseComObject(pTableSort);
                    pTableSort = null;
                    GC.Collect();
                }
            }

            // Open output file.
            StreamWriter theOutput = new StreamWriter(OutTable, Append);
            List<string> ColumnList = Columns.Split(',').ToList();
            int intLineCount = 0;
            if (!Append && !ExcludeHeader)
            {
                string strHeader = Columns;
                theOutput.WriteLine(strHeader);
            }
            // Now write the file.
            IRow aRow = myCurs.NextRow();

            while (aRow != null)
            {
                string strRow = "";
                intLineCount++;
                foreach (string aColName in ColumnList)
                {
                    string aColNameTr = aColName.Trim();
                    if (aColNameTr.Substring(0, 1) != "\"")
                    {
                        int i = fldsFields.FindField(aColNameTr);
                        if (i == -1) i = fldsFields.FindFieldByAliasName(aColNameTr);
                        var theValue = aRow.get_Value(i);
                        // Wrap value if quotes if it is a string that contains a comma
                        if ((theValue is string) &&
                           (theValue.ToString().Contains(","))) theValue = "\"" + theValue.ToString() + "\"";
                        // Format distance to the nearest metre
                        if (theValue is double && aColNameTr == "Distance")
                        {
                            double dblValue = double.Parse(theValue.ToString());
                            int intValue = Convert.ToInt32(dblValue);
                            theValue = intValue;
                        }
                        strRow = strRow + theValue.ToString() + ",";
                    }
                    else
                    {
                        strRow = strRow + aColNameTr +",";
                    }
                    
                }

                strRow = strRow.Substring(0, strRow.Length - 1); // Remove final comma.

                theOutput.WriteLine(strRow);
                aRow = myCurs.NextRow();
            }

            theOutput.Close();
            theOutput.Dispose();
            aRow = null;
            pTable = null;
            Marshal.ReleaseComObject(myCurs);
            myCurs = null;
            GC.Collect();
            return intLineCount;
        }

        public bool WriteEmptyCSV(string OutTable, string theHeader, string aLogFile = "", bool Messages = false)
        {
            // Open output file.
            try
            {
                StreamWriter theOutput = new StreamWriter(OutTable, false);
                theOutput.WriteLine(theHeader);
                theOutput.Close();
                theOutput.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show("Can not open " + OutTable + ". Please ensure this is not open in another window. System error: " + ex.Message);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function WriteEmptyCSV returned the following error: Can not open " + OutTable + ". Please ensure this is not open in another window. System error: " + ex.Message);
                return false;
            }

        }

        public void ShowTable(string aTableName, string aLogFile = "", bool Messages = false)
        {
            if (aTableName == null)
            {
                if (Messages) MessageBox.Show("Please pass a table name", "Show Table");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ShowTable returned the following error: Please pass a table name");
                return;
            }

            ITable myTable = GetTable(aTableName, aLogFile, Messages);
            if (myTable == null)
            {
                if (Messages)
                {
                    MessageBox.Show("Table " + aTableName + " not found in map");
                }
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ShowTable returned the following error: Table " + aTableName + " not found in map");
                return;
            }

            ITableWindow myWin = new TableWindow();
            myWin.Table = myTable;
            myWin.Application = thisApplication;
            myWin.Show(true);
        }

        public bool SpatialJoin(string anInputLayer, string aJoinLayer, string anOutputName, string aJoinOperation="JOIN_ONE_TO_MANY", string aJoinType="KEEP_ALL", string aMatchMethod="CLOSEST", string aLogFile = "", bool Overwrite = true, bool Messages = false)
        {
            // Joins aJoinLayer onto anInputLayer and writes the output to anOutputLayer.
            // Note this does not implement some of the more advanced features of Spatial Join.
            // Firstly check if the output feature exists.
            if (FeatureclassExists(anOutputName))
            {
                if (!Overwrite)
                {
                    if (Messages)
                        MessageBox.Show("The feature class " + anOutputName + " already exists. Cannot overwrite");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "The BufferFeature function returned: The feature class " + anOutputName + " already exists. Cannot overwrite");
                    return false;
                }
            }
            if (!LayerExists(anInputLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + anInputLayer + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The SpatialJoin function returned the following error: The layer " + anInputLayer + " does not exist in the map");
                return false;
            }
            if (!LayerExists(aJoinLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aJoinLayer + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The SpatialJoin function returned the following error: The layer " + aJoinLayer + " does not exist in the map");
                return false;
            }
            
            ILayer pLayer = GetLayer(anInputLayer);
            try
            {
                IFeatureLayer pTest = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer " + anInputLayer + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The SpatialJoin function returned the following error: The layer " + anInputLayer + " is not a feature layer");
                return false;
            }

            pLayer = GetLayer(aJoinLayer);
            try
            {
                IFeatureLayer pTest = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer " + aJoinLayer + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The SpatialJoin function returned the following error: The layer " + aJoinLayer + " is not a feature layer");
                return false;
            }

            // Set up the geoprocessor.
            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = Overwrite;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(anInputLayer);
            parameters.Add(aJoinLayer);
            parameters.Add(anOutputName);
            parameters.Add(aJoinOperation);
            parameters.Add(aJoinType);
            parameters.Add("");
            parameters.Add(aMatchMethod);
            // No field mapping, match option, search radius or distance field name. May be added!

            // Do the join operation.
            bool blResult = false;
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("SpatialJoin_analysis", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                blResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "While buffering the BufferFeature function returned the following error: " + ex.Message);
            }
            // Tidy up
            gp = null;
            parameters = null;
            return blResult;
        }

        public bool BufferFeatures(string aLayer, string anOutputName, string aBufferDistance, string AggregateFields="", string DissolveOption="NONE", string aLogFile = "", bool Overwrite = true, bool Messages = false)
        {
            // Firstly check if the output feature exists.
            if (FeatureclassExists(anOutputName))
            {
                if (!Overwrite)
                {
                    if (Messages)
                        MessageBox.Show("The feature class " + anOutputName + " already exists. Cannot overwrite");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "The BufferFeature function returned: The feature class " + anOutputName + " already exists. Cannot overwrite");
                    return false;
                }
            }
            if (!LayerExists(aLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The BufferFeature function returned the following error: The layer " + aLayer + " does not exist in the map");
                return false;
            }

            if (GroupLayerExists(aLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " is a group layer and cannot be buffered.");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The BufferFeature function returned the following error: The layer " + aLayer + " is a group layer and cannot be buffered");
                return false;
            }
            ILayer pLayer = GetLayer(aLayer);
            try
            {
                IFeatureLayer pTest = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The BufferFeature function returned the following error: The layer " + aLayer + " is not a feature layer");
                return false;
            }

            // Check if all fields in the aggregate fields exist. If not, ignore.
            List<string> strAggColumns = AggregateFields.Split(';').ToList();
            AggregateFields = "";
            foreach (string strField in strAggColumns)
            {
                if (FieldExists(pLayer, strField, aLogFile))
                {
                    AggregateFields = AggregateFields + strField + ";";
                }
            }

            // a different approach using the geoprocessor object.
            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = Overwrite;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aLayer);
            parameters.Add(anOutputName);
            parameters.Add(aBufferDistance);
            parameters.Add("FULL");
            parameters.Add("ROUND");
            parameters.Add(DissolveOption);
            if (AggregateFields != "")
                parameters.Add(AggregateFields);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Buffer_analysis", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "While buffering the BufferFeature function returned the following error: " + ex.Message);
                gp = null;
                return false;
            }

        }

        public bool DissolveFeatures(string aLayer, string anOutputName, string aDissolveFieldList, string aStatisticsList="", string aDissolveType = "SINGLE_PART", string aLogFile = "", bool Overwrite = true, bool Messages = false)
        {

            // Firstly check if the output feature exists.
            if (FeatureclassExists(anOutputName))
            {
                if (!Overwrite)
                {
                    if (Messages)
                        MessageBox.Show("The feature class " + anOutputName + " already exists. Cannot overwrite");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "The DissolveFeature function returned: The feature class " + anOutputName + " already exists. Cannot overwrite");
                    return false;
                }
            }
            if (!LayerExists(aLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The DissolveFeature function returned the following error: The layer " + aLayer + " does not exist in the map");
                return false;
            }

            if (GroupLayerExists(aLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " is a group layer and cannot be buffered.");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The DissolveFeature function returned the following error: The layer " + aLayer + " is a group layer and cannot be buffered");
                return false;
            }
            ILayer pLayer = GetLayer(aLayer);
            try
            {
                IFeatureLayer pTest = (IFeatureLayer)pLayer;
                pTest = null; // either it worked or it's already fallen over.
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The DissolveFeature function returned the following error: The layer " + aLayer + " is not a feature layer");
                pLayer = null;
                return false;
            }
            pLayer = null;

            // Set up the geoprocessor.
            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = Overwrite;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aLayer);
            parameters.Add(anOutputName);
            parameters.Add(aDissolveFieldList);
            parameters.Add(aStatisticsList);
            parameters.Add(aDissolveType);


            // Do the dissolve operation.
            bool blResult = false;
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Dissolve_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                blResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "While dissolving the Dissolve function returned the following error: " + ex.Message);
            }
            // Tidy up
            gp = null;
            parameters = null;
            return blResult;
        }

        public int SetValueFromUnderlyingLayer(string anInputLayer, string aUniqueField, List<string> inputValueFields, List<string> criteriaFields, string aSourceLayer, List<string> SourceValueFields, string aLogFile = "", bool Messages = false)
        {
            // This function addresses the very strange bug in ArcGIS where it a spatial join fails on one or two points for no obvious reasons.
            int intResult = -999;
            bool blTest = false;

            // Select all the rows where the first of inputValueFields == null;
            string strQuery = inputValueFields[0] + " is null";
            blTest = SelectLayerByAttributes(anInputLayer, strQuery, aLogFile: aLogFile, Messages: Messages);
            if (!blTest)
            {
                if (Messages) MessageBox.Show("Could not select by attributes", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetValueFromUnderlyingLayer returned the following error: Could not select by attributes from layer " + anInputLayer);
                return intResult;
            }

            if (CountSelectedLayerFeatures(anInputLayer) == 0)
            {
                // Nothing to fix; return true.
                if (aLogFile != "")
                {
                    // myFileFuncs.WriteLine(aLogFile, "No missing values in layer " + anInputLayer);
                    return 0;
                }
            }

            // Get list of unique IDs (numeric)
            List<int> UniqueList = new List<int>(); // assumes unique IDs are numerical
            List<string> CriteriaList = new List<string>(); // assumes criteria are strings

            ILayer pLayer = GetLayer(anInputLayer);
            IFeatureLayer pFLayer = (IFeatureLayer)pLayer;
            IFeatureSelection aFS = (IFeatureSelection)pFLayer;
            ICursor pCurs = null;
            aFS.SelectionSet.Search(null, false, out pCurs);
            IFeatureClass pFC = pFLayer.FeatureClass;
            int intIndex = pFC.FindField(aUniqueField);

            if (intIndex < 0)
            {
                if (Messages) MessageBox.Show("The field " + aUniqueField + " doesn't exist in layer " + anInputLayer, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetValueFromUnderlyingLayer returned the following error: The field " + aUniqueField + " doesn't exist in layer " + anInputLayer);
                return intResult;
            }
            
            IRow pRow = pCurs.NextRow();
            while (pRow != null)
            {
                UniqueList.Add((int) pRow.get_Value(intIndex));
                foreach (string aCritField in criteriaFields)
                {
                    int intInd = pFC.FindField(aCritField);
                    CriteriaList.Add(pRow.get_Value(intInd).ToString());
                }

                pRow = pCurs.NextRow();
            }

            // Release this pile of objects.
            pLayer = null;
            pFLayer = null;
            aFS = null;
            pCurs = null;
            pRow = null;

            // For each unique found
            foreach (int unique in UniqueList)
            {
                // Select input layer by unique ID
                strQuery = aUniqueField + " = " + unique.ToString();
                SelectLayerByAttributes(anInputLayer, strQuery, aLogFile: aLogFile, Messages: Messages);

                if (CountSelectedLayerFeatures(anInputLayer) == 0)
                {
                    myFileFuncs.WriteLine(aLogFile, "Could not select feature with unique ID " + unique.ToString());
                    return intResult;
                }
                else if (CountSelectedLayerFeatures(anInputLayer) > 1)
                {
                    myFileFuncs.WriteLine(aLogFile, "Too many features with unique ID " + unique.ToString());
                    return intResult;
                }

                // Select by location: new selection, CONTAINS on Input layer
                SelectLayerByLocation(aSourceLayer, anInputLayer, "CONTAINS", "0.1 Meters", aLogFile: aLogFile, Messages: Messages);
                if (CountSelectedLayerFeatures(aSourceLayer) == 0)
                {
                    myFileFuncs.WriteLine(aLogFile, "The feature with unique ID " + unique.ToString() + " did not have a match in the source layer");
                    return intResult;
                }

                // If more than 1 is selected
                if (CountSelectedLayerFeatures(aSourceLayer) > 1) // We've found more than one and must do an additional attribute query.
                {
                    // Apply criteria using selectbyattributes
                    int i = 0;
                    strQuery = "";
                    foreach (string aField in criteriaFields)
                    {
                        
                        strQuery = strQuery + aField + " = " + CriteriaList[i] + " AND ";
                        i++;
                    }
                    strQuery = strQuery.Substring(0, strQuery.Length - 5);
                    SelectLayerByAttributes(aSourceLayer, strQuery, "SUBSET_SELECTION", aLogFile, Messages);
                    if (CountSelectedLayerFeatures(aSourceLayer) == 0)
                    {
                        if (Messages) MessageBox.Show("No selection on layer " + aSourceLayer, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        if (aLogFile != "")
                            myFileFuncs.WriteLine(aLogFile, "The function SetValueFromUnderlyingLayer returned the following error: No selection on layer " + aSourceLayer + " for ID " + unique.ToString());
                        return intResult;
                    }
                    else if (CountAllLayerFeatures(aSourceLayer) > 1)
                    {
                        if (Messages) MessageBox.Show("Too many features selected on layer " + aSourceLayer, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        if (aLogFile != "")
                            myFileFuncs.WriteLine(aLogFile, "The function SetValueFromUnderlyingLayer returned the following error: Too many features selected on layer " + aSourceLayer + " for ID " + unique.ToString());
                        return intResult;
                    }
                }
                // Find the value of the sourceValueFields
                pLayer = GetLayer(aSourceLayer);
                pFLayer = (IFeatureLayer)pLayer;
                aFS = (IFeatureSelection)pFLayer;
                aFS.SelectionSet.Search(null, false, out pCurs);
                pFC = pFLayer.FeatureClass;
                pRow = pCurs.NextRow(); // There is only one row selected; this should always get the correct row.

                // Calculate into inputValueFields USE CURSOR WOULD BE BETTER
                int p = 0;
                foreach (string anInputField in inputValueFields)
                {
                    string strSourceField = SourceValueFields[p];
                    int theIndex = pFC.FindField(strSourceField);
                    if (FieldIsNumeric(pFC, strSourceField, aLogFile, Messages))
                    {
                        var theValue = pRow.get_Value(theIndex);
                        blTest = CalculateField(anInputLayer, anInputField, theValue.ToString()); // Arc will round if it was an integer.
                    }
                    else if (FieldIsString(pFC, strSourceField, aLogFile, Messages))
                    {
                        string theValue = "\"" + pRow.get_Value(theIndex).ToString() + "\"";
                        blTest = CalculateField(anInputLayer, anInputField, theValue);
                    }
                    else
                    {
                        if (Messages) MessageBox.Show("Field " + strSourceField + " has a type that is not supported.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        if (aLogFile != "")
                            myFileFuncs.WriteLine(aLogFile, "Function SetValueFromUnderlyingLayer returned the following error: Field " + strSourceField + " has a type that is not supported.");
                        return intResult;
                    }
                    if (!blTest)
                        {
                            if (Messages) MessageBox.Show("Could not calculate field " + anInputField + " in " + anInputLayer, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            if (aLogFile != "")
                                myFileFuncs.WriteLine(aLogFile, "Function SetValueFromUnderlyingLayer returned the following error: Could not calculate field " + anInputField + " in " + anInputLayer);
                            return intResult;
                        }
                    p++;
                }
                
            }

            // Clear selected features on input layer.
            ClearSelectedMapFeatures(anInputLayer);
            myFileFuncs.WriteLine(aLogFile, UniqueList.Count.ToString() + " feature(s) had no cluster ID assigned in layer " + anInputLayer + ". Fixed.");

            pLayer = null;
            pFLayer = null;
            aFS = null;
            pCurs = null;
            pRow = null;
            // We have successfully navigated the whole thing.
            intResult = UniqueList.Count;
            return intResult;

        }

        public bool CreateFeatureClassNew(string aWorkspaceName, string aFeatureClassName, string aGeometryType, string aTemplateLayer = "", string aSpatialReference = null, string aLogFile = "", bool Overwrite = true, bool Messages = false)
        {
            // create a new FC based on a template layer (if given) using the spatial reference of aSpatialReference, which can also be a template layer.
            bool blResult = false;
            string aFullName = aWorkspaceName + @"\" + aFeatureClassName;
            if (FeatureclassExists(aFullName) && !Overwrite)
            {

                if (Messages) MessageBox.Show("Feature class " + aFullName + " already exists. Can't overwrite", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "") myFileFuncs.WriteLine(aLogFile, "Function CreateFeatureClass returned the following: Feature class " + aFullName + " already exists. Can't overwrite.");
                return blResult; // couldn't delete it.
                
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aWorkspaceName);
            parameters.Add(aFeatureClassName);
            parameters.Add(aGeometryType);
            parameters.Add(aTemplateLayer);
            if (aSpatialReference != "")
            {
                parameters.Add(""); // has_m
                parameters.Add(""); // has_z
                parameters.Add(aSpatialReference);
            }

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("CreateFeatureclass_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CreateFeatureclass returned the following error: " + ex.Message);
            }
            gp = null;
            parameters = null;
            myresult = null;
            return blResult;
        }

        public bool DeleteFeatureclass(string aFeatureclassName, string aLogFile = "", bool Messages = false)
        {
            if (!FeatureclassExists(aFeatureclassName) || !TableExists(aFeatureclassName))
            {
                if (Messages) MessageBox.Show("Feature class " + aFeatureclassName + " doesn't exist.", "Delete Feature Class");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteFeatureclass returned the following error: Feature class " + aFeatureclassName + " doesn't exist.");
                return false;
            }

            // a different approach using the geoprocessor object.
            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aFeatureclassName);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Delete_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteFeatureclass returned the following error: " + ex.Message);
                gp = null;
                return false;
            }

        }

        public bool DeleteWorkspace(string aWorkspaceName, string aLogFile = "", bool Messages = false)
        {
            if (!myFileFuncs.DirExists(aWorkspaceName))
            {
                if (Messages) MessageBox.Show("Workspace " + aWorkspaceName + " doesn't exist.", "Delete Workspace");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteWorkspace returned the following error: Workspace " + aWorkspaceName + " doesn't exist.");
                return false;
            }

            // a different approach using the geoprocessor object.
            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aWorkspaceName);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Delete_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteWorkspace returned the following error: " + ex.Message);
                gp = null;
                return false;
            }
        }

        public IFeature GetFeatureFromLayer(string aFeatureLayer, string aQuery, string aLogFile = "", bool Messages = false)
        {
            // This function returns a feature from the FeatureLayer. If there is more than one feature, it returns the LAST one.
            // Check if the layer exists.
            if (!LayerExists(aFeatureLayer))
            {
                if (Messages)
                    MessageBox.Show("Cannot find feature layer " + aFeatureLayer);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureFromLayer returned the following error: Cannot find feature layer " + aFeatureLayer);
                return null;
            }

            ILayer pLayer = GetLayer(aFeatureLayer, aLogFile, Messages);
            IFeatureLayer pFL;
            try
            {
                pFL = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("Layer " + aFeatureLayer + " is not a feature layer.");
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureFromLayer returned the following error: Layer " + aFeatureLayer + " is not a feature layer.");
                }
                return null;
            }

            IFeatureClass pFC = pFL.FeatureClass;
            IQueryFilter pQueryFilter = new QueryFilterClass();
            pQueryFilter.WhereClause = aQuery;
            IFeatureCursor pCurs = pFC.Search(pQueryFilter, false);

            int aCount = 0;
            IFeature feature = null;
            IFeature pResult = null;
            int nameFieldIndex = pFC.FindField("Shape");
            try
            {
                while ((feature = pCurs.NextFeature()) != null)
                {
                    aCount = aCount + 1;
                    pResult = feature;
                }
                
            }
            catch (COMException comExc)
            {
                // Handle any errors that might occur on NextFeature().
                if (Messages)
                    MessageBox.Show("Error: " + comExc.Message);
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureFromLayer returned the following error: " + comExc.Message);
                }
                Marshal.ReleaseComObject(pCurs);
                return null;
            }

            // Release the cursor.
            Marshal.ReleaseComObject(pCurs);

            if (aCount == 0)
            {
                if (Messages)
                    MessageBox.Show("There was no feature found matching those criteria");
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function GetFeatureFromLayer returned the following message: There was no feature found matching those criteria");
                }
                return null;
            }
            
            // Allow multiple features to be used
            if (aCount > 1)
            {
                // Ask the user if they want to continue
                DialogResult dlResult = MessageBox.Show("There were " + aCount.ToString() + " features found matching those criteria. Do you wish to continue?", "Data Buffer", MessageBoxButtons.YesNo);
                if (dlResult == System.Windows.Forms.DialogResult.Yes)
                {
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "There were " + aCount.ToString() + " features found matching those criteria");
                    }
                }
                else
                {
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "Function GetFeatureFromLayer returned the following message: There were " + aCount.ToString() + " features found matching those criteria");
                    }
                    return null;
                }
            }

            return pResult;

        }

        public ISpatialReference GetSpatialReference(string aFeatureLayer, string aLogFile = "", bool Messages = false)
        {
            // This falls over for reasons unknown.

            // Check if the layer exists.
            if (!LayerExists(aFeatureLayer))
            {
                if (Messages)
                    MessageBox.Show("Cannot find feature layer " + aFeatureLayer);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetSpatialReference returned the following error: Cannot find feature layer " + aFeatureLayer);
                return null;
            }

            ILayer pLayer = GetLayer(aFeatureLayer);
            IFeatureLayer pFL;
            try
            {
                pFL = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("Layer " + aFeatureLayer + " is not a feature layer.");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function GetSpatialReference returned the following error: Layer " + aFeatureLayer + " is not a feature layer");
                return null;
            }

            IFeatureClass pFC = pFL.FeatureClass;
            IDataset pDataSet = pFC.FeatureDataset;
            IGeoDataset pDS = (IGeoDataset)pDataSet;
            MessageBox.Show(pDS.SpatialReference.ToString());
            ISpatialReference pRef = pDS.SpatialReference;
            return pRef;
        }

        public bool SelectLayerByAttributes(string aFeatureLayerName, string aWhereClause, string aSelectionType = "NEW_SELECTION", string aLogFile = "", bool Messages = false)
        {
            ///<summary>Select features in the IActiveView by an attribute query using a SQL syntax in a where clause.</summary>
            /// 
            ///<param name="featureLayer">An IFeatureLayer interface to select upon</param>
            ///<param name="whereClause">A System.String that is the SQL where clause syntax to select features. Example: "CityName = 'Redlands'"</param>
            ///  
            ///<remarks>Providing and empty string "" will return all records.</remarks>
            if (!LayerExists(aFeatureLayerName))
                return false;

            IActiveView activeView = GetActiveView();
            IFeatureLayer featureLayer = null;
            try
            {
                featureLayer = (IFeatureLayer)GetLayer(aFeatureLayerName);
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer " + aFeatureLayerName + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "The layer " + aFeatureLayerName + " is not a feature layer");
                return false;
            }

            if (activeView == null || featureLayer == null || aWhereClause == null)
            {
                if (Messages)
                    MessageBox.Show("Please check input for this tool");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Please check input for the SelectLayerByAttributes function");
                return false;
            }


            // do this with Geoprocessor.

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aFeatureLayerName);
            parameters.Add(aSelectionType);
            parameters.Add(aWhereClause);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("SelectLayerByAttribute_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "SelectLayerByAttributes returned the following error: " + ex.Message);
                gp = null;
                return false;
            }
           
        }

        public bool SelectLayerByLocation(string aTargetLayer, string aSearchLayer, string anOverlapType = "INTERSECT", string aSearchDistance = "", string aSelectionType = "NEW_SELECTION", string aLogFile = "", bool Messages = false)
        {
            // Implementation of python SelectLayerByLocation_management.

            if (!LayerExists(aTargetLayer, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The target layer " + aTargetLayer + " doesn't exist", "Select Layer By Location");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SelectLayerByLocation returned the following error: Cannot find target layer " + aTargetLayer);
                return false;
            }

            if (!LayerExists(aSearchLayer, aLogFile, Messages))
            {
                if (Messages) MessageBox.Show("The search layer " + aSearchLayer + " doesn't exist", "Select Layer By Location");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SelectLayerByLocation returned the following error: Cannot find search layer " + aSearchLayer);
                return false;
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aTargetLayer);
            parameters.Add(anOverlapType);
            parameters.Add(aSearchLayer);
            parameters.Add(aSearchDistance);
            parameters.Add(aSelectionType);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("SelectLayerByLocation_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SelectLayerByLocation returned the following error: " + ex.Message);
                gp = null;
                return false;
            }
        }

        public long CountAllLayerFeatures(string aFeatureLayerName, string aLogFile = "", bool Messages = false)
        {
            // simply does a feature count.
            IFeatureClass pFC = GetFeatureClassFromLayerName(aFeatureLayerName);
            long lngCount = pFC.FeatureCount(null);
            pFC = null;
            return lngCount;
        }

        public int CountSelectedLayerFeatures(string aFeatureLayerName, string aLogFile = "", bool Messages = false)
        {
            // Check input.
            if (aFeatureLayerName == null)
            {
                if (Messages) MessageBox.Show("Please pass valid input string", "Count Selected Features");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CountSelectedLayerFeatures returned the following error: Please pass valid input string");
                return -1;
            }

            if (!LayerExists(aFeatureLayerName))
            {
                if (Messages) MessageBox.Show("Feature layer " + aFeatureLayerName + " does not exist in this map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CountSelectedLayerFeatures returned the following error: Feature layer " + aFeatureLayerName + " does not exist in this map");
                return -1;
            }

            IFeatureLayer pFL = null;
            try
            {
                pFL = (IFeatureLayer)GetLayer(aFeatureLayerName);
            }
            catch
            {
                if (Messages)
                    MessageBox.Show(aFeatureLayerName + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CountSelectedLayerFeatures returned the following error: " + aFeatureLayerName + " is not a feature layer");
                return -1;
            }

            IFeatureSelection pFSel = (IFeatureSelection)pFL;
            if (pFSel.SelectionSet.Count > 0) return pFSel.SelectionSet.Count;
            return 0;
        }

        public void ClearSelectedMapFeatures(string aFeatureLayerName, string aLogFile = "", bool Messages = false)
        {
            ///<summary>Clear the selected features in the IActiveView for a specified IFeatureLayer.</summary>
            /// 
            ///<param name="activeView">An IActiveView interface</param>
            ///<param name="featureLayer">An IFeatureLayer</param>
            /// 
            ///<remarks></remarks>
            if (!LayerExists(aFeatureLayerName))
                return;

            IActiveView activeView = GetActiveView();
            IFeatureLayer featureLayer = null;
            try
            {
                featureLayer = (IFeatureLayer)GetLayer(aFeatureLayerName);
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The layer " + aFeatureLayerName + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ClearSelectedMapFeatures returned the following error: " + aFeatureLayerName + " is not a feature layer");
                return;
            }
            if (activeView == null || featureLayer == null)
            {
                return;
            }
            ESRI.ArcGIS.Carto.IFeatureSelection featureSelection = featureLayer as ESRI.ArcGIS.Carto.IFeatureSelection; // Dynamic Cast

            // Invalidate only the selection cache. Flag the original selection
            activeView.PartialRefresh(ESRI.ArcGIS.Carto.esriViewDrawPhase.esriViewGeoSelection, null, null);

            // Clear the selection
            featureSelection.Clear();

            // Flag the new selection
            activeView.PartialRefresh(ESRI.ArcGIS.Carto.esriViewDrawPhase.esriViewGeoSelection, null, null);
        }

        public void ZoomToLayer(string aLayerName, string aLogFile = "", bool Messages = false)
        {
            if (!LayerExists(aLayerName))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ZoomToLayer returned the following error: Layer " + aLayerName + " does not exist in the map");
                return;
            }
            IActiveView activeView = GetActiveView();
            ILayer pLayer = GetLayer(aLayerName);
            IEnvelope pEnv = pLayer.AreaOfInterest;
            pEnv.Expand(1.05, 1.05, true);
            activeView.Extent = pEnv;
            activeView.Refresh();
        }

        public void ChangeLegend(string aLayerName, string aLayerFile, bool DisplayLabels = false, string aLogFile = "", bool Messages = false)
        {
            if (!LayerExists(aLayerName))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ChangeLegend returned the following error: Layer " + aLayerName + " does not exist in the map");
                return;
            }
            if (!myFileFuncs.FileExists(aLayerFile))
            {
                if (Messages)
                    MessageBox.Show("The layer file " + aLayerFile + " does not exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ChangeLegend returned the following error: Layer file " + aLayerFile + " does not exist");
                return;
            }

            ILayer pLayer = GetLayer(aLayerName);
            IGeoFeatureLayer pTargetLayer = null;
            try
            {
                pTargetLayer = (IGeoFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The input layer " + aLayerName + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ChangeLegend returned the following error: Layer " + aLayerName + " is not a feature layer");
                return;
            }
            ILayerFile pLayerFile = new LayerFileClass();
            pLayerFile.Open(aLayerFile);

            IGeoFeatureLayer pTemplateLayer = (IGeoFeatureLayer)pLayerFile.Layer;
            IFeatureRenderer pTemplateSymbology = pTemplateLayer.Renderer;
            IAnnotateLayerPropertiesCollection pTemplateAnnotation = pTemplateLayer.AnnotationProperties;

            pLayerFile.Close();

            IObjectCopy pCopy = new ObjectCopyClass();
            pTargetLayer.Renderer = (IFeatureRenderer)pCopy.Copy(pTemplateSymbology);
            pTargetLayer.AnnotationProperties = pTemplateAnnotation;

            SwitchLabels(aLayerName, DisplayLabels, aLogFile, Messages);

        }

        public void SwitchLabels(string aLayerName, bool DisplayLabels = false, string aLogFile = "", bool Messages = false)
        {
            if (!LayerExists(aLayerName))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ChangeLegend returned the following error: Layer " + aLayerName + " does not exist in the map");
                return;
            }
            ILayer pLayer = GetLayer(aLayerName);
            IGeoFeatureLayer pTargetLayer = null;
            try
            {
                pTargetLayer = (IGeoFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("The input layer " + aLayerName + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ChangeLegend returned the following error: Layer " + aLayerName + " is not a feature layer");
                return;
            }

            if (DisplayLabels)
            {
                pTargetLayer.DisplayAnnotation = true;
            }
            else
            {
                pTargetLayer.DisplayAnnotation = false;
            }
        }

        public bool CalculateField(string aLayerName, string aFieldName, string aCalculate, string aCodeBlock = "", string aLogFile = "", bool Messages = false)
        {
            // This takes both layers and FCs which is why I've skipped the error checking
            // tut, tut.

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();


            // Populate the variant array with parameter values.
            parameters.Add(aLayerName);
            parameters.Add(aFieldName);
            parameters.Add(aCalculate);
            parameters.Add("VB");
            parameters.Add(aCodeBlock);

            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("CalculateField_management", parameters, null);

                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                gp = null;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function CalculateField returned the following error: " + ex.Message);
                gp = null;
                return false;
            }
        }

        public bool AddJoin(string aTargetLayer, string aTargetField, string aJoinTable, string aJoinField, string aLogFile = "", bool Messages = false)
        {
            // Takes both FC and Layer which is why we're limited in our error checking.
            if (!LayerExists(aTargetLayer) && !FeatureclassExists(aTargetLayer) && !TableExists(aTargetLayer))
            {
                if (Messages)
                {
                    MessageBox.Show("The layer or feature class " + aTargetLayer + " does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function AddJoin returned the following error: The layer or feature class " + aTargetLayer + " does not exist.");
                }
                return false;
            }

            if (!LayerExists(aJoinTable) && !FeatureclassExists(aJoinTable) && !TableExists(aJoinTable))
            {
                if (Messages)
                {
                    MessageBox.Show("The layer or feature class " + aJoinTable + " does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function AddJoin returned the following error: The layer or feature class " + aJoinTable + " does not exist.");
                }
                return false;
            }

            // We have to assume that the field exists.

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aTargetLayer);
            parameters.Add(aTargetField);
            parameters.Add(aJoinTable);
            parameters.Add(aJoinField);

            // Execute the tool.
            bool blResult = false;
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("AddJoin_management", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "Function AddJoin returned the following errors: " + ex.Message);
                        myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                    }

                }
            }
            finally
            {
                gp = null;
                myresult = null;
                sev = null;
                parameters = null;
            }
            return blResult;
        }

        public bool RemoveJoin(string aLayer, string aLogFile = "", bool Messages = false)
        {
            // Takes both FC and Layer which is why we're limited in our error checking.
            if (!LayerExists(aLayer) && !FeatureclassExists(aLayer) && !TableExists(aLayer))
            {
                if (Messages)
                {
                    MessageBox.Show("The layer or feature class " + aLayer + " does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveJoin returned the following error: The layer or feature class " + aLayer + " does not exist.");
                }
                return false;
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aLayer);

            // Execute the tool.
            bool blResult = false;
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("RemoveJoin_management", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "Function RemoveJoin returned the following errors: " + ex.Message);
                        myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                    }

                }
            }
            finally
            {
                gp = null;
                myresult = null;
                sev = null;
                parameters = null;
            }
            return blResult;
        }

        public bool ExportSelectionToShapefile(string aLayerName, string anOutShapefile, string OutputColumns, string TempShapeFile, string GroupColumns = "",
            string StatisticsColumns = "", bool IncludeArea = false, string AreaMeasurementUnit = "ha", bool IncludeDistance = false, string aRadius = "None", string aTargetLayer = null, string aLogFile = "", bool Overwrite = true, bool CheckForSelection = false, bool RenameColumns = false, bool Messages = false)
        {
            // Some sanity tests.
            if (!LayerExists(aLayerName, aLogFile, Messages))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: The layer " + aLayerName + " does not exist in the map");
                return false;
            }
            if (CountSelectedLayerFeatures(aLayerName, aLogFile, Messages) <= 0 && CheckForSelection)
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not have a selection");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: The layer " + aLayerName + " does not have a selection");
                return false;
            }

            // Does the output file exist?
            if (FeatureclassExists(anOutShapefile))
            {
                if (!Overwrite)
                {
                    if (Messages)
                        MessageBox.Show("The output feature class " + anOutShapefile + " already exists. Cannot overwrite");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: The output feature class " + anOutShapefile + " already exists. Cannot overwrite");
                    return false;
                }
            }

            IFeatureClass pFC = GetFeatureClassFromLayerName(aLayerName, aLogFile, Messages);

            // Add the area field if required.
            string strTempLayer = myFileFuncs.ReturnWithoutExtension(myFileFuncs.GetFileName(TempShapeFile)); // Temporary layer.

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = Overwrite;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Check if the FC is a point FC.
            string strFCType = GetFeatureClassType(pFC);
            // Calculate the area field if required.
            bool blAreaAdded = false;
            if (IncludeArea && strFCType == "polygon")
            {
                string strCalc = "";
                if (AreaMeasurementUnit.ToLower() == "ha")
                    strCalc = "!SHAPE.AREA@HECTARES!";
                else if (AreaMeasurementUnit.ToLower() == "m2")
                    strCalc = "!SHAPE.AREA@SQUAREMETERS!";
                else if (AreaMeasurementUnit.ToLower() == "km2")
                    strCalc = "!SHAPE.AREA@SQUAREKILOMETERS!";

                // Does the area field already exist? If not, add it.
                if (!FieldExists(pFC, "Area", aLogFile, Messages))
                {
                    AddField(ref pFC, "Area", esriFieldType.esriFieldTypeDouble, 20, aLogFile, Messages);
                    blAreaAdded = true;
                }
                // Calculate the field.
                IVariantArray AreaCalcParams = new VarArrayClass();
                AreaCalcParams.Add(aLayerName);
                AreaCalcParams.Add("AREA");
                AreaCalcParams.Add(strCalc);
                AreaCalcParams.Add("PYTHON_9.3");

                try
                {
                    myresult = (IGeoProcessorResult)gp.Execute("CalculateField_management", AreaCalcParams, null);
                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: " + ex.Message);
                    gp = null;
                    return false;
                }
            }

            // Check all the requested group by and statistics fields exist.
            // Only pass those that do.
            if (GroupColumns != "")
            {
                List<string> strColumns = GroupColumns.Split(';').ToList();
                GroupColumns = "";
                foreach (string strCol in strColumns)
                {
                    if (FieldExists(pFC, strCol.Trim()))
                        GroupColumns = GroupColumns + strCol.Trim() + ";";
                }
                if (GroupColumns != "")
                    GroupColumns = GroupColumns.Substring(0, GroupColumns.Length - 1);

            }

            if (StatisticsColumns != "")
            {
                List<string> strStatsColumns = StatisticsColumns.Split(';').ToList();
                StatisticsColumns = "";
                foreach (string strColDef in strStatsColumns)
                {
                    List<string> strComponents = strColDef.Split(' ').ToList();
                    string strField = strComponents[0]; // The field name.
                    if (FieldExists(pFC, strField.Trim()))
                        StatisticsColumns = StatisticsColumns + strColDef + ";";
                }
                if (StatisticsColumns != "")
                    StatisticsColumns = StatisticsColumns.Substring(0, StatisticsColumns.Length - 1);
            }

            // New process: 1. calculate distance, 2. summary statistics to dbf or csv. use min_radius and sum_area.


            // If we are including distance, the process is slighly different.
            if ((GroupColumns != null && GroupColumns != "") || StatisticsColumns != "") // include group columns OR statistics columns.
            {
                string strOutFile = TempShapeFile;
                if (!IncludeDistance)
                    // We are ONLY performing a group by. Go straight to final shapefile.
                    strOutFile = anOutShapefile;
        

                // Do the dissolve as requested.
                IVariantArray DissolveParams = new VarArrayClass();
                DissolveParams.Add(aLayerName);
                DissolveParams.Add(strOutFile);
                DissolveParams.Add(GroupColumns);
                DissolveParams.Add(StatisticsColumns); // These should be set up to be as required beforehand.

                try
                {
                    //// Try using statistics instead of dissolve
                    //myresult = (IGeoProcessorResult)gp.Execute("Statistics_analysis", DissolveParams, null);
                    myresult = (IGeoProcessorResult)gp.Execute("Dissolve_management", DissolveParams, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.
                    string strNewLayer = myFileFuncs.ReturnWithoutExtension(myFileFuncs.GetFileName(strOutFile));

                    IFeatureClass pInFC = GetFeatureClassFromLayerName(aLayerName, aLogFile, Messages);
                    IFeatureClass pOutFC = GetFeatureClassFromLayerName(strNewLayer, aLogFile, Messages);

                    //ILayer pInLayer = GetLayer(aLayerName);
                    //IFeatureLayer pInFLayer = (IFeatureLayer)pInLayer;
                    //IFeatureClass pInFC = pInFLayer.FeatureClass;

                    //ILayer pOutLayer = GetLayer(strNewLayer);
                    //IFeatureLayer pOutFLayer = (IFeatureLayer)pOutLayer;
                    //IFeatureClass pOutFC = pOutFLayer.FeatureClass;

                    // Now rejig the statistics fields if required because they will look like FIRST_SAC which is no use.
                    if (StatisticsColumns != "" && RenameColumns)
                    {
                        List<string> strFieldNames = StatisticsColumns.Split(';').ToList();
                        int intIndexCount = 0;
                        foreach (string strField in strFieldNames)
                        {
                            List<string> strFieldComponents = strField.Split(' ').ToList();
                            // Let's find out what the new field is called - could be anything.
                            int intNewIndex = 2; // FID = 1; Shape = 2.
                            intNewIndex = intNewIndex + GroupColumns.Split(';').ToList().Count + intIndexCount; // Add the number of columns uses for grouping
                            IField pNewField = pOutFC.Fields.get_Field(intNewIndex);
                            string strInputField = pNewField.Name;
                            // Note index stays the same, since we're deleting the fields. 
                            
                            string strNewField = strFieldComponents[0]; // The original name of the field.
                            // Get the definition of the original field from the original feature class.
                            int intIndex = pInFC.Fields.FindField(strNewField);
                            IField pField = pInFC.Fields.get_Field(intIndex);

                            // Add the field to the new FC.
                            AddLayerField(strNewLayer, strNewField, pField.Type, pField.Length, aLogFile, Messages);
                            // Calculate the new field.
                            string strCalc = "[" + strInputField + "]";
                            CalculateField(strNewLayer, strNewField, strCalc, "", aLogFile, Messages);
                            DeleteLayerField(strNewLayer, strInputField, aLogFile, Messages);
                        }
                        
                    }

                    aLayerName = strNewLayer;
                    
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: " + ex.Message);
                    gp = null;
                    return false;
                }

            }
            if (IncludeDistance)
            {
                // Now add the distance field by joining if required. This will take all fields.

                IVariantArray params1 = new VarArrayClass();
                params1.Add(aLayerName);
                params1.Add(aTargetLayer);
                params1.Add(anOutShapefile);
                params1.Add("JOIN_ONE_TO_ONE");
                params1.Add("KEEP_ALL");
                params1.Add("");
                params1.Add("CLOSEST");
                params1.Add("0");
                params1.Add("Distance");

                try
                {
                    myresult = (IGeoProcessorResult)gp.Execute("SpatialJoin_analysis", params1, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.
                    
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: " + ex.Message);
                    gp = null;
                    return false;
                }
            }

            
            if (GroupColumns == "" && !IncludeDistance && StatisticsColumns == "") 
                // Only run a straight copy if neither a group/dissolve nor a distance has been requested
                // Because the data won't have been processed yet.
            {

                // Create a variant array to hold the parameter values.
                IVariantArray parameters = new VarArrayClass();

                // Populate the variant array with parameter values.
                parameters.Add(aLayerName);
                parameters.Add(anOutShapefile);

                try
                {
                    myresult = (IGeoProcessorResult)gp.Execute("CopyFeatures_management", parameters, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: " + ex.Message);
                    gp = null;
                    return false;
                }
            }

            // If the Area field was added, remove it again now from the original since we've saved our results.
            if (blAreaAdded)
            {
                DeleteField(pFC, "Area", aLogFile, Messages);
            }


            // Remove all temporary layers.
            bool blFinished = false;
            while (!blFinished)
            {
                if (LayerExists(strTempLayer, aLogFile, Messages))
                    RemoveLayer(strTempLayer, aLogFile, Messages);
                else
                    blFinished = true;
            }

            if (FeatureclassExists(TempShapeFile))
            {
                IVariantArray DelParams = new VarArrayClass();
                DelParams.Add(TempShapeFile);
                try
                {

                    myresult = (IGeoProcessorResult)gp.Execute("Delete_management", DelParams, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.
                }
                catch (Exception ex)
                {
                    if (Messages)
                        MessageBox.Show("Cannot delete temporary layer " + TempShapeFile + ". System error: " + ex.Message);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToShapefile returned the following error: " + ex.Message);
                }
            }

            // Get the output shapefile
            IFeatureClass pResultFC = GetFeatureClass(anOutShapefile, aLogFile, Messages);

            // Include radius if requested
            if (aRadius != "none")
            {
                AddField(ref pResultFC, "Radius", esriFieldType.esriFieldTypeString, 25, aLogFile, Messages);
                CalculateField(anOutShapefile, "Radius", '"' + aRadius + '"', "", aLogFile, Messages);
            }

            // Now drop any fields from the output that we don't want.
            IFields pFields = pResultFC.Fields;
            List<string> strDeleteFields = new List<string>();

            // Make a list of fields to delete.
            for (int i = 0; i < pFields.FieldCount; i++)
            {
                IField pField = pFields.get_Field(i);
                if (OutputColumns.IndexOf(pField.Name, StringComparison.CurrentCultureIgnoreCase) == -1 && !pField.Required) 
                    // Does it exist in the 'keep' list or is it required?
                {
                    // If not, add to te delete list.
                    strDeleteFields.Add(pField.Name);
                }
            }

            //Delete the listed fields.
            foreach (string strField in strDeleteFields)
            {
                DeleteField(pResultFC, strField, aLogFile, Messages);
            }
            
            pResultFC = null;
            pFC = null;
            pFields = null;
            //pFL = null;
            gp = null;

            UpdateTOC();
            GC.Collect(); // Just in case it's hanging onto anything.

            return true;
        }

        public int ExportSelectionToCSV(string aLayerName, string anOutTable, string OutputColumns, bool IncludeHeaders, string TempShapeFile, string TempDBF, string GroupColumns = "",
    string StatisticsColumns = "", string OrderColumns = "", bool IncludeArea = false, string AreaMeasurementUnit = "ha", bool IncludeDistance = false, string aRadius = "None", string aTargetLayer = null, string aLogFile = "", bool Overwrite = true, bool CheckForSelection = false, bool RenameColumns = false, bool Messages = false)
        {
            int intResult = -1;
            // Some sanity tests.
            if (!LayerExists(aLayerName, aLogFile, Messages))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not exist in the map");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: The layer " + aLayerName + " does not exist in the map");
                return -1;
            }
            if (CountSelectedLayerFeatures(aLayerName, aLogFile, Messages) <= 0 && CheckForSelection)
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayerName + " does not have a selection");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: The layer " + aLayerName + " does not have a selection");
                return -1;
            }

            // Does the output file exist?
            if (myFileFuncs.FileExists(anOutTable))
            {
                if (!Overwrite)
                {
                    if (Messages)
                        MessageBox.Show("The output table " + anOutTable + " already exists. Cannot overwrite");
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: The output table " + anOutTable + " already exists. Cannot overwrite");
                    return -1;
                }
            }

            IFeatureClass pFC = GetFeatureClassFromLayerName(aLayerName, aLogFile, Messages);

            // Add the area field if required.
            string strTempLayer = myFileFuncs.ReturnWithoutExtension(myFileFuncs.GetFileName(TempShapeFile)); // Temporary layer.

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = Overwrite;

            IGeoProcessorResult myresult = new GeoProcessorResultClass();

            // Check if the FC is a point FC.
            string strFCType = GetFeatureClassType(pFC);
            // Calculate the area field if required.
            bool blAreaAdded = false;
            if (IncludeArea && strFCType == "polygon")
            {
                string strCalc = "";
                if (AreaMeasurementUnit.ToLower() == "ha")
                    strCalc = "!SHAPE.AREA@HECTARES!";
                else if (AreaMeasurementUnit.ToLower() == "m2")
                    strCalc = "!SHAPE.AREA@SQUAREMETERS!";
                else if (AreaMeasurementUnit.ToLower() == "km2")
                    strCalc = "!SHAPE.AREA@SQUAREKILOMETERS!";

                // Does the area field already exist? If not, add it.
                if (!FieldExists(pFC, "Area", aLogFile, Messages))
                {
                    AddField(ref pFC, "Area", esriFieldType.esriFieldTypeDouble, 20, aLogFile, Messages);
                    blAreaAdded = true;
                }
                // Calculate the field.
                IVariantArray AreaCalcParams = new VarArrayClass();
                AreaCalcParams.Add(aLayerName);
                AreaCalcParams.Add("AREA");
                AreaCalcParams.Add(strCalc);
                AreaCalcParams.Add("PYTHON_9.3");

                try
                {
                    myresult = (IGeoProcessorResult)gp.Execute("CalculateField_management", AreaCalcParams, null);
                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: " + ex.Message);
                    gp = null;
                    return -1;
                }
            }

            // New process: 1. calculate distance, 2. summary statistics to dbf or csv. use min_radius and sum_area.

            // Calculate the radius as required.
            if (IncludeDistance)
            {
                // Now add the distance field by joining if required. This will take all fields.

                IVariantArray params1 = new VarArrayClass();
                params1.Add(aLayerName);
                params1.Add(aTargetLayer);
                params1.Add(TempShapeFile);
                params1.Add("JOIN_ONE_TO_ONE");
                params1.Add("KEEP_ALL");
                params1.Add("");
                params1.Add("CLOSEST");
                params1.Add("0");
                params1.Add("Distance");

                try
                {
                    myresult = (IGeoProcessorResult)gp.Execute("SpatialJoin_analysis", params1, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.

                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: " + ex.Message);
                    gp = null;
                    return -1;
                }

                // After this the input to the remainder of the function should be from TempShapefile.
                //string strNewLayer = strTempLayer;
                aLayerName = strTempLayer;
                pFC = GetFeatureClassFromLayerName(aLayerName);
            }

            // Check all the requested group by and statistics fields exist.
            // Only pass those that do.
            if (GroupColumns != "")
            {
                List<string> strColumns = GroupColumns.Split(';').ToList();
                GroupColumns = "";
                foreach (string strCol in strColumns)
                {
                    if (FieldExists(pFC, strCol.Trim()))
                        GroupColumns = GroupColumns + strCol.Trim() + ";";
                }
                if (GroupColumns != "")
                    GroupColumns = GroupColumns.Substring(0, GroupColumns.Length - 1);

            }

            if (StatisticsColumns != "")
            {
                List<string> strStatsColumns = StatisticsColumns.Split(';').ToList();
                StatisticsColumns = "";
                foreach (string strColDef in strStatsColumns)
                {
                    List<string> strComponents = strColDef.Split(' ').ToList();
                    string strField = strComponents[0]; // The field name.
                    if (FieldExists(pFC, strField.Trim()))
                        StatisticsColumns = StatisticsColumns + strColDef + ";";
                }
                if (StatisticsColumns != "")
                    StatisticsColumns = StatisticsColumns.Substring(0, StatisticsColumns.Length - 1);
            }

            // If we have group columns but no statistics columns, add a dummy column.
            if (StatisticsColumns == "" && GroupColumns != "")
            {
                string strDummyField = GroupColumns.Split(';').ToList()[0];
                StatisticsColumns = strDummyField + " FIRST";
            }

            ///// Now do the summary statistics as required, or export the layer to table if not.
            if ((GroupColumns != null && GroupColumns != "") || StatisticsColumns != "")
            {
                // Do summary statistics
                IVariantArray StatsParams = new VarArrayClass();
                StatsParams.Add(aLayerName);
                StatsParams.Add(TempDBF);

                if (StatisticsColumns != "") StatsParams.Add(StatisticsColumns);

                if (GroupColumns != "") StatsParams.Add(GroupColumns);

                try
                {
                    myresult = (IGeoProcessorResult)gp.Execute("Statistics_analysis", StatsParams, null);
                }
                catch (COMException ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: " + ex.Message);
                    gp = null;
                    return -1;
                }

                // Now export this output table to CSV and delete the temporary file.
                intResult = CopyToCSV(TempDBF, anOutTable, OutputColumns, OrderColumns, false, false, !IncludeHeaders, aLogFile);
            }
            else
            {
                // Do straight copy to dbf.
                intResult = CopyToCSV(TempShapeFile, anOutTable, OutputColumns, OrderColumns, true, false, !IncludeHeaders, aLogFile);
            }


            // If the Area field was added, remove it again now from the original since we've saved our results.
            if (blAreaAdded)
            {
                DeleteField(pFC, "Area", aLogFile, Messages);
            }


            // Remove all temporary layers.
            bool blFinished = false;
            while (!blFinished)
            {
                if (LayerExists(strTempLayer, aLogFile, Messages))
                    RemoveLayer(strTempLayer, aLogFile, Messages);
                else
                    blFinished = true;
            }

            if (FeatureclassExists(TempShapeFile))
            {
                IVariantArray DelParams = new VarArrayClass();
                DelParams.Add(TempShapeFile);
                try
                {

                    myresult = (IGeoProcessorResult)gp.Execute("Delete_management", DelParams, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.
                }
                catch (Exception ex)
                {
                    if (Messages)
                        MessageBox.Show("Cannot delete temporary layer " + TempShapeFile + ". System error: " + ex.Message);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: " + ex.Message);
                }
            }

            if (TableExists(TempDBF))
            {
                IVariantArray DelParams = new VarArrayClass();
                DelParams.Add(TempDBF);
                try
                {

                    myresult = (IGeoProcessorResult)gp.Execute("Delete_management", DelParams, null);

                    // Wait until the execution completes.
                    while (myresult.Status == esriJobStatus.esriJobExecuting)
                        Thread.Sleep(1000);
                    // Wait for 1 second.
                }
                catch (Exception ex)
                {
                    if (Messages)
                        MessageBox.Show("Cannot delete temporary DBF file " + TempDBF + ". System error: " + ex.Message);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "Function ExportSelectionToCSV returned the following error: " + ex.Message);
                }
            }

            //pResultFC = null;
            pFC = null;
            //pFields = null;
            //pFL = null;
            gp = null;

            UpdateTOC();
            GC.Collect(); // Just in case it's hanging onto anything.

            return intResult;
        }

        public bool SummaryStatistics(string aLayer, string anOutFile, string StatsFields, string GroupFields, string aLogFile = "", bool Overwrite = true, bool Messages = false)
        {
            // Simple summary statistics tool. 
            // Takes both FC and Layer which is why we're limited in our error checking.
            if (!LayerExists(aLayer) && !FeatureclassExists(aLayer) && !TableExists(aLayer))
            {
                if (Messages)
                {
                    MessageBox.Show("The layer or feature class " + aLayer + " does not exist.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveJoin returned the following error: The layer or feature class " + aLayer + " does not exist.");
                }
                return false;
            }

            if (TableExists(anOutFile) && !Overwrite)
            {
                if (Messages)
                {
                    MessageBox.Show("The output table " + anOutFile + " already exists. Can't overwrite", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                if (aLogFile != "")
                {
                    myFileFuncs.WriteLine(aLogFile, "Function RemoveJoin returned the following error: The output table " + anOutFile + " already exists. Can't overwrite");
                }
                return false;
            }

            ESRI.ArcGIS.Geoprocessor.Geoprocessor gp = new ESRI.ArcGIS.Geoprocessor.Geoprocessor();
            gp.OverwriteOutput = true;
            IGeoProcessorResult myresult = new GeoProcessorResultClass();
            object sev = null;

            // Create a variant array to hold the parameter values.
            IVariantArray parameters = new VarArrayClass();

            // Populate the variant array with parameter values.
            parameters.Add(aLayer);
            parameters.Add(anOutFile);
            parameters.Add(StatsFields);
            parameters.Add(GroupFields);

            // Execute the tool.
            bool blResult = false;
            try
            {
                myresult = (IGeoProcessorResult)gp.Execute("Statistics_analysis", parameters, null);
                // Wait until the execution completes.
                while (myresult.Status == esriJobStatus.esriJobExecuting)
                    Thread.Sleep(1000);
                // Wait for 1 second.
                if (Messages)
                {
                    MessageBox.Show("Process complete");
                }
                blResult = true;
            }
            catch (Exception ex)
            {
                if (Messages)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    MessageBox.Show(gp.GetMessages(ref sev));
                    if (aLogFile != "")
                    {
                        myFileFuncs.WriteLine(aLogFile, "Function SummaryStatistics returned the following errors: " + ex.Message);
                        myFileFuncs.WriteLine(aLogFile, "Geoprocessor error: " + gp.GetMessages(ref sev));
                    }

                }
            }
            finally
            {
                gp = null;
                myresult = null;
                sev = null;
                parameters = null;
            }
            return blResult;
        }

        public bool SetMostCommon(string aWriteLayer, string aUniqueField, List<string> aFieldList, string anInputLayer, List<string> aValueFieldList, string aLogFile = "", bool Messages = false)
        {
            // Goes through aWriteLayer's Uniques, finds all rows in anInputLayer and looks for the occurrences of aValueFieldList.
            // Then writes the results to aFieldList. Assumes aFieldList and aValueFieldList are in sync. Also makes the gross assumption
            // that UniqueField is an integer and the same in both layers.
            bool blResult = false;
            // do QA
            if (!LayerExists(aWriteLayer))
            {
                if (Messages) MessageBox.Show("The layer " + aWriteLayer + " doesn't exist in the view", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetMostCommon returned the following error: The layer " + aWriteLayer + " doesn't exist in the view");
                return blResult;
            }
            if (!LayerExists(anInputLayer))
            {
                if (Messages) MessageBox.Show("The layer " + anInputLayer + " doesn't exist in the view", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetMostCommon returned the following error: The layer " + anInputLayer + " doesn't exist in the view");
                return blResult;
            }

            IFeatureClass pFC = GetFeatureClassFromLayerName(aWriteLayer);
            if (pFC == null)
            {
                if (Messages) MessageBox.Show("The layer " + aWriteLayer + " is not a feature layer", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetMostCommon returned the following error: The layer " + aWriteLayer + " is not a feature layer");
                return blResult ;
            }
            IFeatureCursor pCurs = pFC.Update(null, false);

            IFeatureLayer pLayer = (IFeatureLayer) GetLayer(anInputLayer);
            if (pLayer == null)
            {
                if (Messages) MessageBox.Show("The layer " + anInputLayer + " is not a feature layer", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetMostCommon returned the following error: The layer " + anInputLayer + " is not a feature layer");
                return blResult;
            }
            IFeatureClass pClass = pLayer.FeatureClass; // should always work on feature layer.

            // the relevant field index.
            int inUniqueIndex = pFC.FindField(aUniqueField);
            if (inUniqueIndex < 0)
            {
                if (Messages) MessageBox.Show("The unique ID field does not exist", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function SetMostCommon returned the following error: The layer " + aWriteLayer + " has no field named " + aUniqueField);
                return blResult;
            }

            IFeature pRow = pCurs.NextFeature();
            while (pRow != null)
            {
                // get the unique
                var Unique = pRow.get_Value(inUniqueIndex);
                if (Unique == null)
                {
                    if (Messages) MessageBox.Show("Null value found for unique ID in layer " + aWriteLayer, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (aLogFile != "")
                        myFileFuncs.WriteLine(aLogFile, "The function SetMostCommon returned the following error: Null value found for unique ID in layer " + aWriteLayer);
                    pFC = null;
                    pCurs = null;
                    pLayer = null;
                    pClass = null;
                    return blResult;
                }

                int intUnique = (int)Unique; //pRow.get_Value(inUniqueIndex);

                // Select the input layer on this
                string strQuery = aUniqueField + " = " + intUnique.ToString();
                int aTracker = 0;
                foreach (string aFieldName in aValueFieldList)
                {
                    // Find the field in this layer.
                    int intFieldIndex = pLayer.FeatureClass.FindField(aFieldName);
                    // What is the corresponding output field name and index?
                    string anOutFieldname = aFieldList[aTracker];
                    int anOutFieldIndex = pFC.FindField(anOutFieldname);

                    IQueryFilter pFilt = new QueryFilterClass();
                    pFilt.WhereClause = strQuery;
                    pFilt.SubFields = aFieldName;
                    IFeatureCursor pResCurs =  pClass.Search(pFilt, false);

                    pFilt = null; // get rid.

                    IFeature aRow = pResCurs.NextFeature();
                    List<string> theValues = new List<string>();
                    List<int> theCounts = new List<int>();
                    while (aRow != null)
                    {
                        // cycle through the selection
                        string theValue = aRow.get_Value(intFieldIndex).ToString();
                        if (theValues.Contains(theValue))
                        {
                            int theLocation = 0;
                            foreach (string aThing in theValues)
                            {
                                if (aThing == theValue)
                                {
                                    theCounts[theLocation] = theCounts[theLocation] + 1;
                                    break;
                                }
                                else
                                    theLocation++; // add one.
                            }
                        }
                        else
                        {
                            theValues.Add(theValue);
                            theCounts.Add(1); // One occurrence of this object added.
                        }
                        aRow = pResCurs.NextFeature();
                    }
                    // Let's find the highest occurrence.
                    int i = 0;
                    int theMax = 0;
                    string theMaxValue = "";
                    foreach (string aValue in theValues)
                    {
                        int theFrequency = theCounts[i];
                        if (theFrequency > theMax)
                        {
                            // Store this value.
                            theMaxValue = aValue;
                            theMax = theFrequency;
                        }
                        i++; 
                    }
                    // set the value of the field to this object.
                    pRow.set_Value(anOutFieldIndex, theMaxValue); // Could deal with numbers, too...

                    aTracker++;
                }
                // Next feature.
                pCurs.UpdateFeature(pRow); // Commit.
                pRow = pCurs.NextFeature();
            }

            pFC = null;
            pCurs = null;
            pLayer = null;
            pClass = null;

            blResult = true;
            return blResult;
        }

        public void AnnotateLayer(string thisLayer, String LabelExpression, string aFont = "Arial",double aSize = 10, int Red = 0, int Green = 0, int Blue = 0, string OverlapOption = "OnePerShape", bool annotationsOn = true, bool showMapTips = false, string aLogFile = "", bool Messages = false)
        {
            // Options: OnePerShape, OnePerName, OnePerPart and NoRestriction.
            ILayer pLayer = GetLayer(thisLayer, aLogFile, Messages);
            try
            {
                IFeatureLayer pFL = (IFeatureLayer)pLayer;
            }
            catch
            {
                if (Messages)
                    MessageBox.Show("Layer " + thisLayer + " is not a feature layer");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AnnotateLayer returned the following error: Layer " + thisLayer + " is not a feature layer");
                return;
            }

            esriBasicNumLabelsOption esOverlapOption;
            if (OverlapOption == "NoRestriction")
                esOverlapOption = esriBasicNumLabelsOption.esriNoLabelRestrictions;
            else if (OverlapOption == "OnePerName")
                esOverlapOption = esriBasicNumLabelsOption.esriOneLabelPerName;
            else if (OverlapOption == "OnePerPart")
                esOverlapOption = esriBasicNumLabelsOption.esriOneLabelPerPart;
            else
                esOverlapOption = esriBasicNumLabelsOption.esriOneLabelPerShape;

            stdole.IFontDisp fnt = (stdole.IFontDisp)new stdole.StdFontClass();
            fnt.Name = aFont;
            fnt.Size = Convert.ToDecimal(aSize);

            RgbColor annotationLabelColor = new RgbColorClass();
            annotationLabelColor.Red = Red;
            annotationLabelColor.Green = Green;
            annotationLabelColor.Blue = Blue;

            IGeoFeatureLayer geoLayer = pLayer as IGeoFeatureLayer;
            if (geoLayer != null)
            {
                geoLayer.DisplayAnnotation = annotationsOn;
                IAnnotateLayerPropertiesCollection propertiesColl = geoLayer.AnnotationProperties;
                IAnnotateLayerProperties labelEngineProperties = new LabelEngineLayerProperties() as IAnnotateLayerProperties;
                IElementCollection placedElements = new ElementCollectionClass();
                IElementCollection unplacedElements = new ElementCollectionClass();
                propertiesColl.QueryItem(0, out labelEngineProperties, out placedElements, out unplacedElements);
                ILabelEngineLayerProperties lpLabelEngine = labelEngineProperties as ILabelEngineLayerProperties;
                lpLabelEngine.Expression = LabelExpression;
                lpLabelEngine.Symbol.Color = annotationLabelColor;
                lpLabelEngine.Symbol.Font = fnt;
                lpLabelEngine.BasicOverposterLayerProperties.NumLabelsOption = esOverlapOption;
                IFeatureLayer thisFeatureLayer = pLayer as IFeatureLayer;
                IDisplayString displayString = thisFeatureLayer as IDisplayString;
                IDisplayExpressionProperties properties = displayString.ExpressionProperties;
                
                properties.Expression = LabelExpression; //example: "[OWNER_NAME] & vbnewline & \"$\" & [TAX_VALUE]";
                thisFeatureLayer.ShowTips = showMapTips;
            }
        }

        public bool DeleteField(IFeatureClass aFeatureClass, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            // Get the fields collection
            int intIndex = aFeatureClass.Fields.FindField(aFieldName);
            IField pField = aFeatureClass.Fields.get_Field(intIndex);
            bool blResult = false;
            try
            {
                aFeatureClass.DeleteField(pField);
                blResult= true;
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show("Cannot delete field " + aFieldName + ". System error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteField returned the following error: Cannot delete field " + aFieldName + ". System error: " + ex.Message);
            }
            pField = null;
            return blResult;
        }

        public bool DeleteField(string aLayer, string aFieldName, string aLogFile = "", bool Messages = false)
        {
            bool blResult = false;
            if (!LayerExists(aLayer))
            {
                if (Messages)
                    MessageBox.Show("The layer " + aLayer + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function DeleteField returned the following error: The layer " + aLayer + " doesn't exist");
                return blResult;
            }
            IFeatureClass pFC = GetFeatureClassFromLayerName(aLayer, aLogFile, Messages);

            return DeleteField(pFC, aFieldName, aLogFile, Messages);
        }

        public int AddIncrementalNumbers(string aFeatureClass, string aFieldName, string aKeyField, int aStartNumber = 1, string aLogFile = "", bool Messages = false)
        {
            // Check the obvious.
            if (!FeatureclassExists(aFeatureClass))
            {
                if (Messages)
                    MessageBox.Show("The featureclass " + aFeatureClass + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: The featureclass " + aFeatureClass + " doesn't exist");
                return -1;
            }

            if (!FieldExists(aFeatureClass, aFieldName))
            {
                if (Messages)
                    MessageBox.Show("The field " + aFieldName + " does not exist in featureclass " + aFeatureClass);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: The field " + aFieldName + " doesn't exist in feature class " + aFeatureClass);
                return -1;
            }

            if (!FieldIsNumeric(aFeatureClass, aFieldName))
            {
                if (Messages)
                    MessageBox.Show("The field " + aFieldName + " is not numeric");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: The field " + aFieldName + " is not numeric");
                return -1;
            }

            // All hurdles passed - let's do this.
            // Firstly make the list of labels in the correct order.
            // Get the search cursor
            IQueryFilter pQFilt = new QueryFilterClass();
            pQFilt.SubFields = aFieldName + "," + aKeyField;
            IFeatureClass pFC = GetFeatureClass(aFeatureClass);
            IFeatureCursor pSearchCurs = pFC.Search(pQFilt, false);

            // Sort the cursor
            ITableSort pTableSort = new TableSortClass();
            pTableSort.Table = (ITable)pFC;
            pTableSort.Fields = aKeyField;
            pTableSort.Cursor = (ICursor)pSearchCurs;
            pTableSort.Sort(null);
            pSearchCurs = (IFeatureCursor)pTableSort.Rows;
            Marshal.ReleaseComObject(pTableSort); // release the sort object.

            // Extract the lists of values.
            IFields pFields = pFC.Fields;
            int intFieldIndex = pFields.FindField(aFieldName);
            int intKeyFieldIndex = pFields.FindField(aKeyField);
            List<string> KeyList = new List<string>();
            List<int> ValueList = new List<int>(); // These lists are in sync.

            IFeature feature = null;
            int intMax = aStartNumber - 1;
            int intValue = intMax;
            string strKey = "";
            while ((feature = pSearchCurs.NextFeature()) != null)
            {
                string strTest = feature.get_Value(intKeyFieldIndex).ToString();
                if (strTest != strKey) // Different key value
                {
                    // Do we know about it?
                    if (KeyList.IndexOf(strTest) != -1)
                    {
                        intValue = ValueList[KeyList.IndexOf(strTest)];
                        strKey = strTest;
                    }
                    else
                    {
                        intMax++;
                        intValue = intMax;
                        strKey = strTest;
                        KeyList.Add(strKey);
                        ValueList.Add(intValue);
                    }
                }
            }
            Marshal.ReleaseComObject(pSearchCurs);
            pSearchCurs = null;

            // Now do the update.
            IFeatureCursor pUpdateCurs = pFC.Update(pQFilt, false);
            strKey = "";
            intValue = -1;
            try
            {
            while ((feature = pUpdateCurs.NextFeature()) != null)
                {
                    string strTest = feature.get_Value(intKeyFieldIndex).ToString();
                    if (strTest != strKey) // Different key value
                    {
                        // Find out all about it
                        intValue = ValueList[KeyList.IndexOf(strTest)];
                        strKey = strTest;
                    }
                    feature.set_Value(intFieldIndex, intValue);
                    pUpdateCurs.UpdateFeature(feature);
                }
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show("Error: " + ex.Message, "Error");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: " + ex.Message);
                Marshal.ReleaseComObject(pUpdateCurs);
                return -1;
            }

            // If the cursor is no longer needed, release it.
            Marshal.ReleaseComObject(pUpdateCurs);
            pUpdateCurs = null;
            return intMax; // Return the maximum value for info.
        }

        public int AddIncrementalNumbers(string aFeatureClass, string aFieldName, int aStartNumber = 1, string aLogFile = "", bool Messages = false)
        {
            // a version of the above without the use of a key field.

            // Check the obvious.
            if (!FeatureclassExists(aFeatureClass))
            {
                if (Messages)
                    MessageBox.Show("The featureclass " + aFeatureClass + " doesn't exist");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: The featureclass " + aFeatureClass + " doesn't exist");
                return -1;
            }

            if (!FieldExists(aFeatureClass, aFieldName))
            {
                if (Messages)
                    MessageBox.Show("The field " + aFieldName + " does not exist in featureclass " + aFeatureClass);
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: The field " + aFieldName + " doesn't exist in feature class " + aFeatureClass);
                return -1;
            }

            if (!FieldIsNumeric(aFeatureClass, aFieldName))
            {
                if (Messages)
                    MessageBox.Show("The field " + aFieldName + " is not numeric");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: The field " + aFieldName + " is not numeric");
                return -1;
            }

            IFeatureClass pFC = GetFeatureClass(aFeatureClass);

            // Now do the update.
            int intValue = aStartNumber;
            int intFieldIndex = pFC.FindField(aFieldName);
            IFeature feature = null; // Not sure why this is showing in orange??
            IFeatureCursor pUpdateCurs = pFC.Update(null, false); // Get the whole thing.
            try
            {
                while ((feature = pUpdateCurs.NextFeature()) != null)
                {
                    feature.set_Value(intFieldIndex, intValue);
                    pUpdateCurs.UpdateFeature(feature);
                    intValue++;
                }
            }
            catch (Exception ex)
            {
                if (Messages)
                    MessageBox.Show("Error: " + ex.Message, "Error");
                if (aLogFile != "")
                    myFileFuncs.WriteLine(aLogFile, "Function AddIncrementalNumbers returned the following error: " + ex.Message);
                Marshal.ReleaseComObject(pUpdateCurs);
                return -1;
            }

            // If the cursor is no longer needed, release it.
            Marshal.ReleaseComObject(pUpdateCurs);
            pUpdateCurs = null;
            return intValue - 1 ; // Return the maximum value for info.
        }

        public void ToggleDrawing(bool AllowDrawing)
        {
            IMxApplication2 thisApp = (IMxApplication2)thisApplication;
            thisApp.PauseDrawing = !AllowDrawing;
            if (AllowDrawing)
            {
                IActiveView activeView = GetActiveView();
                activeView.Refresh();
            }
        }

        public void ToggleTOC(bool AllowTOC)
        {
            IApplication m_app = thisApplication;

            IDockableWindowManager pDocWinMgr = m_app as IDockableWindowManager;
            UID uid = new UIDClass();
            uid.Value = "{368131A0-F15F-11D3-A67E-0008C7DF97B9}";
            IDockableWindow pTOC = pDocWinMgr.GetDockableWindow(uid);
            pTOC.Show(AllowTOC);
            
            IActiveView activeView = GetActiveView();
            activeView.Refresh();
        }

        public void SetContentsView()
        {
            IApplication m_app = thisApplication;
            IMxDocument mxDoc = (IMxDocument) m_app.Document;
            IContentsView pCV = mxDoc.get_ContentsView(0);
            mxDoc.CurrentContentsView = pCV;

        }

    }
}
