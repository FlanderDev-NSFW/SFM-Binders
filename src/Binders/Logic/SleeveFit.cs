using System;
using System.Collections.Generic;
using System.Numerics;

namespace Binders.Logic
{
    /// <summary>
    /// A ring across a sleeve at one height, in the sleeve's local frame: centre (x, z), half-extents and its roundness - a
    /// superellipse |x/Hx|^P + |z/Hz|^P = 1, an oval at P = 2, squarer above (0 counts as 2).
    /// </summary>
    public struct Ring
    {
        public float Cx, Cz, Hx, Hz, P;
        public bool Ok;

        public float Power => P < 2f ? 2f : P;

        public static Ring Lerp(Ring a, Ring b, float t) => new Ring
        {
            Cx = a.Cx + (b.Cx - a.Cx) * t, Cz = a.Cz + (b.Cz - a.Cz) * t,
            Hx = a.Hx + (b.Hx - a.Hx) * t, Hz = a.Hz + (b.Hz - a.Hz) * t,
            P = a.Power + (b.Power - a.Power) * t, Ok = a.Ok && b.Ok,
        };

        /// <summary>How far out along the unit direction (du, dv) the ring's edge lies, in half-extents (1 for an oval).</summary>
        public float Reach(float du, float dv)
        {
            float p = Power;
            if (p <= 2.001f) return 1f;
            return MathF.Pow(MathF.Pow(Math.Abs(du), p) + MathF.Pow(Math.Abs(dv), p), -1f / p);
        }
    }

    /// <summary>
    /// A sleeve shaped to the limb inside it, ring by ring: the model's inner wall is measured at <see cref="Count"/> heights
    /// (<see cref="SleeveShape.From"/>), the limb's outline at the same heights (<see cref="FitRings"/>), and every vertex is moved
    /// so the inner wall follows the limb with a clearance while keeping its own distance from the wall (straps, buckles and
    /// the wall's thickness stay as modelled; <see cref="Deform"/>). All in the sleeve's local frame (<see cref="BinderPart.ToLocal"/>:
    /// y up the sleeve from the cup, z towards the folded segment), in the model's units.
    /// </summary>
    public sealed class SleeveShape
    {
        public const int Count = 12;
        public Vector3[] Local;      // the vertices as modelled
        public Vector3[] Normals;
        public float Length;         // y of the opening
        public float InnerBottom;    // y of the cup's inner floor
        public float[] Heights = new float[Count];
        public Ring[] Inner = new Ring[Count]; // the model's inner wall at each height

        public static SleeveShape From(Vector3[] local, Vector3[] normals, float length)
        {
            var s = new SleeveShape { Local = local, Normals = normals, Length = length };
            // The cup's inner floor: the highest point near the axis with a normal pointing up the sleeve.
            float floor = 0f;
            float r2 = 0.01f * 0.01f;
            for (int i = 0; i < local.Length; i++)
            {
                var p = local[i];
                if (p.X * p.X + p.Z * p.Z < r2 && normals[i].Y > 0.7f && p.Y < length * 0.3f) floor = Math.Max(floor, p.Y);
            }
            s.InnerBottom = floor;
            float band = (length - floor) / Count;
            for (int k = 0; k < Count; k++)
            {
                float y = floor + band * (k + 0.5f);
                s.Heights[k] = y;
                // Centre of the band (all vertices), then the inner wall: vertices whose normal points back towards that centre.
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                for (int i = 0; i < local.Length; i++)
                    if (Math.Abs(local[i].Y - y) <= band / 2f) { x0 = Math.Min(x0, local[i].X); x1 = Math.Max(x1, local[i].X); z0 = Math.Min(z0, local[i].Z); z1 = Math.Max(z1, local[i].Z); }
                if (x0 > x1) continue;
                float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f;
                float ix0 = float.MaxValue, ix1 = float.MinValue, iz0 = float.MaxValue, iz1 = float.MinValue;
                for (int i = 0; i < local.Length; i++)
                {
                    var p = local[i];
                    if (Math.Abs(p.Y - y) > band / 2f) continue;
                    float dx = p.X - cx, dz = p.Z - cz;
                    if (dx * normals[i].X + dz * normals[i].Z >= 0f) continue; // faces outwards
                    ix0 = Math.Min(ix0, p.X); ix1 = Math.Max(ix1, p.X); iz0 = Math.Min(iz0, p.Z); iz1 = Math.Max(iz1, p.Z);
                }
                if (ix0 > ix1) continue;
                s.Inner[k] = new Ring { Cx = (ix0 + ix1) / 2f, Cz = (iz0 + iz1) / 2f, Hx = (ix1 - ix0) / 2f, Hz = (iz1 - iz0) / 2f, Ok = true };
            }
            Fill(s.Inner);
            return s;
        }

