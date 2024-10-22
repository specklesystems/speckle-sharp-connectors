using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.Helpers;

public sealed class CorridorHandler
{
  /// <summary>
  /// Keeps track of all corridor solids by their hierarchy of (corridor, baseline, region, applied assembly, applied subassembly) in the current send operation.
  /// This should be added to the display value of the corridor applied subassemblies after they are processed
  /// Handles should be used instead of Handle.Value (as is typically used for speckle app ids) since the exported solid property sets only stores the handle
  /// </summary>
  private Dictionary<(string, string, string, string, string), List<SOG.Mesh>> CorridorSolidsCache { get; } = new();

  // these ints are used to retrieve the correct values from the exported corridor solids property sets to cache them
  // they were determined via trial and error
#pragma warning disable CA1805 // Initialized explicitly to 0
  private readonly int _corridorHandleIndex = 0;
#pragma warning restore CA1805 // Initialized explicitly to 0
  private readonly int _baselineGuidIndex = 6;
  private readonly int _regionGuidIndex = 7;
  private readonly int _assemblyHandleIndex = 3;
  private readonly int _subassemblyHandleIndex = 4;

  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _solidConverter;
  private readonly ITypedConverter<ADB.Body, SOG.Mesh> _bodyConverter;
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Point3dCollection, SOG.Polyline> _pointCollectionConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CorridorHandler(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    ITypedConverter<ADB.Body, SOG.Mesh> bodyConverter,
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> pointCollectionConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _solidConverter = solidConverter;
    _bodyConverter = bodyConverter;
    _pointConverter = pointConverter;
    _pointCollectionConverter = pointCollectionConverter;
    _settingsStore = settingsStore;
  }

