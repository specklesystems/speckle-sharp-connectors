#if RHINO8_OR_GREATER
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Display;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public SpeckleObjectWrapperGoo(ModelObject mo)
  {
    CastFrom(mo);
  }

  private bool CastFromModelObject(object source)
  {
    if (source is ModelObject modelObject)
    {
      if (GetGeometryFromModelObject(modelObject) is GeometryBase modelGB)
      {
        Base modelConverted = SpeckleConversionContext.ConvertToSpeckle(modelGB);
        SpecklePropertyGroupGoo propertyGroup = new();
        propertyGroup.CastFrom(modelObject.UserText);

        // update the converted Base with props as well
        modelConverted.applicationId = modelObject.Id?.ToString();
        modelConverted["name"] = modelObject.Name.ToString();
        Dictionary<string, object?> propertyDict = new();
        foreach (var entry in propertyGroup.Value)
        {
          propertyDict.Add(entry.Key, entry.Value.Value);
        }
        modelConverted["properties"] = propertyDict;

        // get the object color and material
        ObjectDisplayColor.Value? color = modelObject.Display.Color;
        SpeckleMaterialWrapperGoo? materialWrapper = new();
        if (GetMaterialFromModelObject(modelObject) is Rhino.Render.RenderMaterial renderMat)
        {
          materialWrapper.CastFrom(renderMat);
        }

        SpeckleObjectWrapper so =
          new()
          {
            GeometryBase = modelGB,
            Base = modelConverted,
            Name = modelObject.Name.ToString(),
            Color = color is null ? null : Color.FromArgb(color.Value.Color.ToArgb()),
            Material = materialWrapper.Value,
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

  private Rhino.Render.RenderMaterial? GetMaterialFromModelObject(ModelObject modelObject) =>
    RhinoDoc.ActiveDoc.RenderMaterials.Find(modelObject.Render.Material?.Material.Id ?? Guid.Empty);
}
#endif
