﻿//
// BIM IFC library: this library works with Autodesk(R) Revit(R) to export IFC files containing model geometry.
// Copyright (C) 2015  Autodesk, Inc.
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
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Toolkit;

namespace Revit.IFC.Export.Utility
{
   /// <summary>
   /// Provides static methods for level related manipulations.
   /// </summary>
   public class LevelUtil
   {
      private class LevelElevationComparer : IComparer<Level>
      {
         #region IComparer<Level> Members

         public int Compare(Level x, Level y)
         {
            if (x == null || y == null)
            {
               if (x == y)
                  return 0;
               if (x == null)
                  return -1;
               return 1;
            }

            if (x.Id == y.Id)
               return 0;

            if (x.Elevation == y.Elevation)
               return (x.Id > y.Id) ? 1 : -1;
            return (x.Elevation > y.Elevation) ? 1 : -1;
         }

         #endregion
      }

      /// <summary>
      /// Gets all levels from Revit document.
      /// </summary>
      /// <param name="document">
      /// The document.
      /// </param>
      /// <returns>
      /// The list of levels found
      /// </returns>
      public static List<Level> FindAllLevels(Document document)
      {
         ElementFilter elementFilter = new ElementClassFilter(typeof(Level));
         FilteredElementCollector collector = new FilteredElementCollector(document);
         List<Level> allLevels = collector.WherePasses(elementFilter).Cast<Level>().ToList();
         LevelElevationComparer comparer = new LevelElevationComparer();
         allLevels.Sort(comparer);
         return allLevels;
      }

      /// <summary>
      /// Checks to see if a particular level is a building story.  Returns true as default.
      /// </summary>
      /// <param name="level">
      /// The level.
      /// </param>
      /// <returns>
      /// True if the level is a building story, false otherwise.
      /// </returns>
      public static bool IsBuildingStory(Level level)
      {
         if (level == null)
            return false;

         if (ExporterCacheManager.ExportOptionsCache.ExportAllLevels)
            return true;

         Parameter isBuildingStorey = level.get_Parameter(BuiltInParameter.LEVEL_IS_BUILDING_STORY);
         if (isBuildingStorey == null)
            return true;

         return (isBuildingStorey.AsInteger() != 0);
      }

      /// <summary>
      /// Gets a representative view generated by the level.
      /// </summary>
      /// <param name="document">The document.</param>
      /// <param name="viewType">The view type.</param>
      /// <param name="level">The level.</param>
      /// <returns>A view associate with the level.</returns>
      /// <remarks>This function will return the first view it finds with a bottom range set to an offset of the generating level.
      /// If no such view is found, but a view based on the generating level exists, that view will be returned.</remarks>
      public static View FindViewByLevel(Document document, ViewType viewType, Level level)
      {
         View possibleView = null;

         ElementFilter elementFilter = new ElementClassFilter(typeof(View));
         FilteredElementCollector collector = new FilteredElementCollector(document);
         collector.WherePasses(elementFilter);
         foreach (View view in collector)
         {
            if (view.ViewType != viewType)
               continue;
            if (view.GenLevel != null && view.GenLevel.Id == level.Id)
            {
               PlanViewRange viewRange = (view as ViewPlan).GetViewRange();
               ElementId bottomClipPlaneId = viewRange.GetLevelId(PlanViewPlane.BottomClipPlane);
               if (bottomClipPlaneId != level.Id)
               {
                  possibleView = view;
                  continue;
               }
               return view;
            }
         }
         return possibleView;
      }

      /// <summary>
      /// Checks if view is generated by the level.
      /// </summary>
      /// <param name="view">
      /// The view.
      /// </param>
      /// <param name="level">
      /// The level.
      /// </param>
      /// <returns>
      /// True if the view is generated by the level, false otherwise.
      /// </returns>
      public static bool IsViewGeneratedByLevel(View view, Level level)
      {
         Level genLevel = view.GenLevel;
         if (genLevel == null)
            return false;

         return genLevel.Id == level.Id;
      }