  // Ok, this is going to be very complicated.
  // We are building a nested `Base.elements` of corridor subelements in this hierarchy: corridor -> baselines -> baseline regions -> applied assemblies -> applied subassemblies
  // This is because none of these entities inherit from CDB.Entity, and we need to match the corridor solids with the corresponding applied subassembly.
  public List<Base> GetCorridorChildren(CDB.Corridor corridor)
  {
    // first extract all corridor solids.
    // this needs to be done before traversing children, so we can match the solid mesh to the appropriate subassembly
    HandleCorridorSolids(corridor);

    // track children hierarchy ids:
    string corridorHandle = corridor.Handle.ToString();

    // process baselines and any featurelines found
    List<Base> baselines = new(corridor.Baselines.Count);
    foreach (CDB.Baseline baseline in corridor.Baselines)
    {
      string baselineGuid = baseline.baselineGUID.ToString();

      Base convertedBaseline =
        new()
        {
          ["type"] = baseline.GetType().ToString().Split('.').Last(),
          ["name"] = baseline.Name,
          ["startStation"] = baseline.StartStation,
          ["endStation"] = baseline.EndStation,
          ["units"] = _settingsStore.Current.SpeckleUnits,
          ["applicationId"] = baselineGuid,
        };

      // get profile and alignment if nonfeaturelinebased
      // for featureline based corridors, accessing AlignmentId and ProfileId will return NULL
      // and throw an exception ""This operation on feature line based baseline is invalid".
      if (baseline.IsFeatureLineBased())
      {
        convertedBaseline["featureLineId"] = baseline.FeatureLineId.GetSpeckleApplicationId();
      }
      else
      {
        convertedBaseline["alignmentId"] = baseline.AlignmentId.GetSpeckleApplicationId();
        convertedBaseline["profileId"] = baseline.ProfileId.GetSpeckleApplicationId();
      }

      // get baseline featurelines
      List<Base> mainFeatureLines = new();
      foreach (
        CDB.FeatureLineCollection mainFeaturelineCollection in baseline
          .MainBaselineFeatureLines
          .FeatureLineCollectionMap
      )
      {
        foreach (CDB.CorridorFeatureLine featureline in mainFeaturelineCollection)
        {
          mainFeatureLines.Add(FeatureLineToSpeckle(featureline));
        }
      }
      if (mainFeatureLines.Count > 0)
      {
        convertedBaseline["mainBaselineFeatureLines"] = mainFeatureLines;
      }

      List<Base> offsetFeatureLines = new();
      foreach (CDB.BaselineFeatureLines offsetFeaturelineCollection in baseline.OffsetBaselineFeatureLinesCol) // offset featurelines
      {
        foreach (
          CDB.FeatureLineCollection featurelineCollection in offsetFeaturelineCollection.FeatureLineCollectionMap
        )
        {
          foreach (CDB.CorridorFeatureLine featureline in featurelineCollection)
          {
            offsetFeatureLines.Add(FeatureLineToSpeckle(featureline));
          }
        }
      }
      if (offsetFeatureLines.Count > 0)
      {
        convertedBaseline["offsetBaselineFeatureLines"] = mainFeatureLines;
      }

      // get the baseline regions
      List<Base> regions = new();
      foreach (CDB.BaselineRegion region in baseline.BaselineRegions)
      {
#if CIVIL3D2023_OR_GREATER
        string regionGuid = region.RegionGUID.ToString();
#else
        string regionGuid = "";
#endif

        Base convertedRegion =
          new()
          {
            ["type"] = region.GetType().ToString().Split('.').Last(),
            ["name"] = region.Name,
            ["startStation"] = region.StartStation,
            ["endStation"] = region.EndStation,
            ["units"] = _settingsStore.Current.SpeckleUnits,
            ["applicationId"] = regionGuid,
          };

        // traverse region assembly for subassemblies and codes
        // display values (corridor solids) will be dumped here, by their code
        Dictionary<ADB.ObjectId, string> subassemblyNameCache = new();
        using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
        {
          var assembly = (CDB.Assembly)tr.GetObject(region.AssemblyId, ADB.OpenMode.ForRead);
          string assemblyHandle = region.AssemblyId.Handle.ToString();

          // traverse groups for subassemblies
          List<Base> subassemblies = new();
          foreach (CDB.AssemblyGroup group in assembly.Groups)
          {
            foreach (ADB.ObjectId subassemblyId in group.GetSubassemblyIds())
            {
              var subassembly = (CDB.Subassembly)tr.GetObject(subassemblyId, ADB.OpenMode.ForRead);
              string subassemblyHandle = subassemblyId.Handle.ToString();

              // store name in cache for later use by applied subassemblies
              subassemblyNameCache[subassemblyId] = subassembly.Name;

              Base convertedSubassembly =
                new()
                {
                  ["name"] = subassembly.Name,
                  ["type"] = subassembly.GetType().ToString().Split('.').Last(),
                  applicationId = subassembly.GetSpeckleApplicationId()
                };

              // try to get the display value mesh
              (string, string, string, string, string) corridorSolidsKey = (
                corridorHandle,
                baselineGuid,
                regionGuid,
                assemblyHandle,
                subassemblyHandle
              );
              if (CorridorSolidsCache.TryGetValue(corridorSolidsKey, out List<SOG.Mesh>? display))
              {
                convertedSubassembly["displayValue"] = display;
              }

              subassemblies.Add(convertedSubassembly);
            }
          }

          Base convertedAssembly =
            new()
            {
              ["name"] = assembly.Name,
              ["type"] = assembly.GetType().ToString().Split('.').Last(),
              ["elements"] = subassemblies,
              applicationId = assembly.GetSpeckleApplicationId()
            };

          convertedRegion["elements"] = convertedAssembly;

          tr.Commit();
        }

        // now get all region applied assemblies, applied subassemblies, and calculated shapes, links, and points as dicts
        Dictionary<string, object?> appliedAssemblies = new();
        double[] sortedStations = region.SortedStations();
        for (int i = 0; i < sortedStations.Length; i++)
        {
          double station = sortedStations[i];

          CDB.AppliedAssembly appliedAssembly = region.AppliedAssemblies[i];

          Dictionary<string, object?> appliedAssemblyDict =
            new() { ["assemblyId"] = appliedAssembly.AssemblyId.GetSpeckleApplicationId(), ["station"] = station };

          try
          {
            appliedAssemblyDict["adjustedElevation"] = appliedAssembly.AdjustedElevation;
          }
          catch (ArgumentException e) when (!e.IsFatal())
          {
            // Do nothing. Leave the value as null. Not sure why accessing adjusted elevation sometimes throws.
          }

          // get the applied assembly's applied subassemblies
          Dictionary<string, object?> appliedSubassemblies = new();
          foreach (CDB.AppliedSubassembly appliedSubassembly in appliedAssembly.GetAppliedSubassemblies())
          {
            string subassemblyId = appliedSubassembly.SubassemblyId.GetSpeckleApplicationId();
            string name = subassemblyNameCache.TryGetValue(appliedSubassembly.SubassemblyId, out string? cachedName)
              ? cachedName
              : subassemblyId;

            Dictionary<string, object?> appliedSubassemblyDict =
              new()
              {
                ["subassemblyId"] = subassemblyId,
                ["calculatedShapes"] = GetCalculatedShapes(appliedSubassembly)
              };

            appliedSubassemblies[name] = appliedSubassemblyDict;
          }
          appliedAssemblyDict["appliedSubassemblies"] = appliedSubassemblies;

          appliedAssemblies[station.ToString()] = appliedAssemblyDict;
        }

        convertedRegion["appliedAssemblies"] = appliedAssemblies;
        regions.Add(convertedRegion);
      }

      convertedBaseline["elements"] = regions;
      baselines.Add(convertedBaseline);
    }

    return baselines;
  }

