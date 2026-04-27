using System.Diagnostics.CodeAnalysis;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>
/// Extracts and stores the display value meshes of a Corridor by the corridor's subassembly corridor keys.
/// </summary>
[SuppressMessage(
  "Style",
  "IDE0052:Remove unread private members",
  Justification = "This class is a bit messy because of host app versioning, acceptible techdebt"
)]
[SuppressMessage(
  "Style",
  "IDE0051:Remove unused private members",
  Justification = "This class is a bit messy because of host app versioning, acceptible techdebt"
)]
public sealed class CorridorDisplayValueExtractor
{
  /// <summary>
  /// Keeps track of all corridor solids by their hierarchy of (corridor, baseline, region, applied assembly, applied subassembly) in the current send operation.
  /// This should be added to the display value of the corridor applied subassemblies after they are processed
  /// Handles should be used instead of Handle.Value (as is typically used for speckle app ids) since the exported solid property sets only stores the handle
  /// </summary>
  public Dictionary<string, List<SOG.Mesh>> CorridorSolidsCache { get; } = new();

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
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CorridorDisplayValueExtractor(
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    ITypedConverter<ADB.Body, SOG.Mesh> bodyConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _solidConverter = solidConverter;
    _bodyConverter = bodyConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Extracts the solids from a corridor and stores them in <see cref="CorridorSolidsCache"/> by their subassembly corridor id.
  /// Api method is only available for 2024 or greater.
  /// </summary>
  /// <param name="corridor"></param>
  /// <remarks>This is pretty complicated because we need to match each extracted solid to a corridor subassembly by inspecting its property sets for identifying information</remarks>
  public void ProcessCorridorSolids(CDB.Corridor corridor)
  {
#if CIVIL3D2024_OR_GREATER

    CDB.ExportCorridorSolidsParams param = new();

    using (var tr = _settingsStore.Current.Document.Database.TransactionManager.StartTransaction())
    {
      foreach (ADB.ObjectId solidId in corridor.ExportSolids(param, corridor.Database))
      {
        if (solidId.IsNull) // unclear why this happens
        {
          continue;
        }

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

        // get the subassembly corridor key from the solid property sets
        if (GetSubassemblyKeyFromDBObject(solid, tr) is SubassemblyCorridorKey solidKey)
        {
          if (CorridorSolidsCache.TryGetValue(solidKey.ToString(), out List<SOG.Mesh>? display))
          {
            display.Add(mesh);
          }
          else
          {
            CorridorSolidsCache[solidKey.ToString()] = new() { mesh };
          }
        }
      }

      tr.Commit();
    }
#endif
  }

  private SubassemblyCorridorKey? GetSubassemblyKeyFromDBObject(ADB.DBObject obj, ADB.Transaction tr)
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

      string? propSetName;
      try
      {
        propSetName = propertySet.PropertySetDefinitionName;
      }
      catch (Autodesk.AutoCAD.Runtime.Exception)
      {
        continue; // Skip to next property set
      }

      if (propSetName == "Corridor Identity")
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

        return new SubassemblyCorridorKey(corridorHandle, baselineGuid, regionGuid, assemblyHandle, subassemblyHandle);
      }
    }

    return null;
  }
}
