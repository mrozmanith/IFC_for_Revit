﻿//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2012  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using BIM.IFC.Utility;
using BIM.IFC.Toolkit;
using BIM.IFC.Exporter.PropertySet;

namespace BIM.IFC.Exporter
{
    /// <summary>
    /// The comparer for comparing XYZ.
    /// </summary>
    struct XYZComparer : IEqualityComparer<XYZ>
    {
        /// <summary>
        /// Two XYZ is equal if they are almost equal.
        /// </summary>
        /// <param name="x">The XYZ.</param>
        /// <param name="y">The XYZ.</param>
        /// <returns>True if two XYZ are almost equal; false otherwise.</returns>
        public bool Equals(XYZ x, XYZ y)
        {
            return x.IsAlmostEqualTo(y);
        }
        /// <summary>
        /// Return 0 to let call Equals.
        /// </summary>
        /// <param name="obj">The XYZ.</param>
        /// <returns>0 for all XYZ.</returns>
        public int GetHashCode(XYZ obj)
        {
            return 0;
        }
    }

    /// <summary>
    /// Provides methods to export Grid.
    /// </summary>
    class GridExporter
    {
        /// <summary>
        /// Export the Grids.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="document">The document object.</param>
        public static void Export(ExporterIFC exporterIFC, Document document)
        {
            if (ExporterCacheManager.GridCache.Count == 0)
                return;

            // Get all the grids from cache and sorted in levels.
            IDictionary<ElementId, List<Grid>> levelGrids = GetAllGrids(exporterIFC);

            // Get grids in each level and export.
            foreach (ElementId levelId in levelGrids.Keys)
            {
                IDictionary<XYZ, List<Grid>> linearGrids = new Dictionary<XYZ, List<Grid>>(new XYZComparer());
                IDictionary<XYZ, List<Grid>> radialGrids = new Dictionary<XYZ, List<Grid>>(new XYZComparer());
                List<Grid> exportedLinearGrids = new List<Grid>();

                List<Grid> gridsOneLevel = levelGrids[levelId];
                SortGrids(gridsOneLevel, out linearGrids, out radialGrids);

                // Export radial grids first.
                if (radialGrids.Count > 0)
                {
                    ExportRadialGrids(exporterIFC, levelId, radialGrids, ref linearGrids);
                }

                // Export the rectangular and duplex rectangular grids.
                if (linearGrids.Count > 1)
                {
                    ExportRectangularGrids(exporterIFC, levelId, ref linearGrids);
                }             

                // Export the triangular grids
                if (linearGrids.Count > 1)
                {
                    ExportTriangularGrids(exporterIFC, levelId, ref linearGrids);
                }

                // TODO: warn user about orphaned grid lines.
                if (linearGrids.Count == 1)
                    continue;// not export the orphan grid (only has U).
            }
        }

        /// <summary>
        /// Export all the radial Grids.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="levelId">The level id.</param>
        /// <param name="radialGrids">The set of radial grids.</param>
        /// <param name="linearGrids">The set of linear grids.</param>
        public static void ExportRadialGrids(ExporterIFC exporterIFC, ElementId levelId, IDictionary<XYZ, List<Grid>> radialGrids, ref IDictionary<XYZ, List<Grid>> linearGrids)
        {
            foreach (XYZ centerPoint in radialGrids.Keys)
            {
                List<Grid> exportedLinearGrids = new List<Grid>();
                List<Grid> radialUAxes = new List<Grid>();
                List<Grid> radialVAxes = new List<Grid>();
                radialUAxes = radialGrids[centerPoint];
                foreach (XYZ directionVector in linearGrids.Keys)
                {
                    foreach (Grid linearGrid in linearGrids[directionVector])
                    {
                        Line newLine = linearGrid.Curve.Clone() as Line;
                        newLine.MakeUnbound();
                        if (MathUtil.IsAlmostEqual(newLine.Project(centerPoint).Distance, 0.0))
                        {
                            radialVAxes.Add(linearGrid);
                        }
                    }
                }

                // TODO: warn user about orphaned grid lines.
                if (radialVAxes.Count == 0)
                    continue; //not export the orphan grid (only has U).

                // export a radial IFCGrid.
                ExportGrid(exporterIFC, levelId, radialUAxes, radialVAxes, null);

                // remove the linear grids that have been exported.
                exportedLinearGrids = exportedLinearGrids.Union<Grid>(radialVAxes).ToList();
                RemoveExportedGrids(linearGrids, exportedLinearGrids);
            }
        }