  // Gets the calculated shapes > calculated links > calculated points of an applied subassembly
  private Dictionary<string, object?> GetCalculatedShapes(CDB.AppliedSubassembly appliedSubassembly)
  {
    Dictionary<string, object?> calculatedShapes = new();
    int shapeCount = 0;
    foreach (CDB.CalculatedShape shape in appliedSubassembly.Shapes)
    {
      Dictionary<string, object?> calculatedLinks = new();
      int linkCount = 0;
      foreach (CDB.CalculatedLink link in shape.CalculatedLinks)
      {
        Dictionary<string, object?> calculatedPoints = new();
        int pointCount = 0;
        foreach (CDB.CalculatedPoint point in link.CalculatedPoints)
        {
          calculatedPoints[pointCount.ToString()] = new Dictionary<string, object?>()
          {
            ["xyz"] = _pointConverter.Convert(point.XYZ),
            ["corridorCodes"] = point.CorridorCodes.ToList(),
            ["stationOffsetElevationToBaseline"] = point.StationOffsetElevationToBaseline.ToArray(),
          };
          pointCount++;
        }

        calculatedLinks[linkCount.ToString()] = new Dictionary<string, object?>()
        {
          ["corridorCodes"] = link.CorridorCodes.ToList(),
          ["calculatedPoints"] = calculatedPoints
        };

        linkCount++;
      }

      calculatedShapes[shapeCount.ToString()] = new Dictionary<string, object?>()
      {
        ["corridorCodes"] = shape.CorridorCodes.ToList(),
        ["area"] = shape.Area,
        ["calculatedLinks"] = calculatedLinks
      };
    }
    return calculatedShapes;
  }

