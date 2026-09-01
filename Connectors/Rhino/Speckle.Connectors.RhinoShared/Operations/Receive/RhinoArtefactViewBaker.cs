using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
using RG = Rhino.Geometry;

namespace Speckle.Connectors.Rhino.Operations.Receive;

/// <summary>
/// Recreates an artefact bundle's named camera viewpoints (<c>envelope.camera_views.parquet</c>) as Rhino named
/// views [ENG-9112].
/// </summary>
/// <remarks>
/// <para>Follows the v1 <c>RhinoViewBaker</c> mechanism — clone the active viewport, retarget its camera, push it
/// onto the viewport, add the named view from it, then restore the viewport. Going through a real viewport is what
/// guarantees a valid screen port and frustum aspect; a bare <c>new ViewInfo()</c> has neither.</para>
/// <para>Unlike the v1 baker this preserves the source projection: an artefact camera view records
/// <see cref="ArtefactCameraView.IsOrtho"/>, so a parallel view stays parallel instead of being forced to
/// perspective. Lens length is applied through <c>Camera35mmLensLength</c> rather than the
/// <c>ChangeToPerspectiveProjection</c> lens argument, which is documented to be ignored when the viewport is
/// already perspective (as a cloned active viewport usually is).</para>
/// </remarks>
internal static class RhinoArtefactViewBaker
{
  public static void BakeViews(
    RhinoDoc doc,
    IReadOnlyList<ArtefactCameraView> views,
    string docUnits,
    ArtefactSessionLog session,
    ILogger logger
  )
  {
    if (views.Count == 0)
    {
      return;
    }

    var activeView = doc.Views.ActiveView;
    if (activeView is null)
    {
      // headless / no open viewport (the importer legs): nothing to clone a valid frustum from.
      logger.LogWarning("Skipped baking {Count} artefact named view(s): no active Rhino view", views.Count);
      return;
    }

    var viewport = activeView.ActiveViewport;
    using var original = new ViewportInfo(viewport);

    foreach (var view in views)
    {
      if (view.Name is not { Length: > 0 } rawName || rawName.Trim().Length == 0)
      {
        continue; // an unnamed viewpoint cannot be a Rhino named view
      }
      var name = rawName.Trim();

      try
      {
        RemoveNamedView(doc, name); // re-receive replaces rather than duplicating
        if (Apply(viewport, view, docUnits))
        {
          doc.NamedViews.Add(name, activeView.ActiveViewportID);
          session.Increment("namedViewsBaked");
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to create artefact named view '{ViewName}'", name);
      }
    }

    // put the user's viewport back exactly as it was — it was only ever a scratch surface
    viewport.SetViewProjection(original, false);
    activeView.Redraw();
  }

  // Retargets the viewport's camera onto one artefact view. Returns false when the recorded camera frame is
  // degenerate, so the caller skips it instead of adding a broken named view.
  private static bool Apply(global::Rhino.Display.RhinoViewport viewport, ArtefactCameraView view, string docUnits)
  {
    var forward = new RG.Vector3d(view.ForwardX, view.ForwardY, view.ForwardZ);
    var up = new RG.Vector3d(view.UpX, view.UpY, view.UpZ);
    if (!forward.Unitize() || !up.Unitize())
    {
      return false;
    }

    // Positions, target and the ortho/frustum extents are in the bundle's model units; direction vectors and the
    // 35mm lens length are unitless, so they are NOT scaled.
    double scale = view.Units is { Length: > 0 } u ? Units.GetConversionFactor(u, docUnits) : 1.0;

    using var vp = new ViewportInfo(viewport);
    vp.SetCameraLocation(new RG.Point3d(view.PosX * scale, view.PosY * scale, view.PosZ * scale));
    vp.SetCameraDirection(forward);
    vp.SetCameraUp(up);

    if (view.TargetX is double tx && view.TargetY is double ty && view.TargetZ is double tz)
    {
      vp.TargetPoint = new RG.Point3d(tx * scale, ty * scale, tz * scale);
    }

    if (view.IsOrtho)
    {
      vp.ChangeToParallelProjection(true);
      ApplyOrthoExtents(vp, view, scale);
    }
    else
    {
      // targetDistance = UnsetValue → keep the current frustum plane; the lens is set explicitly below.
      vp.ChangeToPerspectiveProjection(RhinoMath.UnsetValue, true, 50.0);
      if (view.LensMm is double lens && lens > 0)
      {
        vp.Camera35mmLensLength = lens;
      }
    }

    if (!vp.IsValidCamera)
    {
      return false;
    }

    // updateTargetLocation: false — the target set above is the source's own, not one recomputed from the direction.
    return viewport.SetViewProjection(vp, false);
  }

  // A parallel view's "zoom" is its frustum height, which the send side records as OrthoHeight. Rebuild a symmetric
  // frustum of that height, widened by the recorded aspect (falling back to the viewport's own) so the view frames
  // the same region it did in the source document.
  private static void ApplyOrthoExtents(ViewportInfo vp, ArtefactCameraView view, double scale)
  {
    if (view.OrthoHeight is not double orthoHeight || orthoHeight <= 0)
    {
      return;
    }

    double height = orthoHeight * scale;
    double aspect = view.Aspect is double a && a > 0 ? a : vp.FrustumAspect;
    if (aspect <= 0)
    {
      aspect = 1.0;
    }

    double halfHeight = height / 2.0;
    double halfWidth = halfHeight * aspect;
    double near = view.Near is double n && n > 0 ? n * scale : vp.FrustumNear;
    double far = view.Far is double f && f > 0 ? f * scale : vp.FrustumFar;
    if (far <= near)
    {
      return; // recorded planes are unusable — keep whatever the parallel switch produced
    }

    vp.SetFrustum(-halfWidth, halfWidth, -halfHeight, halfHeight, near, far);
  }

  private static void RemoveNamedView(RhinoDoc doc, string name)
  {
    for (int i = doc.NamedViews.Count - 1; i >= 0; i--)
    {
      if (string.Equals(doc.NamedViews[i].Name, name, StringComparison.OrdinalIgnoreCase))
      {
        doc.NamedViews.Delete(i);
        break;
      }
    }
  }
}