        /// <summary>
        /// Export all the rectangular Grids.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="levelId">The level id.</param>
        /// <param name="linearGrids">The set of linear grids.</param>
        public static void ExportRectangularGrids(ExporterIFC exporterIFC, ElementId levelId, ref IDictionary<XYZ, List<Grid>> linearGrids)
        {
            XYZ uDirection = null;
            XYZ vDirection = null;
            List<XYZ> directionList = linearGrids.Keys.ToList();
            while (FindOrthogonalDirectionPair(directionList, out uDirection, out vDirection))
            {
                List<Grid> exportedLinearGrids = new List<Grid>();
                List<Grid> duplexAxesU = FindParallelGrids(linearGrids, uDirection);
                List<Grid> duplexAxesV = FindParallelGrids(linearGrids, vDirection);

                // export a rectangular IFCGrid.
                ExportGrid(exporterIFC, levelId, duplexAxesU, duplexAxesV, null);

                // remove the linear grids that have been exported.
                exportedLinearGrids = exportedLinearGrids.Union<Grid>(duplexAxesU).ToList();
                exportedLinearGrids = exportedLinearGrids.Union<Grid>(duplexAxesV).ToList();
                if (exportedLinearGrids.Count > 0)
                {
                    RemoveExportedGrids(linearGrids, exportedLinearGrids);
                }

                directionList = linearGrids.Keys.ToList();
            }
        }

        /// <summary>
        /// Export all the triangular Grids.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="levelId">The level id.</param>
        /// <param name="linearGrids">The set of linear grids.</param>
        public static void ExportTriangularGrids(ExporterIFC exporterIFC, ElementId levelId, ref IDictionary<XYZ, List<Grid>> linearGrids)
        {
            List<XYZ> directionList = linearGrids.Keys.ToList();
            for (int ii = 0; ii < directionList.Count; ii += 3)
            {
                List<Grid> sameDirectionAxesU = new List<Grid>();
                List<Grid> sameDirectionAxesV = new List<Grid>();
                List<Grid> sameDirectionAxesW = new List<Grid>();
                sameDirectionAxesU = linearGrids[directionList[ii]];
                if (ii + 1 < directionList.Count)
                {
                    sameDirectionAxesV = linearGrids[directionList[ii + 1]];
                }
                if (ii + 2 < directionList.Count)
                {
                    sameDirectionAxesW = linearGrids[directionList[ii + 2]];
                }

                // TODO: warn user about orphaned grid lines.
                if (sameDirectionAxesV.Count == 0)
                    continue;//not export the orphan grid (only has U).

                // export a triangular IFCGrid.
                ExportGrid(exporterIFC, levelId, sameDirectionAxesU, sameDirectionAxesV, sameDirectionAxesW);
            }
        }

