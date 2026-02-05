using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>
/// Processes the children of a corridor. Expects to be a singleton service.
/// </summary>
public sealed class CorridorHandler
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Point3dCollection, SOG.Polyline> _pointCollectionConverter;
  private readonly CorridorDisplayValueExtractor _displayValueExtractor;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CorridorHandler(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Point3dCollection, SOG.Polyline> pointCollectionConverter,
    CorridorDisplayValueExtractor displayValueExtractor,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _pointCollectionConverter = pointCollectionConverter;
    _displayValueExtractor = displayValueExtractor;
    _settingsStore = settingsStore;
  }

  // Ok, this is going to be very complicated.
  // We are building a nested `Base.elements` of corridor subelements in this hierarchy: corridor -> baselines -> baseline regions -> assembly -> subassemblies.
  // Corridors will also have a dict of applied assemblies -> applied subassemblies attached to the region.
  // This handler is in place because none of the corridor children inherit from CDB.Entity
  public List<Base> GetCorridorChildren(CDB.Corridor corridor)
  {
    // extract corridor solids for display value first: this will be used later to attach display values to subassemblies.
    _displayValueExtractor.ProcessCorridorSolids(corridor);

    // track children hierarchy ids:
    string corridorHandle = corridor.Handle.ToString();

    // process baselines and any featurelines found
    List<Base> baselines = new(corridor.Baselines.Count);
    foreach (CDB.Baseline baseline in corridor.Baselines)
    {
#if CIVIL3D2025_OR_GREATER
      string baselineGuid = baseline.BaselineGuid.ToString();
#else
      string baselineGuid = baseline.baselineGUID.ToString();

#endif

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
        convertedBaseline["@mainBaselineFeatureLines"] = mainFeatureLines;
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
        convertedBaseline["@offsetBaselineFeatureLines"] = mainFeatureLines;
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

              // try to get the display value mesh from the corridor display value extractor by subassembly key
              SubassemblyCorridorKey subassemblyKey =
                new(corridorHandle, baselineGuid, regionGuid, assemblyHandle, subassemblyHandle);

              if (
                _displayValueExtractor.CorridorSolidsCache.TryGetValue(
                  subassemblyKey.ToString(),
                  out List<SOG.Mesh>? display
                )
              )
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
              ["subassemblies"] = subassemblies,
              applicationId = assembly.GetSpeckleApplicationId()
            };

          convertedRegion["assembly"] = convertedAssembly;

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
          PropertyHandler propHandler = new();
          propHandler.TryAddToDictionary(
            appliedAssemblyDict,
            "adjustedElevation",
            () => appliedAssembly.AdjustedElevation
          ); // can throw

          // get the applied assembly's applied subassemblies
          Dictionary<string, object?> appliedSubassemblies = new();
          foreach (CDB.AppliedSubassembly appliedSubassembly in appliedAssembly.GetAppliedSubassemblies())
          {
            string subassemblyId = appliedSubassembly.SubassemblyId.GetSpeckleApplicationId();
            string name = subassemblyNameCache.TryGetValue(appliedSubassembly.SubassemblyId, out string? cachedName)
              ? cachedName!
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
}
