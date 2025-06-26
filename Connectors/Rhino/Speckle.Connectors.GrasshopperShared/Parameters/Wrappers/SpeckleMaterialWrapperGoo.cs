using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.HostApp;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleMaterialWrapperGoo : GH_Goo<SpeckleMaterialWrapper>
{
  public override bool IsValid => Value.Base is not null;
  public override string TypeName => "Speckle Material";
  public override string TypeDescription => "Represents a render material from Speckle";

  public SpeckleMaterialWrapperGoo(SpeckleMaterialWrapper value)
  {
    Value = value;
  }

  /// <summary>
  /// Empty constructor should only be used for casting
  /// </summary>
  public SpeckleMaterialWrapperGoo() { }

  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $"Speckle Material : {Value.Name}";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleMaterialWrapper sourceWrapper:
        Value = sourceWrapper;
        return true;
      case SpeckleMaterialWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value;
        return true;
      case GH_Goo<SpeckleMaterialWrapper> goo:
        Value = goo.Value;
        return true;
      case GH_Material materialGoo:
        var gooMaterial = ToRhinoMaterial(materialGoo.Value);
        Value = new()
        {
          Base = ToSpeckleRenderMaterial(materialGoo.Value),
          Name = gooMaterial.Name,
          RhinoMaterial = gooMaterial,
          RhinoRenderMaterialId = Guid.Empty,
        };
        return true;
      case Material material:
        Value = new()
        {
          Base = ToSpeckleRenderMaterial(material),
          Name = material.Name,
          RhinoMaterial = material,
          RhinoRenderMaterialId = Guid.Empty
        };
        return true;
      case SpeckleRenderMaterial speckleMaterial:
        Value = new()
        {
          Base = speckleMaterial,
          Name = speckleMaterial.name,
          RhinoMaterial = ToRhinoMaterial(speckleMaterial),
          RhinoRenderMaterialId = Guid.Empty,
          ApplicationId = speckleMaterial.applicationId,
        };
        return true;
    }

    return CastFromModelRenderMaterial(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelRenderMaterial(object _) => false;

  private bool CastToModelRenderMaterial<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(GH_Material))
    {
      target = (T)(object)(new GH_Material() { Value = new(Value.RhinoMaterial) });
      return true;
    }

    return CastToModelRenderMaterial(ref target);
  }

  private SpeckleRenderMaterial ToSpeckleRenderMaterial(Rhino.Display.DisplayMaterial mat)
  {
    SpeckleRenderMaterial speckleRenderMaterial =
      new()
      {
        name = mat.GetSpeckleApplicationId(),
        opacity = 1 - mat.Transparency,
        metalness = mat.Shine,
        diffuse = mat.Diffuse.ToArgb(),
        emissive = mat.Emission.ToArgb(),
        applicationId = mat.GetSpeckleApplicationId(),
      };

    // add additional dynamic props for rhino material receive
    speckleRenderMaterial["specular"] = mat.Specular.ToArgb();
    speckleRenderMaterial["shine"] = mat.Shine;

    return speckleRenderMaterial;
  }

  private SpeckleRenderMaterial ToSpeckleRenderMaterial(Material mat)
  {
    SpeckleRenderMaterial speckleRenderMaterial =
      new()
      {
        name = mat.Name,
        opacity = 1 - mat.Transparency,
        diffuse = mat.DiffuseColor.ToArgb(),
        emissive = mat.EmissionColor.ToArgb(),
        applicationId = mat.Name,
        ["specular"] = mat.SpecularColor.ToArgb(),
        ["shine"] = mat.AmbientColor,
        ["ior"] = mat.IndexOfRefraction
      };

    return speckleRenderMaterial;
  }

  private Material ToRhinoMaterial(Rhino.Display.DisplayMaterial mat) =>
    new()
    {
      DiffuseColor = mat.Diffuse,
      EmissionColor = mat.Emission,
      Transparency = mat.Transparency,
      SpecularColor = mat.Specular,
      Shine = mat.Shine,
    };

  private Material ToRhinoMaterial(SpeckleRenderMaterial mat) =>
    new()
    {
      Name = mat.name,
      DiffuseColor = mat.diffuseColor,
      EmissionColor = mat.emissiveColor,
      Transparency = 1 - mat.opacity,
      Shine = mat["shine"] is double shine ? shine : default,
      IndexOfRefraction = mat["ior"] is double ior ? ior : default
    };
}