        /// <summary>
        /// Export one IFCGrid in one level.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="levelId">The level ID.</param>
        /// <param name="sameDirectionAxesU">The U axes of grids.</param>
        /// <param name="sameDirectionAxesV">The V axes of grids.</param>
        /// <param name="sameDirectionAxesW">The W axes of grids.</param>
        public static void ExportGrid(ExporterIFC exporterIFC, ElementId levelId, List<Grid> sameDirectionAxesU, List<Grid> sameDirectionAxesV, List<Grid> sameDirectionAxesW)
        {

            List<IFCAnyHandle> axesU = null;
            List<IFCAnyHandle> axesV = null;
            List<IFCAnyHandle> axesW = null;
            List<IFCAnyHandle> representations = new List<IFCAnyHandle>();

            using (ProductWrapper productWrapper = ProductWrapper.Create(exporterIFC, true))
            {
                IFCFile ifcFile = exporterIFC.GetFile();
                using (IFCTransaction transaction = new IFCTransaction(ifcFile))
                {
                    GridRepresentationData gridRepresentationData = new GridRepresentationData();

                    axesU = CreateIFCGridAxisAndRepresentations(exporterIFC, productWrapper, sameDirectionAxesU, representations, gridRepresentationData);
                    axesV = CreateIFCGridAxisAndRepresentations(exporterIFC, productWrapper, sameDirectionAxesV, representations, gridRepresentationData);
                    if (sameDirectionAxesW != null)
                        axesW = CreateIFCGridAxisAndRepresentations(exporterIFC, productWrapper, sameDirectionAxesW, representations, gridRepresentationData);

                    IFCAnyHandle contextOfItemsFootPrint = exporterIFC.Get3DContextHandle("FootPrint");
                    string identifierOpt = "FootPrint";
                    string representationTypeOpt = "GeometricCurveSet";

                    int numGridsToExport = gridRepresentationData.m_Grids.Count;
                    if (numGridsToExport == 0)
                        return;

                    bool useIFCCADLayer = !string.IsNullOrWhiteSpace(gridRepresentationData.m_IFCCADLayer);
                    
                    IFCAnyHandle shapeRepresentation = null;

                    HashSet<IFCAnyHandle> allCurves = new HashSet<IFCAnyHandle>();
                    for (int ii = 0; ii < numGridsToExport; ii++)
                        allCurves.UnionWith(gridRepresentationData.m_curveSets[ii]);

                    if (useIFCCADLayer)
                    {
                        shapeRepresentation = RepresentationUtil.CreateShapeRepresentation(exporterIFC, contextOfItemsFootPrint,
                            identifierOpt, representationTypeOpt, allCurves, gridRepresentationData.m_IFCCADLayer);
                    }
                    else
                    {
                        ElementId catId = CategoryUtil.GetSafeCategoryId(gridRepresentationData.m_Grids[0]);
                        shapeRepresentation = RepresentationUtil.CreateShapeRepresentation(exporterIFC, gridRepresentationData.m_Grids[0], catId,
                                contextOfItemsFootPrint, identifierOpt, representationTypeOpt, allCurves);
                    }
                    representations.Add(shapeRepresentation);

                    IFCAnyHandle productRep = IFCInstanceExporter.CreateProductDefinitionShape(ifcFile, null, null, representations);

                    IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(exporterIFC, levelId);

                    string gridGUID = GUIDUtil.CreateGUID();
                    // Get the first grid's override name, if cannot find it, use null.
                    string gridName = GetGridName(sameDirectionAxesU, sameDirectionAxesV, sameDirectionAxesW);
                    IFCAnyHandle ownerHistory = exporterIFC.GetOwnerHistoryHandle();
                    IFCAnyHandle gridLevelHandle = levelInfo.GetBuildingStorey();
                    IFCAnyHandle levelObjectPlacement = IFCAnyHandleUtil.GetInstanceAttribute(gridLevelHandle, "ObjectPlacement");
                    double elev = levelInfo.Elevation;
                    double scaleFactor = exporterIFC.LinearScale;
                    double elevation = elev * scaleFactor;
                    XYZ orig = new XYZ(0.0, 0.0, elevation);
                    IFCAnyHandle copyLevelPlacement = ExporterUtil.CopyLocalPlacement(ifcFile, levelObjectPlacement);
                    IFCAnyHandle ifcGrid = IFCInstanceExporter.CreateGrid(ifcFile, gridGUID, ownerHistory, gridName, null, null, copyLevelPlacement, productRep, axesU, axesV, axesW);
                    productWrapper.AddElement(ifcGrid, levelInfo, null, true);

                    transaction.Commit();
                }
            }
        }

        public class GridRepresentationData
        {
            // The CAD Layer override.
            public string m_IFCCADLayer = null;

            // The ElementIds of the grids to export.
            public List<Element> m_Grids = new List<Element>();

            // The curve sets to export.
            public List<HashSet<IFCAnyHandle>> m_curveSets = new List<HashSet<IFCAnyHandle>>();
        }