      /// <summary>
      /// Gets level extension.
      /// </summary>
      /// <remarks>
      /// When we are splitting columns (or walls) by level, we allow for a certain amount of overflow into the next level.
      /// This has been set (somewhat arbitrarily) at 10cm.
      /// </remarks>
      /// <returns>
      /// The level extension.
      /// </returns>
      public static double GetLevelExtension()
      {
         // 10cm, in feet.
         return 10.0 / (12.0 * 2.54);
      }

      /// <summary>
      /// Calculates the distance to the next level if the UpToLevel parameter is set.
      /// If the parameter is not set, or the distance is negative, 0 is returned.
      /// This is not set in IFCLevelInfo as we want this calculation to be done in .NET code.
      /// </summary>
      /// <param name="doc">The document.</param>
      /// <param name="levelId">The element id of the level.</param>
      /// <param name="levelInfo">The associated level information object.</param>
      public static double CalculateDistanceToNextLevel(Document doc, ElementId levelId, IFCLevelInfo levelInfo)
      {
         double height = 0.0;
         Level level = doc.GetElement(levelId) as Level;
         ElementId nextLevelId = ElementId.InvalidElementId;

         if (level != null)
         {
            Parameter nextLevelParameter = level.get_Parameter(BuiltInParameter.LEVEL_UP_TO_LEVEL);
            if (nextLevelParameter != null)
            {
               Element nextLevelAsElement = doc.GetElement(nextLevelParameter.AsElementId());
               if (nextLevelAsElement != null)
               {
                  Level possibleNextLevel = nextLevelAsElement as Level;
                  if (possibleNextLevel != null && IsBuildingStory(possibleNextLevel))
                  {
                     double nextLevelElevation = possibleNextLevel.Elevation;
                     double netElevation = nextLevelElevation - level.Elevation;
                     if (netElevation > 0.0)
                     {
                        height = netElevation;
                        nextLevelId = nextLevelParameter.AsElementId();
                     }
                  }
               }
            }
         }

         if ((height <= 0.0) && (levelInfo != null))
            height = levelInfo.DistanceToNextLevel;

         ExporterCacheManager.LevelInfoCache.Register(levelId, nextLevelId, height);
         return height;
      }

      /// <summary>
      /// Returns the base level of an element as specified in its parameters.
      /// </summary>
      /// <remarks>
      /// Only implemented for Walls and FamilyInstances.
      /// </remarks>
      private static ElementId GetBaseLevelIdForElement(Element element)
      {
         BuiltInParameter paramId = BuiltInParameter.INVALID;

         if (element is FamilyInstance)
         {
            paramId = BuiltInParameter.FAMILY_BASE_LEVEL_PARAM;
         }
         else if (element is Wall)
         {
            paramId = BuiltInParameter.WALL_BASE_CONSTRAINT;
         }

         if (paramId != BuiltInParameter.INVALID)
         {
            Parameter nextLevelParameter = element.get_Parameter(paramId);
            if (nextLevelParameter != null)
               return nextLevelParameter.AsElementId();
         }

         return ElementId.InvalidElementId;
      }

