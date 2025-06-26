using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
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

  public override string ToString() => $"Speckle Material Wrapper [{typeof(Material)}]";

  public override IGH_Goo CreateGoo() => new SpeckleMaterialWrapperGoo(this);

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
