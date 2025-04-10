using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a render material base object and its converted speckle equivalent.
/// </summary>
public class SpeckleMaterialWrapper : Base
{
  public required SpeckleRenderMaterial Base { get; set; }

  public required Material RhinoMaterial { get; set; }

  public override string ToString() => $"Speckle Wrapper [{typeof(Rhino.Render.RenderMaterial)}]";

  /// <summary>
  /// Creates the material in the document
  /// </summary>
  /// <param name="doc"></param>
  /// <param name="name"></param>
  /// <returns>The index of the created material in the material table</returns>
  public int Bake(RhinoDoc doc, string name)
  {
    // first set the material name to be the nickname of this param
    Material bakeMaterial = new();
    bakeMaterial.CopyFrom(RhinoMaterial);
    bakeMaterial.Name = name;

    return doc.Materials.Add(bakeMaterial);
  }
}

public partial class SpeckleMaterialWrapperGoo : GH_Goo<SpeckleMaterialWrapper>, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Material Goo [{Value.Base.name}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle render material wrapper";
  public override string TypeDescription => "A wrapper around speckle render materials.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleMaterialWrapper speckleGrasshopperMaterial:
        Value = speckleGrasshopperMaterial;
        return true;
      case GH_Goo<SpeckleMaterialWrapper> speckleGrasshopperMaterialGoo:
        Value = speckleGrasshopperMaterialGoo.Value;
        return true;
      case GH_Material materialGoo:
        Value = new()
        {
          Base = ToSpeckleRenderMaterial(materialGoo.Value),
          RhinoMaterial = ToRhinoMaterial(materialGoo.Value)
        };
        return true;
      case Material material:
        Value = new() { Base = ToSpeckleRenderMaterial(material), RhinoMaterial = material };
        return true;
    }

    return CastFromModelRenderMaterial(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelRenderMaterial(object _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(GH_Material))
    {
      target = (T)(object)(new GH_Material() { Value = new(Value.RhinoMaterial) });
      return true;
    }

    return false;
  }

  public SpeckleMaterialWrapperGoo(SpeckleMaterialWrapper value)
  {
    Value = value;
  }

  public SpeckleMaterialWrapperGoo() { }

  private SpeckleRenderMaterial ToSpeckleRenderMaterial(Rhino.Display.DisplayMaterial mat)
  {
    SpeckleRenderMaterial speckleRenderMaterial =
      new()
      {
        name = "",
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

  private SpeckleRenderMaterial ToSpeckleRenderMaterial(Rhino.Render.RenderMaterial mat)
  {
    Rhino.DocObjects.PhysicallyBasedMaterial pbRenderMaterial = mat.ConvertToPhysicallyBased(
      RenderTexture.TextureGeneration.Allow
    );

    // get opacity
    // POC: pbr will return opacity = 0 for these because they are not pbr materials, they are transparent materials with IOR. Currently hardcoding 0.2 value in lieu of proper type support in rhino.
    double opacity = (mat.SmellsLikeGem || mat.SmellsLikeGlass) ? 0.2 : pbRenderMaterial.Opacity;

    string renderMaterialName = mat.Name ?? "default"; // default rhino material has no name
    Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    Color emissive = mat.TypeName.Equals("Emission")
      ? pbRenderMaterial.Material.EmissionColor
      : pbRenderMaterial.Emission.AsSystemColor(); // pbRenderMaterial.emission gives wrong color for emission materials, and material.emissioncolor gives the wrong value for most others *shrug*

    SpeckleRenderMaterial speckleRenderMaterial =
      new()
      {
        name = renderMaterialName,
        opacity = opacity,
        metalness = pbRenderMaterial.Metallic,
        roughness = pbRenderMaterial.Roughness,
        diffuse = diffuse.ToArgb(),
        emissive = emissive.ToArgb(),
        applicationId = mat.Id.ToString(),
        ["typeName"] = mat.TypeName,
        ["ior"] = pbRenderMaterial.Material.IndexOfRefraction,
        ["shine"] = pbRenderMaterial.Material.Shine,
      };

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
}

public class SpeckleMaterialParam : GH_Param<SpeckleMaterialWrapperGoo>, IGH_BakeAwareData
{
  public SpeckleMaterialParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleMaterialParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleMaterialParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleMaterialParam(GH_ParamAccess access)
    : base(
      "Speckle Material",
      "SM",
      "Represents a Speckle material",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("1A08CF79-2072-4B14-9430-E4465FF0C0FE");

  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("SM");

  public bool IsBakeCapable =>
    // False if no data
    !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> _)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleMaterialWrapperGoo goo)
      {
        int bakeIndex = goo.Value.Bake(doc, NickName);

        if (bakeIndex == -1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to add material {NickName} to document.");
        }
      }
    }
  }

  public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
  {
    obj_guid = Guid.Empty;
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleMaterialWrapperGoo goo)
      {
        int bakeIndex = goo.Value.Bake(doc, NickName);
        if (bakeIndex == -1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to add material {NickName} to document.");
          return false;
        }

        obj_guid = doc.Materials[bakeIndex].Id;
        return true;
      }
    }

    return false;
  }
}