  private Base FeatureLineToSpeckle(CDB.CorridorFeatureLine featureline)
  {
    // get the display polylines
    var polylines = new List<SOG.Polyline>();

    var polylinePoints = new AG.Point3dCollection();
    for (int i = 0; i < featureline.FeatureLinePoints.Count; i++)
    {
      var point = featureline.FeatureLinePoints[i];
      if (!point.IsBreak)
      {
        polylinePoints.Add(point.XYZ);
      }
      if (polylinePoints.Count > 1 && (i == featureline.FeatureLinePoints.Count - 1 || point.IsBreak))
      {
        polylines.Add(_pointCollectionConverter.Convert(polylinePoints));
        polylinePoints.Clear();
      }
    }

    // create featureline
    return new()
    {
      ["name"] = featureline.CodeName,
      ["type"] = featureline.GetType().ToString().Split('.').Last(),
      ["codeName"] = featureline.CodeName,
      ["displayValue"] = polylines
    };
  }

  /// <summary>
  /// Extracts the solids from a corridor and stores in <see cref="CorridorSolidsCache"/> according to property sets on the solid.
  /// NOTE: The Export Solids method is only available for version 2024 or greater
  /// </summary>
  /// <param name="corridor"></param>
  /// <returns></returns>
  private void HandleCorridorSolids(CDB.Corridor corridor)
  {
#if CIVIL3D2024_OR_GREATER
    CDB.ExportCorridorSolidsParams param = new();

    using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      foreach (ADB.ObjectId solidId in corridor.ExportSolids(param, corridor.Database))
      {
        SOG.Mesh? mesh = null;
        var solid = tr.GetObject(solidId, ADB.OpenMode.ForRead);
        if (solid is ADB.Solid3d solid3d)
        {
          // get the solid mesh
          mesh = _solidConverter.Convert(solid3d);
        }
        else if (solid is ADB.Body body)
        {
          mesh = _bodyConverter.Convert(body);
        }

        if (mesh is null)
        {
          continue;
        }

        // get the (corridor handle, baseline guid, region guid, assembly handle, subassembly handle) of the solid property sets
        (string, string, string, string, string)? solidKey = GetCorridorSolidIdFromPropertySet(solid, tr);

        if (solidKey is (string, string, string, string assemblyHandle, string subassemblyHandle) validSolidKey)
        {
          if (CorridorSolidsCache.TryGetValue(validSolidKey, out List<SOG.Mesh>? display))
          {
            display.Add(mesh);
          }
          else
          {
            CorridorSolidsCache[validSolidKey] = new() { mesh };
          }
        }
      }

      tr.Commit();
    }
#endif
  }

  private (string, string, string, string, string)? GetCorridorSolidIdFromPropertySet(
    ADB.DBObject obj,
    ADB.Transaction tr
  )
  {
    ADB.ObjectIdCollection? propertySetIds;

    try
    {
      propertySetIds = AAECPDB.PropertyDataServices.GetPropertySets(obj);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      return null;
    }

    if (propertySetIds is null || propertySetIds.Count == 0)
    {
      return null;
    }

    foreach (ADB.ObjectId id in propertySetIds)
    {
      AAECPDB.PropertySet propertySet = (AAECPDB.PropertySet)tr.GetObject(id, ADB.OpenMode.ForRead);

      if (propertySet.PropertySetDefinitionName == "Corridor Identity")
      {
        if (propertySet.PropertySetData[_corridorHandleIndex].GetData() is not string corridorHandle)
        {
          return null;
        }
        if (propertySet.PropertySetData[_baselineGuidIndex].GetData() is not string baselineGuid)
        {
          return null;
        }
        if (propertySet.PropertySetData[_regionGuidIndex].GetData() is not string regionGuid)
        {
          return null;
        }
        if (propertySet.PropertySetData[_assemblyHandleIndex].GetData() is not string assemblyHandle)
        {
          return null;
        }
        if (propertySet.PropertySetData[_subassemblyHandleIndex].GetData() is not string subassemblyHandle)
        {
          return null;
        }

        return (
          corridorHandle,
          baselineGuid[1..^1].ToLower(), // guid is uppercase and enclosed in {} which need to be removed
          regionGuid[1..^1].ToLower(), // guid is uppercase and enclosed in {} which need to be removed
          assemblyHandle,
          subassemblyHandle
        );
      }
    }
    return null;
  }
}
