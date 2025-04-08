#if RHINO8_OR_GREATER
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Display;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public SpeckleObjectWrapperGoo(ModelObject mo)
  {
    CastFrom(mo);
  }

  private bool HandleModelObjects(object source)
  {
    if (source is ModelObject modelObject)
    {
      if (GetGeometryFromModelObject(modelObject) is GeometryBase modelGB)
      {
        var modelConverted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(modelGB);
        SpecklePropertyGroupGoo propertyGroup = new();
        propertyGroup.CastFrom(modelObject.UserText);

        // update the converted Base with props as well
        modelConverted["name"] = modelObject.Name.ToString();
        Dictionary<string, object?> propertyDict = new();
        foreach (var entry in propertyGroup.Value)
        {
          propertyDict.Add(entry.Key, entry.Value.Value);
        }
        modelConverted["properties"] = propertyDict;

        // get the object color
        ObjectDisplayColor.Value? color = modelObject.Display.Color;

        SpeckleObjectWrapper so =
          new()
          {
            GeometryBase = modelGB,
            Base = modelConverted,
            Name = modelObject.Name.ToString(),
            Color = color is null ? null : Color.FromArgb(color.Value.Color.ToArgb()),
            ColorSource = color?.Source,
            RenderMaterialName = modelObject.Render.Material?.Material?.Name,
            Properties = propertyGroup,
            applicationId = modelObject.Id?.ToString()
          };

        Value = so;
        return true;
      }
      return false;
    }

    return false;
  }

  private GeometryBase? GetGeometryFromModelObject(ModelObject modelObject) =>
    RhinoDoc.ActiveDoc.Objects.FindId(modelObject.Id ?? Guid.Empty).Geometry;
}
#endif
