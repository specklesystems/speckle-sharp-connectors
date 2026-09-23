using Speckle.Common.StructuralExtrusion;
using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>The unit prism for a frame section, or the reason none could be built (the CSi shape name, qualified).</summary>
public sealed record FrameSectionPrism(PrismTemplate? Template, string ShapeKey);

/// <summary>
/// Resolves a frame section to a cached unit prism through the typed <c>PropFrame</c> getters (ENG-9048, ADR-0002).
/// Every result, including "unsupported", is cached per send so each section costs one round of API calls.
/// </summary>
public sealed class FrameSectionProfileResolver
{
  // Root radii and fillets are not modelled, so the catalog area sits a little under the host's.
  public const double AREA_TOLERANCE = 0.05;

  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly CsiToSpeckleCacheSingleton _cache;

  public FrameSectionProfileResolver(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    CsiToSpeckleCacheSingleton cache
  )
  {
    _settingsStore = settingsStore;
    _cache = cache;
  }

  public FrameSectionPrism Resolve(string sectionName)
  {
    if (_cache.FramePrismCache.TryGetValue(sectionName, out var cached))
    {
      return cached;
    }

    var resolved = ResolveUncached(sectionName);
    _cache.FramePrismCache[sectionName] = resolved;
    return resolved;
  }

  private FrameSectionPrism ResolveUncached(string sectionName)
  {
    var propFrame = _settingsStore.Current.SapModel.PropFrame;
    eFramePropType type = 0;
    if (propFrame.GetTypeOAPI(sectionName, ref type) != 0)
    {
      return new(null, "unknown-type");
    }

    string shape = type.ToString();
    var profile = ReadProfile(propFrame, sectionName, type);
    if (profile is null)
    {
      return new(null, shape);
    }

    var outline = SectionProfileCatalog.TryBuild(profile);
    if (outline is null)
    {
      return new(null, $"{shape}/invalid-dimensions");
    }

    double hostArea = GetHostArea(sectionName);
    if (double.IsNaN(hostArea) || hostArea <= 0)
    {
      return new(null, $"{shape}/no-host-area");
    }
    if (Math.Abs(outline.Area - hostArea) / hostArea > AREA_TOLERANCE)
    {
      return new(null, $"{shape}/area-mismatch");
    }

    var prism = PrismBuilder.TryBuildUnitPrism(outline);
    return prism is null ? new(null, $"{shape}/tessellation-failed") : new(prism, shape);
  }

  private static SectionProfile? ReadProfile(cPropFrame propFrame, string name, eFramePropType type)
  {
    string fileName = string.Empty,
      material = string.Empty,
      notes = string.Empty,
      guid = string.Empty;
    int color = 0;
    double t3 = 0,
      t2 = 0,
      tf = 0,
      tw = 0,
      t2b = 0,
      tfb = 0;

    switch (type)
    {
      case eFramePropType.Rectangular:
        return
          propFrame.GetRectangle(name, ref fileName, ref material, ref t3, ref t2, ref color, ref notes, ref guid) == 0
          ? new RectangleProfile(t3, t2)
          : null;
      case eFramePropType.Circle:
        return propFrame.GetCircle(name, ref fileName, ref material, ref t3, ref color, ref notes, ref guid) == 0
          ? new CircleProfile(t3)
          : null;
      case eFramePropType.I:
        if (
          propFrame.GetISection(
            name,
            ref fileName,
            ref material,
            ref t3,
            ref t2,
            ref tf,
            ref tw,
            ref t2b,
            ref tfb,
            ref color,
            ref notes,
            ref guid
          ) != 0
        )
        {
          return null;
        }
        // Symmetric catalogue sections may report the bottom flange as zero.
        return new ISectionProfile(t3, t2, tf, tw, t2b > 0 ? t2b : t2, tfb > 0 ? tfb : tf);
      case eFramePropType.Channel:
        return
          propFrame.GetChannel(
            name,
            ref fileName,
            ref material,
            ref t3,
            ref t2,
            ref tf,
            ref tw,
            ref color,
            ref notes,
            ref guid
          ) == 0
          ? new ChannelProfile(t3, t2, tf, tw)
          : null;
      case eFramePropType.T:
        return
          propFrame.GetTee(
            name,
            ref fileName,
            ref material,
            ref t3,
            ref t2,
            ref tf,
            ref tw,
            ref color,
            ref notes,
            ref guid
          ) == 0
          ? new TeeProfile(t3, t2, tf, tw)
          : null;
      case eFramePropType.Angle:
        return
          propFrame.GetAngle(
            name,
            ref fileName,
            ref material,
            ref t3,
            ref t2,
            ref tf,
            ref tw,
            ref color,
            ref notes,
            ref guid
          ) == 0
          ? new AngleProfile(t3, t2, tf, tw)
          : null;
      case eFramePropType.Box:
        return
          propFrame.GetTube(
            name,
            ref fileName,
            ref material,
            ref t3,
            ref t2,
            ref tf,
            ref tw,
            ref color,
            ref notes,
            ref guid
          ) == 0
          ? new RectangularHollowProfile(t3, t2, tf, tw)
          : null;
      case eFramePropType.Pipe:
        return propFrame.GetPipe(name, ref fileName, ref material, ref t3, ref tw, ref color, ref notes, ref guid) == 0
          ? new CircularHollowProfile(t3, tw)
          : null;
      default:
        return null;
    }
  }

  private double GetHostArea(string sectionName)
  {
    if (_cache.FrameSectionAreaCache.TryGetValue(sectionName, out double cached))
    {
      return cached;
    }

    double area = 0,
      as2 = 0,
      as3 = 0,
      torsion = 0,
      i22 = 0,
      i33 = 0,
      s22 = 0,
      s33 = 0,
      z22 = 0,
      z33 = 0,
      r22 = 0,
      r33 = 0;
    int result = _settingsStore.Current.SapModel.PropFrame.GetSectProps(
      sectionName,
      ref area,
      ref as2,
      ref as3,
      ref torsion,
      ref i22,
      ref i33,
      ref s22,
      ref s33,
      ref z22,
      ref z33,
      ref r22,
      ref r33
    );

    double validatedArea = result == 0 ? area : double.NaN;
    _cache.FrameSectionAreaCache[sectionName] = validatedArea;
    return validatedArea;
  }
}