        /// <summary>
        /// Get the handles of Grid Axes.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <param name="sameDirectionAxes">The grid axes in the same direction of one level.</param>
        /// <param name="representations">The representation of grid axis.</param>
        /// <returns>The list of handles of grid axes.</returns>
        private static List<IFCAnyHandle> CreateIFCGridAxisAndRepresentations(ExporterIFC exporterIFC, ProductWrapper productWrapper, IList<Grid> sameDirectionAxes, 
            IList<IFCAnyHandle> representations, GridRepresentationData gridRepresentationData)
        {
            if (sameDirectionAxes.Count == 0)
                return null;

            IDictionary<ElementId, List<IFCAnyHandle>> gridAxisMap = new Dictionary<ElementId, List<IFCAnyHandle>>();
            IDictionary<ElementId, List<IFCAnyHandle>> gridRepMap = new Dictionary<ElementId, List<IFCAnyHandle>>();

            IFCFile ifcFile = exporterIFC.GetFile();
            Grid baseGrid = sameDirectionAxes[0];

            Plane plane = new Plane(XYZ.BasisX, XYZ.BasisY, XYZ.Zero);

            List<IFCAnyHandle> ifcGridAxes = new List<IFCAnyHandle>();

            foreach (Grid grid in sameDirectionAxes)
            {
                // Because the IfcGrid is a collection of Revit Grids, any one of them can override the IFC CAD Layer.
                // We will take the first name, and not do too much checking.
                if (string.IsNullOrWhiteSpace(gridRepresentationData.m_IFCCADLayer))
                    ParameterUtil.GetStringValueFromElementOrSymbol(grid, "IFCCadLayer", out gridRepresentationData.m_IFCCADLayer);

                // Get the handle of curve.
                XYZ projectionDirection = plane.Normal;
                IFCGeometryInfo info = IFCGeometryInfo.CreateCurveGeometryInfo(exporterIFC, plane, projectionDirection, false);
                ExporterIFCUtils.CollectGeometryInfo(exporterIFC, info, grid.Curve, XYZ.Zero, false);
                IList<IFCAnyHandle> curves = info.GetCurves();
                if (curves.Count != 1)
                    throw new Exception("IFC: expected 1 curve when export curve element.");
            
                IFCAnyHandle axisCurve = curves[0];

                bool sameSense = true;
                if (baseGrid.Curve is Line)
                {
                    Line baseLine = baseGrid.Curve as Line;
                    Line axisLine = grid.Curve as Line;
                    sameSense = (axisLine.Direction.IsAlmostEqualTo(baseLine.Direction));
                }

                IFCAnyHandle ifcGridAxis = IFCInstanceExporter.CreateGridAxis(ifcFile, grid.Name, axisCurve, sameSense);
                ifcGridAxes.Add(ifcGridAxis);

                HashSet<IFCAnyHandle> AxisCurves = new HashSet<IFCAnyHandle>();
                AxisCurves.Add(axisCurve);

                IFCAnyHandle repItemHnd = IFCInstanceExporter.CreateGeometricCurveSet(ifcFile, AxisCurves);

                // get the weight and color from the GridType to create the curve style.
                GridType gridType = grid.Document.GetElement(grid.GetTypeId()) as GridType;

                IFCData curveWidth = null;
                if (ExporterCacheManager.ExportOptionsCache.ExportAnnotations)
                {
                    int outWidth;
                    double width = ParameterUtil.GetIntValueFromElement(gridType, BuiltInParameter.GRID_END_SEGMENT_WEIGHT, out outWidth) ? outWidth : 1;
                    curveWidth = IFCDataUtil.CreateAsPositiveLengthMeasure(width);
                }

                int outColor;
                int color = ParameterUtil.GetIntValueFromElement(gridType, BuiltInParameter.GRID_END_SEGMENT_COLOR, out outColor) ? outColor : 0;
                double blueVal = 0.0;
                double greenVal = 0.0;
                double redVal = 0.0;
                GeometryUtil.GetRGBFromIntValue(color, out blueVal, out greenVal, out redVal);
                IFCAnyHandle colorHnd = IFCInstanceExporter.CreateColourRgb(ifcFile, null, redVal, greenVal, blueVal);

                BodyExporter.CreateCurveStyleForRepItem(exporterIFC, repItemHnd, curveWidth, colorHnd);

                HashSet<IFCAnyHandle> curveSet = new HashSet<IFCAnyHandle>();
                curveSet.Add(repItemHnd);
                
                gridRepresentationData.m_Grids.Add(grid);
                gridRepresentationData.m_curveSets.Add(curveSet);

                PropertyUtil.CreateInternalRevitPropertySets(exporterIFC, grid, productWrapper);
            }

            return ifcGridAxes;
        }

