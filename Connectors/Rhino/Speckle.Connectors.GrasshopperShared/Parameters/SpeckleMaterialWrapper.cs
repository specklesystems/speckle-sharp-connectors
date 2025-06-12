using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Models;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a render material base object and its converted speckle equivalent.
/// </summary>
public class SpeckleMaterialWrapper : SpeckleWrapper
{
  public override required Base Base
  {
    get => Material;
    set
    {
      if (value is not SpeckleRenderMaterial mat)
      {
        throw new ArgumentException("Cannot create material wrapper from a non-SpeckleRenderMaterial Base");
      }

      Material = mat;
    }
  }

  public SpeckleRenderMaterial Material { get; set; }

  public required Material RhinoMaterial { get; set; }

  // The guid of the rhino render material that corresponds to the rhino material, if it exists.
  public required Guid RhinoRenderMaterialId { get; set; }

  public override string ToString() => $"Speckle Wrapper [{typeof(Material)}]";

  /// <summary>
  /// Creates the material in the document
  /// </summary>
  /// <param name="doc"></param>
  /// <param name="name">The name override, if any. Used for param baking where the nickname is changed by the user, or no name is available.</param>
  /// <returns>The index of the created material in the material table</returns>
  public int Bake(RhinoDoc doc, string? name = null)
  {
    Material bakeMaterial = new();
    bakeMaterial.CopyFrom(RhinoMaterial);

    // set the material name
    // this should be the given name in the rhino material *unless* an override name is passed in
    if (name != null)
    {
      bakeMaterial.Name = name;
    }

    return doc.Materials.Add(bakeMaterial);
  }
}

public partial class SpeckleMaterialWrapperGoo : GH_Goo<SpeckleMaterialWrapper>
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Material Goo [{Value.Name}]";

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

public class SpeckleMaterialParam : GH_Param<SpeckleMaterialWrapperGoo>, IGH_BakeAwareObject
{
  private const string NICKNAME = "Speckle Material";

  public SpeckleMaterialParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleMaterialParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleMaterialParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleMaterialParam(GH_ParamAccess access)
    : base(
      NICKNAME,
      "SM",
      "Represents a Speckle material",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("1A08CF79-2072-4B14-9430-E4465FF0C0FE");

  protected override Bitmap Icon => Resources.speckle_param_material;

  bool IGH_BakeAwareObject.IsBakeCapable => // False if no data
    !VolatileData.IsEmpty;

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleMaterialWrapperGoo goo)
      {
        // get the param nickname if it is a custom name.
        // this is used to override the name of the material.
        // the nickname should also be used in case of an empty name on the rhino material
        string? name =
          NickName != NICKNAME
            ? NickName
            : string.IsNullOrEmpty(goo.Value.Name)
              ? NickName
              : null;

        int bakeIndex = goo.Value.Bake(doc, name);

        if (bakeIndex == -1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to add material {name} to document.");
        }
      }
    }
  }

  void IGH_BakeAwareObject.BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleMaterialWrapperGoo goo)
      {
        // get the param nickname if it is a custom name.
        // this is used to override the name of the material.
        // the nickname should also be used in case of an empty name on the rhino material
        string? name =
          NickName != NICKNAME
            ? NickName
            : string.IsNullOrEmpty(goo.Value.Name)
              ? NickName
              : null;

        int bakeIndex = goo.Value.Bake(doc, name);
        if (bakeIndex == -1)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to add material {name} to document.");
        }

        obj_ids.Add(doc.Materials[bakeIndex].Id);
      }
    }
  }
}
