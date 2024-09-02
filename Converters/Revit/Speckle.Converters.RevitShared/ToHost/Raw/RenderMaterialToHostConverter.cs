using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;

namespace Speckle.Converters.RevitShared.ToHost.Raw;

public class RenderMaterialToHostConverter : ITypedConverter<RenderMaterial, DB.Material>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;

  public RenderMaterialToHostConverter(ISettingsStore<RevitConversionSettings> settings)
  {
    _settings = settings;
  }

  public DB.Material Convert(RenderMaterial target)
  {
    string matName = RemoveProhibitedCharacters(target.name);

    using FilteredElementCollector collector = new(_settings.Current.Document);

    // Try and find an existing material
    var existing = collector
      .OfClass(typeof(DB.Material))
      .Cast<DB.Material>()
      .FirstOrDefault(m => string.Equals(m.Name, matName, StringComparison.CurrentCultureIgnoreCase));

    if (existing != null)
    {
      return existing;
    }

    // Create new material
    ElementId materialId = DB.Material.Create(_settings.Current.Document, matName ?? Guid.NewGuid().ToString());
    DB.Material mat = (DB.Material)_settings.Current.Document.GetElement(materialId);

    var sysColor = System.Drawing.Color.FromArgb(target.diffuse);
    mat.Color = new DB.Color(sysColor.R, sysColor.G, sysColor.B);
    mat.Transparency = (int)((1d - target.opacity) * 100d);

    return mat;
  }

  private string RemoveProhibitedCharacters(string s)
  {
    if (string.IsNullOrEmpty(s))
    {
      return s;
    }

    return Regex.Replace(s, "[\\[\\]{}|;<>?`~]", "");
  }
}