        /// <summary>
        /// Get all the grids and add to the map with its level.
        /// </summary>
        /// <param name="exporterIFC">The ExporterIFC object.</param>
        /// <returns>The map with sorted grids by level.</returns>
        private static IDictionary<ElementId, List<Grid>> GetAllGrids(ExporterIFC exporterIFC)
        {
            View currentView = ExporterCacheManager.ExportOptionsCache.FilterViewForExport;
            Level currentLevel = null;
            if (currentView != null)
            {
                currentLevel = currentView.GenLevel;
            }
            List<ElementId> levelIds = new List<ElementId>();
            if (currentLevel != null)
            {
                levelIds.Add(currentLevel.Id);
            }
            else
            {
                levelIds = ExporterCacheManager.LevelInfoCache.LevelsByElevation;
            }

            IDictionary<ElementId, double> LevelHeightMap = new Dictionary<ElementId, double>();
            foreach (ElementId levelId in levelIds)
            {
                IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(exporterIFC, levelId);
                if (levelInfo == null)
                    continue;
                if (!LevelHeightMap.ContainsKey(levelId))
                {
                    LevelHeightMap.Add(levelId, levelInfo.Elevation);
                }
            }

            double eps = MathUtil.Eps();
            IDictionary<ElementId, List<Grid>> levelGrids = new Dictionary<ElementId, List<Grid>>();
            foreach (Element element in ExporterCacheManager.GridCache)
            {
                Grid grid = element as Grid;                
                XYZ minPoint = grid.GetExtents().MinimumPoint;
                XYZ maxPoint = grid.GetExtents().MaximumPoint;

                foreach (ElementId levelId in LevelHeightMap.Keys)
                {
                    if (minPoint.Z <= LevelHeightMap[levelId] + eps && LevelHeightMap[levelId] - eps <= maxPoint.Z)
                    {
                        if (!levelGrids.ContainsKey(levelId))
                            levelGrids.Add(levelId, new List<Grid>());
                        levelGrids[levelId].Add(grid);
                    }
                }
            }
            return levelGrids;
        }

        /// <summary>
        /// Sort the grids in linear and radial shape.
        /// </summary>
        /// <param name="gridsOneLevel">The grids in one level.</param>
        /// <param name="linearGrids">The linear grids in one level.</param>
        /// <param name="radialGrids">The radial grids in one level.</param>
        private static void SortGrids(List<Grid> gridsOneLevel, out IDictionary<XYZ, List<Grid>> linearGrids, out IDictionary<XYZ, List<Grid>> radialGrids)
        {
            linearGrids = new Dictionary<XYZ, List<Grid>>(new XYZComparer());
            radialGrids = new Dictionary<XYZ, List<Grid>>(new XYZComparer());

            foreach (Grid grid in gridsOneLevel)
            {
                if (grid.Curve is Line)
                {
                    Line line = grid.Curve as Line;
                    XYZ directionVector = line.Direction;
                    if (!linearGrids.ContainsKey(directionVector))
                    {
                        linearGrids.Add(directionVector, new List<Grid>());
                    }

                    linearGrids[directionVector].Add(grid);
                }
                if (grid.Curve is Arc)
                {
                    Arc arc = grid.Curve as Arc;
                    XYZ arcCenter = arc.Center;
                    if (!radialGrids.ContainsKey(arcCenter))
                    {
                        radialGrids.Add(arcCenter, new List<Grid>());
                    }

                    radialGrids[arcCenter].Add(grid);
                }
            }
        }

