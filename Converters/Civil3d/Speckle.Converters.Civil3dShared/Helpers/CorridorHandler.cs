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
  /// </summary>
  public Dictionary<(string, string, string, string, string), SOG.Mesh> CorridorSolidsCache { get; } = new();

  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _solidConverter;
  private readonly PropertySetExtractor _propertySetExtractor;
  private readonly ITypedConverter<CDB.Baseline, Base> _baselineConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CorridorHandler(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    PropertySetExtractor propertySetExtractor,
    ITypedConverter<CDB.Baseline, Base> baselineConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _solidConverter = solidConverter;
    _propertySetExtractor = propertySetExtractor;
    _baselineConverter = baselineConverter;
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
    string corridorId = corridor.GetSpeckleApplicationId();

    // process baselines
    List<Base> baselines = new(corridor.Baselines.Count);
    foreach (CDB.Baseline baseline in corridor.Baselines)
    {
      string baselineId = baseline.baselineGUID.ToString();

      Base convertedBaseline =
        new()
        {
          ["type"] = baseline.GetType().ToString().Split('.').Last(),
          ["name"] = baseline.Name,
          ["startStation"] = baseline.StartStation,
          ["endStation"] = baseline.EndStation,
          ["units"] = _settingsStore.Current.SpeckleUnits,
          ["applicationId"] = baselineId,
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
        string regionId = region.RegionGUID.ToString();

        Base convertedRegion =
          new()
          {
            ["type"] = region.GetType().ToString().Split('.').Last(),
            ["name"] = region.Name,
            ["startStation"] = region.StartStation,
            ["endStation"] = region.EndStation,
            ["assemblyId"] = region.AssemblyId.GetSpeckleApplicationId(),
            ["units"] = _settingsStore.Current.SpeckleUnits,
            ["applicationId"] = regionId,
          };

        // get the region applied assemblies
        List<Base> appliedAssemblies = new();
        double[] sortedStations = region.SortedStations();
        for (int i = 0; i < sortedStations.Length; i++)
        {
          double station = sortedStations[i];

          CDB.AppliedAssembly appliedAssembly = region.AppliedAssemblies[i];
          string assemblyId = appliedAssembly.AssemblyId.GetSpeckleApplicationId();
          Base convertedAppliedAssembly =
            new()
            {
              ["type"] = appliedAssembly.GetType().ToString().Split('.').Last(),
              ["assemblyId"] = assemblyId,
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
            string subassemblyId = appliedSubassembly.SubassemblyId.GetSpeckleApplicationId();

            Base convertedAppliedSubassembly =
              new()
              {
                ["type"] = appliedSubassembly.GetType().ToString().Split('.').Last(),
                ["subassemblyId"] = subassemblyId,
                ["units"] = _settingsStore.Current.SpeckleUnits
              };

            // try to get the display value mesh
            (string, string, string, string, string) corridorSolidsKey = (
              corridorId,
              baselineId,
              regionId,
              assemblyId,
              subassemblyId
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
    List<SOG.Mesh> result = new();
    CDB.ExportCorridorSolidsParams param = new();

    using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      foreach (ADB.ObjectId solidId in corridor.ExportSolids(param, corridor.Database))
      {
        var solid = (ADB.Solid3d)tr.GetObject(solidId, ADB.OpenMode.ForRead);

        // get the solid mesh
        SOG.Mesh mesh = _solidConverter.Convert(solid);

        // get the (corridor id, baseline id, region id, assembly id, subassembly id) of the solid property sets
        Dictionary<string, object?>? propertySets = _propertySetExtractor.GetPropertySets(solid, false);

        if (propertySets is null)
        {
          continue;
        }

        // TODO: add mesh to cahce depending on property set values!!

        result.Add(mesh);
      }

      tr.Commit();
    }
  }
}
