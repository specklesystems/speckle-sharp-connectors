using Speckle.Sdk.Models;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>
/// Civil Quantities (dgnextract's MeshQuantities): sloped area = Σ true 3D triangle areas over the
/// extracted meshes; planar area = the larger of the upward-/downward-facing XY-projected sums
/// (a closed civil mesh projects its top and bottom onto the same footprint).
/// </summary>
public static class CivilQuantities
{
  public static void AddTo(IReadOnlyList<Base> displayValue, Dictionary<string, object?> properties)
  {
    double sloped = 0,
      planarUp = 0,
      planarDown = 0;
    foreach (Base geometry in displayValue)
    {
      if (geometry is not SOG.Mesh mesh)
      {
        continue;
      }
      List<double> v = mesh.vertices;
      List<int> f = mesh.faces;
      int i = 0;
      while (i < f.Count)
      {
        int n = f[i];
        if (n < 3 || i + n >= f.Count)
        {
          break;
        }
        for (int k = 2; k < n; k++)
        {
          int a = f[i + 1] * 3,
            b = f[i + k] * 3,
            c = f[i + k + 1] * 3;
          if (c + 2 >= v.Count)
          {
            continue;
          }
          double ux = v[b] - v[a],
            uy = v[b + 1] - v[a + 1],
            uz = v[b + 2] - v[a + 2];
          double wx = v[c] - v[a],
            wy = v[c + 1] - v[a + 1],
            wz = v[c + 2] - v[a + 2];
          double cx = uy * wz - uz * wy,
            cy = uz * wx - ux * wz,
            cz = ux * wy - uy * wx;
          sloped += 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
          double projected = 0.5 * cz;
          if (projected >= 0)
          {
            planarUp += projected;
          }
          else
          {
            planarDown -= projected;
          }
        }
        i += n + 1;
      }
    }
    if (sloped <= 0)
    {
      return;
    }
    properties["Civil Quantities"] = new Dictionary<string, object?>
    {
      ["Sloped Area"] = new Dictionary<string, object?> { ["value"] = sloped, ["name"] = "Sloped Area" },
      ["Planar Area"] = new Dictionary<string, object?>
      {
        ["value"] = Math.Max(planarUp, planarDown),
        ["name"] = "Planar Area",
      },
    };
  }
}