        /// <summary>Rings without data take their nearest neighbour's (below first, so an opening keeps the limb's last shape).</summary>
        public static void Fill(Ring[] rings)
        {
            int first = Array.FindIndex(rings, r => r.Ok);
            if (first < 0) return;
            for (int k = 0; k < first; k++) rings[k] = rings[first];
            for (int k = first + 1; k < rings.Length; k++) if (!rings[k].Ok) rings[k] = rings[k - 1];
        }

        /// <summary>The ring at height <paramref name="y"/>, interpolated (the ends held).</summary>
        public Ring At(Ring[] rings, float y)
        {
            if (y <= Heights[0]) return rings[0];
            if (y >= Heights[Count - 1]) return rings[Count - 1];
            float band = Heights[1] - Heights[0];
            int k = Math.Min((int)((y - Heights[0]) / band), Count - 2);
            return Ring.Lerp(rings[k], rings[k + 1], (y - Heights[k]) / band);
        }

        /// <summary>
        /// The limb's outline as rings: for each height, the smallest oval of the extents' proportions around the extents' centre
        /// that holds all <paramref name="body"/> points (sleeve frame, model units) within a band of it (up to the neighbouring rings), widened by
        /// <paramref name="clearance"/>; at least <paramref name="minHalf"/>, at most
        /// the model's inner ring times <paramref name="maxGrow"/>. Heights with fewer than <paramref name="minPoints"/> take the
        /// nearest ring's; from the middle up a ring also covers the one below it.
        /// </summary>
        private static readonly float[] Powers = { 2f, 2.5f, 3f, 4f, 6f };

        public Ring[] FitRings(IReadOnlyList<Vector3> body, float clearance, float minHalf, float maxGrow, int minPoints = 1)
        {
            var rings = new Ring[Count];
            float band = Count > 1 ? Heights[1] - Heights[0] : Length;
            // Each ring holds the limb up to its neighbours' heights: between two rings the sleeve blends them, and a ring that
            // held only its own half-band let skin through in the blend (a spot on each arm sleeve) [observed 2026-10-25].
            float reach = band;
            for (int k = 0; k < Count; k++)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                int n = 0;
                foreach (var p in body)
                {
                    if (Math.Abs(p.Y - Heights[k]) > reach) continue;
                    x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
                    n++;
                }
                if (n < minPoints || x0 > x1) continue;
                Ring m = Inner[k];
                // The oval through the outline's box misses its corners (thigh and calf side by side are more a rounded box than
                // an oval: the calves showed through the leg sleeves' backs) [observed 2026-10-25]. Grown into an oval that holds
                // them, the arm sleeves came out half as wide again as the arms; so the ring squares off instead - the roundest
                // superellipse that holds the outline with at most 5 % growth.
                float cx = (x0 + x1) / 2f, cz = (z0 + z1) / 2f, hx = Math.Max((x1 - x0) / 2f, 1e-4f), hz = Math.Max((z1 - z0) / 2f, 1e-4f);
                float power = Powers[Powers.Length - 1], grow = 1f;
                foreach (float p in Powers)
                {
                    float g = 0f;
                    foreach (var q in body)
                    {
                        if (Math.Abs(q.Y - Heights[k]) > reach) continue;
                        float u = Math.Abs(q.X - cx) / hx, v = Math.Abs(q.Z - cz) / hz;
                        g = Math.Max(g, MathF.Pow(MathF.Pow(u, p) + MathF.Pow(v, p), 1f / p));
                    }
                    power = p;
                    grow = Math.Max(g, 1f);
                    if (g <= 1.05f) break;
                }
                rings[k] = new Ring
                {
                    Cx = cx, Cz = cz, P = power, Ok = true,
                    Hx = Math.Clamp(hx * grow + clearance, minHalf, Math.Max(minHalf, m.Hx * maxGrow)),
                    Hz = Math.Clamp(hz * grow + clearance, minHalf, Math.Max(minHalf, m.Hz * maxGrow)),
                };
            }
            Fill(rings);
            if (!rings[0].Ok) return null;
            // Towards the opening a ring never closes in on the one below it (there the limb meets the body and only part of it
            // is measured: the leg sleeves' openings came out 3.8 cm deep and the calves showed through) [observed 2026-10-25].
            for (int k = Count / 2; k < Count; k++)
            {
                Ring below = rings[k - 1], r = rings[k];
                float lx = Math.Min(below.Cx - below.Hx, r.Cx - r.Hx), hx = Math.Max(below.Cx + below.Hx, r.Cx + r.Hx);
                float lz = Math.Min(below.Cz - below.Hz, r.Cz - r.Hz), hz = Math.Max(below.Cz + below.Hz, r.Cz + r.Hz);
                rings[k] = new Ring { Cx = (lx + hx) / 2f, Cz = (lz + hz) / 2f, Hx = (hx - lx) / 2f, Hz = (hz - lz) / 2f, P = Math.Max(below.Power, r.Power), Ok = true };
            }
            return rings;
        }