        /// <summary>
        /// Remove the exported grids from set of linear grids.
        /// </summary>
        /// <param name="linearGrids">The set of linear grids.</param>
        /// <param name="exportedLinearGrids">The exported grids.</param>
        private static void RemoveExportedGrids(IDictionary<XYZ, List<Grid>> linearGrids, List<Grid> exportedLinearGrids)
        {
            foreach (Grid exportedGrid in exportedLinearGrids)
            {
                Line line = exportedGrid.Curve as Line;
                if (linearGrids.ContainsKey(line.Direction))
                {
                    linearGrids[line.Direction].Remove(exportedGrid);
                    if (linearGrids[line.Direction].Count == 0)
                    {
                        linearGrids.Remove(line.Direction);
                    }
                }
            }
        }

        /// <summary>
        /// Find the orthogonal directions for rectangular IFCGrid.
        /// </summary>
        /// <param name="directionList">The directions.</param>
        /// <param name="uDirection">The U direction.</param>
        /// <param name="vDirection">The V direction.</param>
        /// <returns>True if find a pair of orthogonal directions for grids; false otherwise.</returns>
        private static bool FindOrthogonalDirectionPair(List<XYZ> directionList, out XYZ uDirection, out XYZ vDirection)
        {
            uDirection = null;
            vDirection = null;

            foreach (XYZ uDir in directionList)
            {
                foreach (XYZ vDir in directionList)
                {
                    double dotProduct = uDir.DotProduct(vDir);
                    if (MathUtil.IsAlmostEqual(Math.Abs(dotProduct), 0.0))
                    {
                        uDirection = uDir;
                        vDirection = vDir;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Find the list of parallel linear grids via the given direction.
        /// </summary>
        /// <param name="linearGrids">The set of linear grids.</param>
        /// <param name="baseDirection">The given direction.</param>
        /// <returns>The list of parallel grids, containing the anti direction grids.</returns>
        private static List<Grid> FindParallelGrids(IDictionary<XYZ, List<Grid>> linearGrids, XYZ baseDirection)
        {
            List<XYZ> directionList = linearGrids.Keys.ToList();
            List<Grid> parallelGrids = linearGrids[baseDirection];
            foreach (XYZ direction in directionList)
            {
                if (baseDirection.IsAlmostEqualTo(direction))
                    continue;
                double dotProduct = direction.DotProduct(baseDirection);
                if (MathUtil.IsAlmostEqual(dotProduct, -1.0))
                {
                    parallelGrids = parallelGrids.Union<Grid>(linearGrids[direction]).ToList();
                    return parallelGrids;
                }
            }
            return parallelGrids;
        }

        /// <summary>
        /// Get the Grid name from the U, V, W grid lines.
        /// </summary>
        /// <param name="sameDirectionAxesU">The U direction of grids.</param>
        /// <param name="sameDirectionAxesV">The V direction of grids.</param>
        /// <param name="sameDirectionAxesW">The W direction of grids.</param>
        /// <returns>The NameOverride if any grid defines the parameter; null otherwise.</returns>
        private static string GetGridName(List<Grid> sameDirectionAxesU, List<Grid> sameDirectionAxesV, List<Grid> sameDirectionAxesW)
        {
            string gridName = GetOverrideGridName(sameDirectionAxesU);
            if (gridName == null)
                gridName = GetOverrideGridName(sameDirectionAxesV);
            if (gridName == null)
                gridName = GetOverrideGridName(sameDirectionAxesW);
            return gridName;
        }

        /// <summary>
        /// Get the first override Grid name from a collection of grids.
        /// </summary>
        /// <param name="gridList">The collection of grids.</param>
        /// <returns>The NameOverride if any grid defines the parameter; else return null.</returns>
        private static string GetOverrideGridName(List<Grid> gridList)
        {
            if (gridList == null)
                return null;

            foreach (Grid grid in gridList)
            {
                string gridName = NamingUtil.GetNameOverride(grid, null);
                if (gridName != null)
                    return gridName;
            }
            return null;
        }
    }
}
