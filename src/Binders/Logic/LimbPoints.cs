using System;
using System.Collections.Generic;
using System.Numerics;

namespace Binders.Logic
{
    /// <summary>A body segment as a line between two joints; points beyond <see cref="MaxT"/> of its length (from A) do not
    /// belong to it (the end where it joins the torso).</summary>
    public struct Segment
    {
        public Vector3 A, B;
        public float MaxT;
        public int Owner;   // which sleeve it is in (-1 = none: torso, head)

        public Segment(Vector3 a, Vector3 b, int owner, float maxT = 1f) { A = a; B = b; Owner = owner; MaxT = maxT; }

        /// <summary>Distance from <paramref name="p"/> to the segment (clamped at both ends) and where along it (0 at A).</summary>
        public (float Distance, float T) Measure(Vector3 p)
        {
            Vector3 ab = B - A;
            float len2 = ab.LengthSquared();
            float t = len2 < 1e-12f ? 0f : Vector3.Dot(p - A, ab) / len2;
            Vector3 c = A + ab * Math.Clamp(t, 0f, 1f);
            return (Vector3.Distance(p, c), t);
        }
    }

    /// <summary>Her skin split by limb: each point goes to the nearest segment of her skeleton.</summary>
    public static class LimbPoints
    {
        /// <summary>Each point's owner (the owner of its nearest segment, -1 for none): owners 0 .. <paramref name="windows"/>.Length - 1
        /// within their window and not past the segment's <see cref="Segment.MaxT"/>. Points outside every owner's box (its
        /// segments widened by its window) are skipped without measuring.</summary>
        public static int[] Classify(IReadOnlyList<Vector3> points, IReadOnlyList<Segment> segments, float[] windows)
        {
            int owners = windows.Length;
            var result = new int[points.Count];
            var lo = new Vector3[owners];
            var hi = new Vector3[owners];
            for (int o = 0; o < owners; o++)
            {
                lo[o] = new Vector3(float.MaxValue);
                hi[o] = new Vector3(float.MinValue);
            }
            foreach (var s in segments)
            {
                if (s.Owner < 0 || s.Owner >= owners) continue;
                var w = new Vector3(windows[s.Owner]);
                lo[s.Owner] = Vector3.Min(lo[s.Owner], Vector3.Min(s.A, s.B) - w);
                hi[s.Owner] = Vector3.Max(hi[s.Owner], Vector3.Max(s.A, s.B) + w);
            }
            // The segments flattened for the inner loop (~10 000 points x 13 segments).
            int n = segments.Count;
            var a = new Vector3[n];
            var ab = new Vector3[n];
            var inv = new float[n];
            for (int k = 0; k < n; k++)
            {
                a[k] = segments[k].A;
                ab[k] = segments[k].B - segments[k].A;
                float len2 = ab[k].LengthSquared();
                inv[k] = len2 < 1e-12f ? 0f : 1f / len2;
            }
            for (int i = 0; i < points.Count; i++)
            {
                result[i] = -1;
                Vector3 p = points[i];
                bool near = false;
                for (int o = 0; o < owners && !near; o++)
                    near = p.X >= lo[o].X && p.X <= hi[o].X && p.Y >= lo[o].Y && p.Y <= hi[o].Y && p.Z >= lo[o].Z && p.Z <= hi[o].Z;
                if (!near) continue;
                float best = float.MaxValue, bestT = 0f;
                int bestK = -1;
                for (int k = 0; k < n; k++)
                {
                    Vector3 ap = p - a[k];
                    float t = Vector3.Dot(ap, ab[k]) * inv[k];
                    float c = t < 0f ? 0f : t > 1f ? 1f : t;
                    float d2 = (ap - ab[k] * c).LengthSquared();
                    if (d2 < best) { best = d2; bestT = t; bestK = k; }
                }
                if (bestK < 0) continue;
                int who = segments[bestK].Owner;
                if (who >= 0 && who < owners && best <= windows[who] * windows[who] && bestT <= segments[bestK].MaxT) result[i] = who;
            }
            return result;
        }

        /// <summary>The points of each owner (<see cref="Classify"/>).</summary>
        public static List<Vector3>[] SelectAll(IReadOnlyList<Vector3> points, IReadOnlyList<Segment> segments, float[] windows)
        {
            var owner = Classify(points, segments, windows);
            var result = new List<Vector3>[windows.Length];
            for (int o = 0; o < result.Length; o++) result[o] = new List<Vector3>();
            for (int i = 0; i < owner.Length; i++) if (owner[i] >= 0) result[owner[i]].Add(points[i]);
            return result;
        }

        /// <summary>The points whose nearest segment belongs to <paramref name="owner"/>, within <paramref name="window"/> of it
        /// and not past its <see cref="Segment.MaxT"/>.</summary>
        public static List<Vector3> Select(IReadOnlyList<Vector3> points, IReadOnlyList<Segment> segments, int owner, float window)
        {
            var result = new List<Vector3>();
            foreach (var p in points)
            {
                float best = float.MaxValue, bestT = 0f;
                int who = -1;
                Segment bestSeg = default;
                foreach (var s in segments)
                {
                    var (d, t) = s.Measure(p);
                    if (d < best) { best = d; bestT = t; who = s.Owner; bestSeg = s; }
                }
                if (who == owner && best <= window && bestT <= bestSeg.MaxT) result.Add(p);
            }
            return result;
        }
    }
}
