using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Deterministic joint-endpoint comparison, shared by connection creation and layout planning.</summary>
public static class ConnectorPathQuality
{
    public static (int Crossings, int Bends, double Length) Score(ImmutableArray<PointD> path,
        IEnumerable<ImmutableArray<PointD>> otherPaths) =>
        (otherPaths.Sum(other => Crossings(path, other)), Math.Max(0, path.Length - 2),
            path.Zip(path.Skip(1), (a, b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)).Sum());

    public static int Crossings(ImmutableArray<PointD> first, ImmutableArray<PointD> second)
    {
        var count = 0;
        for (var a = 1; a < first.Length; a++)
            for (var b = 1; b < second.Length; b++)
            {
                var p = first[a - 1]; var q = first[a]; var r = second[b - 1]; var s = second[b];
                if (p.X == q.X && r.Y == s.Y && p.X > Math.Min(r.X, s.X) && p.X < Math.Max(r.X, s.X) &&
                    r.Y > Math.Min(p.Y, q.Y) && r.Y < Math.Max(p.Y, q.Y) ||
                    p.Y == q.Y && r.X == s.X && r.X > Math.Min(p.X, q.X) && r.X < Math.Max(p.X, q.X) &&
                    p.Y > Math.Min(r.Y, s.Y) && p.Y < Math.Max(r.Y, s.Y)) count++;
            }
        return count;
    }
}