      /// <summary>
      /// Creates a list of ranges to split an element.
      /// </summary>
      /// <remarks>
      /// We may need to split an element (e.g. column) into parts by level.
      /// </remarks>
      /// <param name="exporterIFC">The ExporterIFC object. </param>
      /// <param name="exportType">The export type. </param>
      /// <param name="element">The element. </param>
      /// <param name="levels">The levels to split the element.</param>
      /// <param name="ranges">The ranges to split the element. These will be non-overlapping.</param>
      public static void CreateSplitLevelRangesForElement(ExporterIFC exporterIFC, IFCExportType exportType, Element element,
         out IList<ElementId> levels, out IList<IFCRange> ranges)
      {
         levels = new List<ElementId>();
         ranges = new List<IFCRange>();

         if (!ExporterCacheManager.ExportOptionsCache.WallAndColumnSplitting)
            return;

         double extension = GetLevelExtension();

         bool splitByLevel = (exportType == IFCExportType.IfcColumnType || exportType == IFCExportType.IfcWall) || (exportType == IFCExportType.IfcDuctSegmentType);

         if (!splitByLevel)
            return;

         BoundingBoxXYZ boundingBox = element.get_BoundingBox(null);
         if (boundingBox == null)
            return;

         {
            IFCRange zSpan = new IFCRange(boundingBox.Min.Z, boundingBox.Max.Z);
            if (zSpan.Start < zSpan.End)
            {
               bool firstLevel = true;
               ElementId skipToNextLevel = ElementId.InvalidElementId;

               // If the base level of the element is set, we will start "looking" at that level.  Anything below the base level will be included with the base level.
               // We will only do this if the base level is a building story.
               ElementId firstLevelId = GetBaseLevelIdForElement(element);
               bool foundFirstLevel = (firstLevelId == ElementId.InvalidElementId);

               IList<ElementId> levelIds = ExporterCacheManager.LevelInfoCache.BuildingStoreysByElevation;
               foreach (ElementId levelId in levelIds)
               {
                  if (!foundFirstLevel)
                  {
                     if (levelId != firstLevelId)
                        continue;
                     else
                        foundFirstLevel = true;
                  }

                  if (skipToNextLevel != ElementId.InvalidElementId && levelId != skipToNextLevel)
                     continue;

                  IFCLevelInfo levelInfo = ExporterCacheManager.LevelInfoCache.GetLevelInfo(exporterIFC, levelId);
                  if (levelInfo == null)
                     continue;

                  // endBelowLevel 
                  if (zSpan.End < levelInfo.Elevation + extension)
                     continue;

                  // To calculate the distance to the next level, we check to see if the Level UpToLevel built-in parameter
                  // is set.  If so, we calculate the distance by getting the elevation of the UpToLevel level minus the
                  // current elevation, and use it if it is greater than 0.  If it is not greater than 0, or the built-in
                  // parameter is not set, we use DistanceToNextLevel.
                  double levelHeight = ExporterCacheManager.LevelInfoCache.FindHeight(levelId);
                  if (levelHeight < 0.0)
                     levelHeight = CalculateDistanceToNextLevel(element.Document, levelId, levelInfo);
                  skipToNextLevel = ExporterCacheManager.LevelInfoCache.FindNextLevel(levelId);

                  // startAboveLevel
                  if ((!MathUtil.IsAlmostZero(levelHeight)) &&
                     (zSpan.Start > levelInfo.Elevation + levelHeight - extension))
                     continue;

                  bool startBelowLevel = !firstLevel && (zSpan.Start < levelInfo.Elevation - extension);
                  bool endAboveLevel = ((!MathUtil.IsAlmostZero(levelHeight)) &&
                     (zSpan.End > levelInfo.Elevation + levelHeight + extension));
                  if (!startBelowLevel && !endAboveLevel)
                     break;

                  IFCRange currentSpan = new IFCRange(
                     startBelowLevel ? levelInfo.Elevation : zSpan.Start,
                     endAboveLevel ? (levelInfo.Elevation + levelHeight) : zSpan.End);

                  // We want our ranges to be non-overlapping.  As such, we'll modify the start parameter
                  // to be at least as large as the previous end parameter (if any).  If this makes the
                  // range invalid, we won't add it.
                  if (ranges.Count > 0)
                  {
                     IFCRange lastSpan = ranges.Last();
                     if (lastSpan.End > currentSpan.End - MathUtil.Eps())
                        continue;

                     currentSpan.Start = Math.Max(currentSpan.Start, lastSpan.End);
                  }

                  ranges.Add(currentSpan);
                  levels.Add(levelId);

                  firstLevel = false;
               }
            }
         }
      }

      /// <summary>
      /// Gets IFCElementComposition types having "COMPLEX","ELEMENT","PARTIAL" values for levels
      /// </summary>
      /// <param name="element">The element </param>
      public static IFCElementComposition GetElementCompositionTypeOverride(Element element)
      {      
         string nameOverride = "IfcElementCompositionType";

         // If the IfcElementCompositionType is not set by the user, 
         // it sets the value to .ELEMENT by default
         string overrideValue = NamingUtil.GetOverrideStringValue(element, nameOverride, "Element");

         IFCElementComposition ifcElementCompositionType;
         
         if (Enum.TryParse<IFCElementComposition>(overrideValue, true, out ifcElementCompositionType))
         {
            return ifcElementCompositionType;
         }

         return IFCElementComposition.Element;   
    
      }

   }
}