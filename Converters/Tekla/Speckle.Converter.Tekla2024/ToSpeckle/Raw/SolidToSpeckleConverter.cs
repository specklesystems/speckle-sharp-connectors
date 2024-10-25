using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using SOG = Speckle.Objects.Geometry;
using TG = Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using System.Collections.Generic;
using System.Linq;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class TeklaMeshConverter : ITypedConverter<TSM.Solid, SOG.Mesh> 
{
    private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
    private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;

    public TeklaMeshConverter(
        IConverterSettingsStore<TeklaConversionSettings> settingsStore,
        ITypedConverter<TG.Point, SOG.Point> pointConverter)
    {
        _settingsStore = settingsStore;
        _pointConverter = pointConverter;
    }

    public SOG.Mesh Convert(TSM.Solid target)
    {
        var faceEnum = target.GetFaceEnumerator();
        List<SOG.Point> vertices = new();
        List<int> faces = new();

        while (faceEnum.MoveNext())
        {
            if (faceEnum.Current is not null)
            {
                var face = faceEnum.Current;
                var loopEnum = face.GetLoopEnumerator();

                while (loopEnum.MoveNext())
                {
                    if (loopEnum.Current is not null)
                    {
                        var loop = loopEnum.Current;
                        var vertexEnum = loop.GetVertexEnumerator();
                        List<int> faceIndices = new();

                        while (vertexEnum.MoveNext())
                        {
                            if (vertexEnum.Current is TG.Point vertex)
                            {
                                var specklePoint = _pointConverter.Convert(vertex);
                                int vertexIndex = vertices.Count;
                                vertices.Add(specklePoint);
                                faceIndices.Add(vertexIndex);
                            }
                        }

                        // Triangulate face (assuming convex faces)
                        for (int i = 1; i < faceIndices.Count - 1; i++)
                        {
                            faces.Add(faceIndices[0]);
                            faces.Add(faceIndices[i]);
                            faces.Add(faceIndices[i + 1]);
                        }
                    }
                }
            }
        }

        return new SOG.Mesh
        {
            vertices = vertices.SelectMany(p => new[] { p.x, p.y, p.z }).ToList(),
            faces = faces,
            units = _settingsStore.Current.SpeckleUnits
        };
    }
}
