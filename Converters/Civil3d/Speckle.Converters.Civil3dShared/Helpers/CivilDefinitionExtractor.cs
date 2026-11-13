using System.Globalization;
using System.Text.Json;
using Speckle.Converters.Civil3dShared.Extensions;

namespace Speckle.Converters.Civil3dShared.Helpers;

/// <summary>Versioned native definitions, distinct from tessellated display and from received source metadata.</summary>
public static class CivilDefinitionExtractor
{
  private static string? Id(ADB.ObjectId id) => id.IsNull ? null : id.Handle.Value.ToString(CultureInfo.InvariantCulture);
  private static double[] Point(AG.Point3d p) => new[] { p.X, p.Y, p.Z };
  private static double[] Point(AG.Point2d p) => new[] { p.X, p.Y };

  public static string? Extract(CDB.Entity entity, Action<string>? trace = null)
  {
    trace?.Invoke(entity.GetType().Name);
    object? definition = entity switch
    {
      CDB.TinSurface surface => new
      {
        triangles = surface.GetTriangles(true).Select(t => new
        {
          visible = t.IsVisible,
          vertices = new[] { Point(t.Vertex1.Location), Point(t.Vertex2.Location), Point(t.Vertex3.Location) },
        }).ToArray(),
        boundaries = Enumerable.Range(0, surface.BoundariesDefinition.Count).Select(i => surface.BoundariesDefinition[i]).Select(operation => new
        {
          name = operation.Name, type = operation.BoundaryType.ToString(), trimmed = operation.IsTrimmed,
          midOrdinateDistance = operation.MidOrdinateDistance,
          loops = operation.Cast<CDB.SurfaceBoundary>().Select(b => b.Vertices.Cast<AG.Point3d>().Select(Point).ToArray()).ToArray(),
        }).ToArray(),
      },
      CDB.Profile profile => Profile(profile, trace),
      CDB.Alignment alignment => Alignment(alignment),
      CDB.Section section => new
      {
        sampleLineId = Id(section.SampleLineId), sourceId = Id(section.SourceId),
        station = section.Station, sourceType = section.SourceType.ToString(),
        points = section.SectionPoints.Cast<CDB.SectionPoint>().Select(p => new { location = Point(p.Location), segmentTo = p.SegmentTo }).ToArray(),
      },
      CDB.SampleLine line => new
      {
        groupId = Id(line.GroupId), station = line.Station, lockToStation = line.LockToStation,
        vertices = line.Vertices.Cast<CDB.SampleLineVertex>().Select(v => Point(v.Location)).ToArray(),
        sections = line.GetSectionIds().Cast<ADB.ObjectId>().Select(Id).ToArray(),
      },
      CDB.SampleLineGroup group => SampleLineGroup(group, trace),
      CDB.SectionView view => new
      {
        sampleLineId = Id(view.SampleLineId), offsetLeft = view.OffsetLeft, offsetRight = view.OffsetRight,
        elevationMin = view.ElevationMin, elevationMax = view.ElevationMax,
      },
      CDB.Corridor corridor => Corridor(corridor),
      CDB.Assembly assembly => new
      {
        type = assembly.Type.ToString(), location = Point(assembly.Location),
        groups = assembly.Groups.Cast<CDB.AssemblyGroup>().Select(g => new
        {
          name = g.Name, subassemblies = g.GetSubassemblyIds().Cast<ADB.ObjectId>().Select(Id).ToArray(),
        }).ToArray(),
      },
      CDB.Subassembly sa => new
      {
        origin = Point(sa.Origin),
        assemblyId = sa.HasParentAssembly ? Id(sa.AssemblyId) : null,
        hookSubassemblyId = sa.HasParentAssembly ? Id(sa.SubassemblyHookTo) : null,
        hookPointIndex = sa.HasParentAssembly ? (int?)sa.PointIndexHookTo : null,
        doubles = sa.ParamsDouble.Cast<Autodesk.Civil.Runtime.ParamDouble>().ToDictionary(p => p.Key, p => p.Value),
        integers = sa.ParamsLong.Cast<Autodesk.Civil.Runtime.ParamLong>().ToDictionary(p => p.Key, p => p.Value),
        strings = sa.ParamsString.Cast<Autodesk.Civil.Runtime.ParamString>().ToDictionary(p => p.Key, p => p.Value),
        booleans = sa.ParamsBool.Cast<Autodesk.Civil.Runtime.ParamBool>().ToDictionary(p => p.Key, p => p.Value),
      },
      _ => null,
    };
    return definition is null ? null : JsonSerializer.Serialize(new
    {
      schema = "speckle.civil3d.definition/1", type = entity.GetType().Name,
      applicationId = entity.GetSpeckleApplicationId(),
      coordinateSpace = entity is CDB.Section ? "section-offset-elevation" : "drawing-wcs",
      units = entity.Database.Insunits.ToString(), definition,
    });
  }

