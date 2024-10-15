using Speckle.Converters.Civil3dShared.Extensions;
using Speckle.Converters.Civil3dShared.ToSpeckle;
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
  public Dictionary<(string, string, string, string, string), SOG.Mesh> CorridorSolidsCache { get; } = new();

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
  private readonly PropertySetExtractor _propertySetExtractor;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CorridorHandler(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    PropertySetExtractor propertySetExtractor,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _solidConverter = solidConverter;
    _propertySetExtractor = propertySetExtractor;
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

    // process baselines
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

      // get the baseline regions
      List<Base> regions = new();
      foreach (CDB.BaselineRegion region in baseline.BaselineRegions)
      {
        string regionGuid = region.RegionGUID.ToString();

        Base convertedRegion =
          new()
          {
            ["type"] = region.GetType().ToString().Split('.').Last(),
            ["name"] = region.Name,
            ["startStation"] = region.StartStation,
            ["endStation"] = region.EndStation,
            ["assemblyId"] = region.AssemblyId.GetSpeckleApplicationId(),
            ["units"] = _settingsStore.Current.SpeckleUnits,
            ["applicationId"] = regionGuid,
          };

        // get the region applied assemblies
        List<Base> appliedAssemblies = new();
        double[] sortedStations = region.SortedStations();
        for (int i = 0; i < sortedStations.Length; i++)
        {
          double station = sortedStations[i];

          CDB.AppliedAssembly appliedAssembly = region.AppliedAssemblies[i];
          string assemblyHandle = appliedAssembly.AssemblyId.Handle.ToString();
          Base convertedAppliedAssembly =
            new()
            {
              ["type"] = appliedAssembly.GetType().ToString().Split('.').Last(),
              ["assemblyId"] = appliedAssembly.AssemblyId.GetSpeckleApplicationId(),
              ["station"] = station,
              ["units"] = _settingsStore.Current.SpeckleUnits
            };

          try
          {
            convertedAppliedAssembly["adjustedElevation"] = appliedAssembly.AdjustedElevation;
          }
          catch (ArgumentException e) when (!e.IsFatal())
          {
            // Do nothing. Leave the value as null. Not sure why accessing adjusted elevation sometimes throws.
          }

          // get the applied assembly's applied subassemblies
          List<Base> appliedSubassemblies = new(appliedAssembly.GetAppliedSubassemblies().Count);

          foreach (CDB.AppliedSubassembly appliedSubassembly in appliedAssembly.GetAppliedSubassemblies())
          {
            string subassemblyHandle = appliedSubassembly.SubassemblyId.Handle.ToString();

            Base convertedAppliedSubassembly =
              new()
              {
                ["type"] = appliedSubassembly.GetType().ToString().Split('.').Last(),
                ["subassemblyId"] = appliedSubassembly.SubassemblyId.GetSpeckleApplicationId(),
                ["units"] = _settingsStore.Current.SpeckleUnits
              };

            // try to get the display value mesh
            (string, string, string, string, string) corridorSolidsKey = (
              corridorHandle,
              baselineGuid,
              regionGuid,
              assemblyHandle,
              subassemblyHandle
            );

            if (CorridorSolidsCache.TryGetValue(corridorSolidsKey, out SOG.Mesh display))
            {
              convertedAppliedSubassembly["displayValue"] = new List<SOG.Mesh> { display };
            }

            // TODO: get the applied subassembly's calculated stuff
            appliedSubassemblies.Add(convertedAppliedSubassembly);
          }

          convertedAppliedAssembly["@elements"] = appliedSubassemblies;
          appliedAssemblies.Add(convertedAppliedAssembly);
        }

        convertedRegion["@elements"] = appliedAssemblies;
        regions.Add(convertedRegion);
      }

      convertedBaseline["@elements"] = regions;
      baselines.Add(convertedBaseline);
    }

    return baselines;
  }

  /// <summary>
  /// Extracts the solids from a corridor and stores in <see cref="CorridorSolidsCache"/> according to property sets on the solid.
  /// </summary>
  /// <param name="corridor"></param>
  /// <returns></returns>
  private void HandleCorridorSolids(CDB.Corridor corridor)
  {
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
        else if (solid is ADB.Body)
        {
          // can't do much with the body - skipping for now
          continue;
        }

        if (mesh is null)
        {
          continue;
        }

        // get the (corridor handle, baseline guid, region guid, assembly handle, subassembly handle) of the solid property sets
        (string corridor, string baseline, string region, string assembly, string subassembly)? solidKey =
          GetCorridorSolidIdFromPropertySet(solid, tr);

        if (solidKey is (string, string, string, string, string) validSolidKey)
        {
          CorridorSolidsCache[validSolidKey] = mesh;
        }
      }

      tr.Commit();
    }
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
        AAECPDB.PropertySetData corridorData = propertySet.PropertySetData[_corridorHandleIndex];
        AAECPDB.PropertySetData baselineData = propertySet.PropertySetData[_baselineGuidIndex];
        AAECPDB.PropertySetData regionData = propertySet.PropertySetData[_regionGuidIndex];
        AAECPDB.PropertySetData assemblyData = propertySet.PropertySetData[_assemblyHandleIndex];
        AAECPDB.PropertySetData subassemblyData = propertySet.PropertySetData[_subassemblyHandleIndex];

        return
          corridorData.GetData() is not string corridorHandle
          || baselineData.GetData() is not string baselineGuid // these will enclose the guid in {} which need to be removed
          || regionData.GetData() is not string regionGuid // these will enclose the guid in {} which need to be removed
          || assemblyData.GetData() is not string assemblyHandle
          || subassemblyData.GetData() is not string subassemblyHandle
          ? null
          : (corridorHandle, baselineGuid[1..^1], regionGuid[1..^1], assemblyHandle, subassemblyHandle);
      }
    }
    return null;
  }
}