        /// <summary>
        /// The vertices moved so the inner wall follows <paramref name="target"/>: a vertex inside the model's inner oval is scaled
        /// with it; one in or on the wall keeps its offset from the inner oval point in its direction.
        /// </summary>
        public Vector3[] Deform(Ring[] target)
        {
            var result = new Vector3[Local.Length];
            for (int i = 0; i < Local.Length; i++)
            {
                var p = Local[i];
                Ring m = At(Inner, p.Y), t = At(target, p.Y);
                if (!m.Ok || m.Hx <= 1e-6f || m.Hz <= 1e-6f) { result[i] = p; continue; }
                float u = (p.X - m.Cx) / m.Hx, v = (p.Z - m.Cz) / m.Hz;
                float r = MathF.Sqrt(u * u + v * v);
                if (r < 1e-6f) { result[i] = new Vector3(t.Cx, p.Y, t.Cz); continue; }
                float du = u / r, dv = v / r, reach = t.Reach(du, dv); // the model's oval point in this direction -> the target's edge
                if (r <= 1f) result[i] = new Vector3(t.Cx + u * reach * t.Hx, p.Y, t.Cz + v * reach * t.Hz);
                else
                {
                    float ox = p.X - (m.Cx + m.Hx * du), oz = p.Z - (m.Cz + m.Hz * dv); // offset from the inner wall
                    result[i] = new Vector3(t.Cx + t.Hx * du * reach + ox, p.Y, t.Cz + t.Hz * dv * reach + oz);
                }
            }
            return result;
        }

        /// <summary>Rings that cover all of <paramref name="fits"/>, height by height (the box around them all, the squarest
        /// shape): the sleeve fitted over the last moments of a movement.</summary>
        public static Ring[] Union(IEnumerable<Ring[]> fits)
        {
            Ring[] result = null;
            foreach (var f in fits)
            {
                if (f == null) continue;
                if (result == null) { result = (Ring[])f.Clone(); continue; }
                for (int k = 0; k < result.Length; k++)
                {
                    Ring a = result[k], b = f[k];
                    float lx = Math.Min(a.Cx - a.Hx, b.Cx - b.Hx), hx = Math.Max(a.Cx + a.Hx, b.Cx + b.Hx);
                    float lz = Math.Min(a.Cz - a.Hz, b.Cz - b.Hz), hz = Math.Max(a.Cz + a.Hz, b.Cz + b.Hz);
                    result[k] = new Ring { Cx = (lx + hx) / 2f, Cz = (lz + hz) / 2f, Hx = (hx - lx) / 2f, Hz = (hz - lz) / 2f, P = Math.Max(a.Power, b.Power), Ok = a.Ok && b.Ok };
                }
            }
            return result;
        }

        /// <summary>The widest reach to either side (|x|) of the deformed sleeve.</summary>
        public static float HalfWidth(Vector3[] positions)
        {
            float max = 0f;
            foreach (var p in positions) max = Math.Max(max, Math.Abs(p.X));
            return max;
        }
    }
}
