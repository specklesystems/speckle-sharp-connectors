using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Settings;

namespace Speckle.Converters.MicroStation.ToSpeckle.Appearance;

/// <summary>A real MicroStation render material resolved for an element (dgnextract's Material).</summary>
public readonly record struct ResolvedMaterial(string Key, string Name, int Argb, double Opacity);

/// <summary>
/// Symbology + material resolution — the managed port of dgnextract's <c>materials_dgn.h</c>
/// (SymbologyResolver / MaterialResolver / SymbologyContext in one per-operation service).
///
/// <para><b>Colors</b> (the display-colour channel): the element's line colour resolved through
/// <see cref="DPN.ElementDisplayParameters.ResolveByLevel"/> (ByLevel → the level record's element
/// colour) and the parent-colour stack (ByCell → the containing cell header's colour), then decoded
/// index/TBGR/colour-book → RGB via <see cref="DPN.DgnColorMap.ExtractElementColorInfo"/>.</para>
///
/// <para><b>Materials</b>: only REAL MicroStation materials, resolved in MicroStation's assignment
/// priority — attached (<see cref="DPN.MaterialManager.FindMaterialAttachment"/>) first, then the
/// level/colour symbology chain (<see cref="DPN.MaterialManager.FindMaterialBySymbology"/> with
/// symbology overrides on, which internally covers level override → (level,colour) assignment →
/// ByLevel). Colours and materials are strictly separate channels (ENG-9130): a geometry gets a
/// material XOR a colour.</para>
/// </summary>
public class AppearanceResolver(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  private const uint COLOR_BYLEVEL = 0xFFFFFFFF;
  private const uint COLOR_BYCELL = 0xFFFFFFFE;

  // ByCell context: cell-children recursion pushes the header's resolved ARGB (dgnextract's
  // SymbologyContext parent-colour stack).
  private readonly List<int> _parentColors = [];

  private const int DEFAULT_WHITE_ARGB = unchecked((int)0xFFFFFFFF);

  public IDisposable PushParentColor(int argb)
  {
    _parentColors.Add(argb);
    return new PopScope(this);
  }

  /// <summary>The element's symbology colour as ARGB (always resolvable — falls back to white).</summary>
  public int ResolveColorArgb(MgdElement element)
  {
    try
    {
      if (element is not MgdElements.DisplayableElement displayable)
      {
        return ParentOrDefault();
      }

      using DPN.ElementDisplayParameters displayParams = displayable.GetElementDisplayParameters(false);
      uint raw = displayParams.LineColor;

      if (raw == COLOR_BYCELL)
      {
        return ParentOrDefault();
      }
      if (raw == COLOR_BYLEVEL)
      {
        displayParams.ResolveByLevel(element.DgnModelRef);
        raw = displayParams.LineColor;
        if (raw is COLOR_BYLEVEL or COLOR_BYCELL)
        {
          return ParentOrDefault();
        }
      }

      if (displayParams.IsLineColorTBGR)
      {
        uint tbgr = displayParams.LineColorTBGR;
        return ArgbFromRgb((byte)(tbgr & 0xFF), (byte)((tbgr >> 8) & 0xFF), (byte)((tbgr >> 16) & 0xFF));
      }

      DPN.DgnFile? file = element.DgnModel?.GetDgnFile();
      if (file != null)
      {
        DPN.ColorInformation info = DPN.DgnColorMap.ExtractElementColorInfo(raw, file);
        DPN.RgbColorDef def = info.ColorDefinition;
        return ArgbFromRgb(def.R, def.G, def.B);
      }
      return ParentOrDefault();
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return ParentOrDefault();
    }
  }

  /// <summary>
  /// The element's render material in assignment priority, or null when only symbology colour
  /// applies. Curves must never call this — materials are a surface concept (CurveExtractor rule).
  /// </summary>
  public ResolvedMaterial? ResolveMaterial(MgdElement element)
  {
    try
    {
      DPN.DgnModelRef modelRef = element.DgnModelRef ?? settingsStore.Current.ActiveModel;

      // 1. Directly attached material (strongest).
      DPN.Material? material = DPN.MaterialManager.FindMaterialAttachment(
        out DPN.MaterialSearchStatus _,
        element,
        modelRef,
        true
      );

      // 2. Symbology chain: level override → (level, colour) assignment → ByLevel.
      if (material == null && element is MgdElements.DisplayableElement displayable)
      {
        using DPN.ElementDisplayParameters displayParams = displayable.GetElementDisplayParameters(false);
        displayParams.ResolveByLevel(modelRef);
        material = DPN.MaterialManager.FindMaterialBySymbology(
          out DPN.MaterialSearchStatus _,
          displayParams.Level,
          displayParams.LineColor,
          modelRef,
          true,
          true
        );
      }

      if (material == null)
      {
        return null;
      }

      string name = material.Name ?? "";
      int argb = DEFAULT_WHITE_ARGB;
      double opacity = 1.0;
      DPN.MaterialSettings? settings = material.GetSettings();
      if (settings != null)
      {
        if (settings.HasBaseColor)
        {
          BG.RgbFactor baseColor = settings.BaseColor;
          argb = ArgbFromRgb(FactorToByte(baseColor.Red), FactorToByte(baseColor.Green), FactorToByte(baseColor.Blue));
        }
        if (settings.HasTransmitIntensity)
        {
          opacity = Math.Max(0.0, Math.Min(1.0, 1.0 - settings.TransmitIntensity));
        }
      }

      return new ResolvedMaterial(Key: name.Length > 0 ? name : "unnamed-material", Name: name, argb, opacity);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return null;
    }
  }

  private int ParentOrDefault() => _parentColors.Count > 0 ? _parentColors[^1] : DEFAULT_WHITE_ARGB;

  private static byte FactorToByte(double factor) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 255.0)));

  private static int ArgbFromRgb(byte r, byte g, byte b) => unchecked((int)0xFF000000 | (r << 16) | (g << 8) | b);

  private sealed class PopScope(AppearanceResolver owner) : IDisposable
  {
    private bool _disposed;

    public void Dispose()
    {
      if (!_disposed)
      {
        _disposed = true;
        if (owner._parentColors.Count > 0)
        {
          owner._parentColors.RemoveAt(owner._parentColors.Count - 1);
        }
      }
    }
  }
}