  private static T Read<T>(Action<string>? trace, string field, Func<T> read)
  {
    trace?.Invoke(field);
    return read();
  }

  private static object SampleLineGroup(CDB.SampleLineGroup group, Action<string>? trace) => new
  {
    alignmentId = Read(trace, "SampleLineGroup.ParentAlignmentId", () => Id(group.ParentAlignmentId)),
    sampleLines = Read(trace, "SampleLineGroup.GetSampleLineIds", () => group.GetSampleLineIds().Cast<ADB.ObjectId>().Select(Id).ToArray()),
    // GetSectionSources updates the group and REQUIRES ForWrite. Export must not call it.
    // Existing sampled sources are retained on each Section through its sourceId/sourceType.
    sourceDiscovery = "existing-sections-only; available-unsampled-sources-not-enumerated",
  };

  private static object Profile(CDB.Profile profile, Action<string>? trace)
  {
    var type = Read(trace, "Profile.ProfileType", () => profile.ProfileType);
    bool hasSource = type is CDB.ProfileType.EG or CDB.ProfileType.Superimposed or CDB.ProfileType.CorridorFeature;
    return new
    {
    alignmentId = Read(trace, "Profile.AlignmentId", () => Id(profile.AlignmentId)),
    sourceId = hasSource ? Read(trace, "Profile.DataSourceId", () => Id(profile.DataSourceId)) : null,
    sourceIdStatus = hasSource ? "available" : "not-applicable-to-profile-type",
    profileType = type.ToString(), offset = Read(trace, "Profile.Offset", () => profile.Offset),
    startStation = Read(trace, "Profile.StartingStation", () => profile.StartingStation),
    endStation = Read(trace, "Profile.EndingStation", () => profile.EndingStation),
    pvis = Read(trace, "Profile.PVIs", () => profile.PVIs.Cast<CDB.ProfilePVI>().Select(p => new
    {
      station = p.RawStation, elevation = p.Elevation, curveType = p.PVIType.ToString(),
      // Endpoint and tangent PVIs have no vertical entity. Do not dereference the native wrapper.
      curveLength = p.PVIType is CDB.ProfileEntityType.None or CDB.ProfileEntityType.Tangent ? 0 : p.VerticalCurve.Length,
    }).ToArray()),
    entities = Read(trace, "Profile.Entities", () => profile.Entities.Cast<CDB.ProfileEntity>().Select(e => new
    {
      type = e.EntityType.ToString(), startStation = e.StartStation, endStation = e.EndStation,
      startElevation = e.StartElevation, endElevation = e.EndElevation, length = e.Length,
    }).ToArray()),
    };
  }

  private static object Alignment(CDB.Alignment alignment)
  {
    var elements = new List<object>();
    for (int i = 0; i < alignment.Entities.Count; i++)
    {
      var entity = alignment.Entities.GetEntityByOrder(i);
      for (int j = 0; j < entity.SubEntityCount; j++)
      {
        var part = entity[j];
        double x = 0, y = 0;
        alignment.PointLocation((part.StartStation + part.EndStation) / 2, 0, ref x, ref y);
        elements.Add(new
        {
          type = part.SubEntityType.ToString(), start = Point(part.StartPoint), end = Point(part.EndPoint),
          mid = new[] { x, y }, startStation = part.StartStation, endStation = part.EndStation,
          length = part.Length, radius = part is CDB.AlignmentSubEntityArc arc ? (double?)arc.Radius : null,
          clockwise = part is CDB.AlignmentSubEntityArc curve ? (bool?)curve.Clockwise : null,
        });
      }
    }
    return new { startStation = alignment.StartingStation, endStation = alignment.EndingStation, elements };
  }

  private static object Corridor(CDB.Corridor corridor) => new
  {
    baselines = corridor.Baselines.Cast<CDB.Baseline>().Select(b => new
    {
      name = b.Name, alignmentId = b.IsFeatureLineBased() ? null : Id(b.AlignmentId),
      profileId = b.IsFeatureLineBased() ? null : Id(b.ProfileId),
      featureLineBased = b.IsFeatureLineBased(),
      regions = b.BaselineRegions.Cast<CDB.BaselineRegion>().Select(r => new
      {
        name = r.Name, assemblyId = Id(r.AssemblyId), startStation = r.StartStation, endStation = r.EndStation,
        stations = r.SortedStations(),
        frequencyTangents = r.AppliedAssemblySetting.FrequencyAlongTangents,
        frequencyCurves = r.AppliedAssemblySetting.FrequencyAlongCurves,
        frequencySpirals = r.AppliedAssemblySetting.FrequencyAlongSpirals,
        targets = r.GetTargets().Cast<CDB.SubassemblyTargetInfo>().Select(t => new
        {
          name = t.LogicalName, subassembly = t.SubassemblyName, type = t.TargetType.ToString(),
          ids = t.TargetIds.Cast<ADB.ObjectId>().Select(Id).ToArray(),
        }).ToArray(),
      }).ToArray(),
    }).ToArray(),
  };
}
