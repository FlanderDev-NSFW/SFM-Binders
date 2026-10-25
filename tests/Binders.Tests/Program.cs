using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Binders.Logic;

namespace Binders.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;
        private static bool Near(Vector3 a, Vector3 b, float eps = 1e-4f) => Vector3.Distance(a, b) <= eps;
        private static float Deg(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

        private static int Main()
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, "binders.bnd");
            if (File.Exists(modelPath)) ModelTests(modelPath);
            else Console.WriteLine("  SKIP  model tests (" + modelPath + " missing: export it with mods/Binders/tools/export_model.py)");
            FormatErrorTests();
            PoseTests();
            FitTests();
            LimbPointTests();

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void ModelTests(string path)
        {
            BinderModel m;
            using (var f = File.OpenRead(path)) m = BinderModel.Read(f);
            Check(m.Parts.Count == 4, "model: four sleeves");
            var armL = m.Get(BinderKind.Arm, 0);
            var legR = m.Get(BinderKind.Leg, 1);
            Check(armL.Bottom.X > 0 && legR.Bottom.X < 0, "model: her left sleeves at +x, right at -x");
            Check(armL.Length > 0.2f && armL.Length < 0.35f && legR.Length > 0.25f && legR.Length < 0.4f, "model: sleeves 20-40 cm long");
            Check(armL.Uvs.Length == armL.Positions.Length, "model: one uv per vertex");
            float halfLeg = legR.HalfWidth(), halfArm = armL.HalfWidth();
            Check(halfLeg > 0.06f && halfLeg < 0.12f && halfArm > 0.04f && halfArm < 0.08f && Near(legR.HalfWidth(), halfLeg),
                  "model: sleeves' half widths (leg " + halfLeg.ToString("0.000") + ", arm " + halfArm.ToString("0.000") + " m), cached");

            var (pos, nrm, idx) = armL.ToLocal();
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pos) { minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            Check(Near(minY, 0f, 0.01f) && Near(maxY, armL.Length, 0.01f), "local: the sleeve runs from y 0 (cup) to its length (opening)");
            Check(idx[1] == armL.Indices[2] && idx[2] == armL.Indices[1], "local: winding flipped for Unity's handedness");
            // Unity's front faces: Cross(b - a, c - a) (as Mesh.RecalculateNormals computes it) points like the vertex normals.
            int agree = 0, total = 0;
            for (int i = 0; i < idx.Length; i += 3 * 37)
            {
                Vector3 a = pos[idx[i]], b = pos[idx[i + 1]], c = pos[idx[i + 2]];
                Vector3 g = Vector3.Cross(b - a, c - a);
                if (g.LengthSquared() < 1e-14f) continue;
                total++;
                if (Vector3.Dot(g, nrm[idx[i]] + nrm[idx[i + 1]] + nrm[idx[i + 2]]) > 0) agree++;
            }
            Check(total > 50 && agree > total * 0.9f, "local: triangles face the way their normals do (" + agree + "/" + total + ")");

            foreach (var part in m.Parts)
            {
                var (lp, ln, _) = part.ToLocal();
                var sh = SleeveShape.From(lp, ln, part.Length);
                bool ok = sh.Inner.All(r => r.Ok && r.Hx > 0.02f && r.Hz > 0.02f && r.Hx < 0.12f && r.Hz < 0.15f);
                Check(ok && sh.InnerBottom > 0f && sh.InnerBottom < 0.05f,
                      "shape: " + part.Kind + " " + part.Side + " inner wall at all heights (" + sh.Inner[0].Hx.ToString("0.000") + " x " + sh.Inner[0].Hz.ToString("0.000")
                      + " .. " + sh.Inner[11].Hx.ToString("0.000") + " x " + sh.Inner[11].Hz.ToString("0.000") + "), cup floor " + sh.InnerBottom.ToString("0.000"));
            }
            {
                // The real leg sleeve deformed to smaller, shifted rings: measured again, its inner wall is where it was asked to be.
                var (lp, ln, _) = legR.ToLocal();
                var sh = SleeveShape.From(lp, ln, legR.Length);
                var want = sh.Inner.Select(r => new Ring { Cx = r.Cx + 0.01f, Cz = r.Cz - 0.02f, Hx = r.Hx * 0.8f, Hz = r.Hz * 0.7f, Ok = true }).ToArray();
                var moved = sh.Deform(want);
                var again = SleeveShape.From(moved, ln, legR.Length);
                float worst = 0f; int worstK = -1;
                for (int k = 1; k < SleeveShape.Count - 1; k++)
                {
                    float e = Math.Max(Math.Max(Math.Abs(again.Inner[k].Hx - want[k].Hx), Math.Abs(again.Inner[k].Hz - want[k].Hz)),
                                       Math.Max(Math.Abs(again.Inner[k].Cx - want[k].Cx), Math.Abs(again.Inner[k].Cz - want[k].Cz)));
                    if (e > worst) { worst = e; worstK = k; }
                }
                Check(worst < 0.004f, "deform: the real leg sleeve's inner wall where asked (worst " + (worst * 100).ToString("0.0") + " cm, ring " + worstK + ")");
            }
            var (axisA, foldA) = armL.Frame();
            var (axisL, foldL) = legR.Frame();
            Check(foldA.Y < -0.9f && foldL.Y > 0.9f && Near(Vector3.Dot(axisA, foldA), 0f) && Near(Vector3.Dot(axisL, foldL), 0f),
                  "frame: forearms fold to the front (-y), shins to the back (+y), across the axis");
        }

        private static void FormatErrorTests()
        {
            bool Throws(byte[] data)
            {
                try { BinderModel.Read(new MemoryStream(data)); return false; }
                catch (InvalidDataException) { return true; }
                catch (EndOfStreamException) { return true; }
            }
            Check(Throws(new byte[] { 1, 2, 3, 4, 1, 0, 0, 0 }), "format: wrong magic refused");
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(BinderModel.Magic); w.Write(2u);
            Check(Throws(ms.ToArray()), "format: unknown version refused");
            ms = new MemoryStream();
            w = new BinaryWriter(ms);
            w.Write(BinderModel.Magic); w.Write(1u); w.Write(0u);
            Check(Throws(ms.ToArray()), "format: a model without its four sleeves refused");
        }

        private static void PoseTests()
        {
            Vector3 up = Vector3.UnitY, fwd = Vector3.UnitZ, floor = Vector3.Zero;
            // The game's crawl [observed 2026-10-25]: spine joint 42 cm up, shoulders 33 cm ahead of it at 46 cm.
            Vector3 pivot = new Vector3(0, 0.42f, -0.38f), shoulders = new Vector3(0, 0.46f, -0.09f);
            Vector3 low = CrawlPose.LowerShoulders(pivot, shoulders, floor, up, 0.30f);
            Check(Near(low.Y, 0.30f) && Near(Vector3.Distance(low, pivot), Vector3.Distance(shoulders, pivot)) && low.Z > pivot.Z,
                  "spine: shoulders lowered to the height asked, the spine keeps its length, still ahead of the hips");
            Check(Near(CrawlPose.LowerShoulders(pivot, shoulders, floor, up, 0.5f), shoulders), "spine: never raised");
            Vector3 deep = CrawlPose.LowerShoulders(pivot, shoulders, floor, up, -1f);
            Check(Near(Vector3.Distance(deep, pivot), Vector3.Distance(shoulders, pivot)) && deep.Y < pivot.Y, "spine: out of reach -> as low as it turns");

            var (off, drop) = CrawlPose.ElbowStep(new Vector3(0.02f, -0.4f, 0.05f), up, 0.26f, 0.5f);
            Check(Near(off.Y, 0f) && Near(off.Length() * off.Length() + drop * drop, 0.26f * 0.26f), "elbow: flat step, the upper arm reaches it");
            var (far, _) = CrawlPose.ElbowStep(new Vector3(0f, 0f, 0.5f), up, 0.26f, 0.5f);
            Check(Near(far.Length(), 0.13f), "elbow: a long step shortened to half the upper arm");

            Vector3 elbowToShoulder = up;
            Vector3 fore = CrawlPose.Fold(elbowToShoulder, fwd, 12f * MathF.PI / 180f);
            Check(Near(Deg(fore, up), 12f, 0.01f) && fore.Z > 0 && Near(fore.Length(), 1f), "fold: forearm 12 deg off the upper arm, to the front");
            Check(Near(CrawlPose.Fold(up, up, 0.3f), up), "fold: no side across the segment -> along it");

            Vector3 knee = Vector3.Zero, hip = new Vector3(0, 0.39f, 0), foot = 0.42f * CrawlPose.Fold(up, -fwd, 14f * MathF.PI / 180f);
            var (axis, foldDir) = CrawlPose.Sleeve(knee, hip, foot, -fwd);
            Check(Near(Deg(axis, up), 7f, 0.01f) && foldDir.Z < -0.99f, "sleeve: axis halfway between thigh and shin, folding to the back");
            var (axis2, fold2) = CrawlPose.Sleeve(knee, hip, hip * 2f, -fwd);
            Check(Near(axis2, up) && Near(fold2, -fwd), "sleeve: segments on each other -> the fallback fold");
        }

        /// <summary>A synthetic sleeve: walls (inner radius 5 cm, outer 6 cm) from y 0 to 0.3, a cup floor at y 0.01.</summary>
        private static SleeveShape Tube()
        {
            var pos = new List<Vector3>(); var nrm = new List<Vector3>();
            for (int k = 0; k <= 30; k++)
                for (int a = 0; a < 24; a++)
                {
                    float y = 0.01f * k, t = a * MathF.PI * 2f / 24f;
                    var d = new Vector3(MathF.Cos(t), 0f, MathF.Sin(t));
                    pos.Add(d * 0.05f + new Vector3(0, y, 0)); nrm.Add(-d);
                    pos.Add(d * 0.06f + new Vector3(0, y, 0)); nrm.Add(d);
                }
            for (int a = 0; a < 8; a++) { float t = a * MathF.PI / 4f; pos.Add(new Vector3(MathF.Cos(t) * 0.005f, 0.01f, MathF.Sin(t) * 0.005f)); nrm.Add(Vector3.UnitY); }
            return SleeveShape.From(pos.ToArray(), nrm.ToArray(), 0.3f);
        }

        private static void FitTests()
        {
            var s = Tube();
            Check(Near(s.InnerBottom, 0.01f) && s.Inner.All(r => r.Ok && Near(r.Hx, 0.05f, 1e-3f) && Near(r.Hz, 0.05f, 1e-3f)),
                  "shape: the inner wall measured at every height (5 cm), the cup floor found");

            // A limb inside: an oval 3 x 4 cm around (0.01, 0.02), thinner near the cup.
            var body = new List<Vector3>();
            for (int k = 0; k < 60; k++)
                for (int a = 0; a < 16; a++)
                {
                    float y = 0.02f + 0.0045f * k, t = a * MathF.PI / 8f, f = y < 0.1f ? 0.7f : 1f;
                    body.Add(new Vector3(0.01f + 0.03f * f * MathF.Cos(t), y, 0.02f + 0.04f * f * MathF.Sin(t)));
                }
            var rings = s.FitRings(body, 0.004f, 0.01f, 1.5f);
            Check(rings != null && Near(rings[5].Hx, 0.034f, 2e-3f) && Near(rings[5].Hz, 0.044f, 2e-3f) && Near(rings[5].Cx, 0.01f, 1e-3f) && Near(rings[5].Cz, 0.02f, 1e-3f),
                  "fit: a ring is the limb's outline plus the clearance, centred on it");
            Check(rings[1].Hx < rings[5].Hx, "fit: thinner limb -> narrower ring (below the middle)");
            // A square outline (4 x 4 cm, points on its edges): the oval must hold its corners.
            var square = new List<Vector3>();
            for (int i = 0; i <= 20; i++)
            {
                float t = -0.02f + 0.002f * i;
                foreach (var (x, z) in new[] { (t, -0.02f), (t, 0.02f), (-0.02f, t), (0.02f, t) }) square.Add(new Vector3(x, 0.1f, z));
            }
            var sq = s.FitRings(square, 0f, 0.001f, 3f);
            Check(sq[3].P >= 6f && sq[3].Hx <= 0.02f * 1.13f && sq[3].Hx >= 0.02f && sq[3].Reach(MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)) * sq[3].Hx * MathF.Sqrt(0.5f) >= 0.02f - 1e-4f,
                  "fit: a square outline gets a squarer ring that holds its corners (P " + sq[3].P + ", half " + (sq[3].Hx * 100).ToString("0.00") + " cm)");
            Check(rings[5].P <= 2f + 1e-4f, "fit: an oval outline keeps an oval ring");
            var shifted = rings.Select(r => new Ring { Cx = r.Cx + 0.01f, Cz = r.Cz, Hx = r.Hx, Hz = r.Hz, P = 3f, Ok = true }).ToArray();
            var both = SleeveShape.Union(new[] { rings, shifted });
            Check(Near(both[4].Cx - both[4].Hx, rings[4].Cx - rings[4].Hx) && Near(both[4].Cx + both[4].Hx, shifted[4].Cx + shifted[4].Hx) && both[4].P == 3f,
                  "union: rings covering both fits (box around them, the squarer shape)");
            var squareRings = Enumerable.Range(0, SleeveShape.Count).Select(_ => new Ring { Hx = 0.04f, Hz = 0.04f, P = 6f, Ok = true }).ToArray();
            var boxed = s.Deform(squareRings);
            Vector3 diag = boxed[2 * (10 * 24 + 3)]; // y 0.1, 45 deg, inner wall
            Check(Near(diag.X, 0.04f * MathF.Sqrt(0.5f) * squareRings[0].Reach(MathF.Sqrt(0.5f), MathF.Sqrt(0.5f)), 1e-4f) && diag.X > 0.04f * MathF.Sqrt(0.5f) + 0.003f,
                  "deform: a squarer ring pushes the wall out at the diagonals");
            Check(Near(rings[11].Hx, rings[10].Hx) && rings[11].Hx >= rings[5].Hx - 1e-5f, "fit: towards the opening a ring covers the one below it");
            var sparse = s.FitRings(body.Where(p => p.Y < 0.2f || p.Y > 0.25f && (int)(p.Y * 1000) % 7 == 0).ToList(), 0.004f, 0.01f, 1.5f, 25);
            Check(sparse != null && sparse.All(r => r.Ok), "fit: a ring with too few points takes the nearest one's");
            var none = s.FitRings(new List<Vector3> { new Vector3(0, 0.29f, 0) }, 0.004f, 0.01f, 1.5f);
            Check(none != null && Near(none[0].Hx, 0.01f) && Near(none[0].Hx, none[11].Hx), "fit: heights without points take the nearest ring's; at least the minimum");
            var big = s.FitRings(new List<Vector3> { new Vector3(-1f, 0.1f, 0), new Vector3(1f, 0.1f, 0) }, 0f, 0.01f, 1.5f);
            Check(Near(big[3].Hx, 0.075f, 1e-3f), "fit: never more than maxGrow times the model's ring");

            var moved = s.Deform(rings);
            Ring at = s.At(rings, 0.1f);
            Vector3 inner = moved[2 * (10 * 24 + 0)], outer = moved[2 * (10 * 24 + 0) + 1]; // y 0.1, angle 0
            Check(Near(inner.X, at.Cx + at.Hx, 1e-4f) && Near(inner.Z, at.Cz, 1e-4f) && Near(outer.X - inner.X, 0.01f, 1e-4f),
                  "deform: the inner wall on the fitted oval, the wall keeps its thickness");
            Vector3 innerSide = moved[2 * (10 * 24 + 6)]; // angle 90 deg: along z
            Check(Near(innerSide.Z, at.Cz + at.Hz, 1e-4f) && Near(innerSide.Y, 0.1f), "deform: across the fold the oval's other half-extent, heights unchanged");
            float widest = rings.Max(r => Math.Abs(r.Cx) + r.Hx) + 0.01f;
            Check(Near(SleeveShape.HalfWidth(moved), widest, 2e-3f), "deform: half width = the widest fitted ring plus the wall");
        }

        private static void LimbPointTests()
        {
            // Two legs (owners 0, 1) side by side and the spine (torso) between their tops.
            var segs = new List<Segment>
            {
                new Segment(new Vector3(-0.1f, 0, 0), new Vector3(-0.1f, 0.4f, 0), 0, 0.8f),
                new Segment(new Vector3(0.1f, 0, 0), new Vector3(0.1f, 0.4f, 0), 1, 0.8f),
                new Segment(new Vector3(0, 0.4f, 0), new Vector3(0, 0.8f, 0), -1),
            };
            var pts = new List<Vector3>
            {
                new Vector3(-0.15f, 0.2f, 0),  // left thigh skin
                new Vector3(-0.02f, 0.2f, 0),  // inner left thigh, nearer the left leg
                new Vector3(0.03f, 0.2f, 0),   // nearer the right leg
                new Vector3(-0.1f, 0.38f, 0.05f), // top of the left thigh (past MaxT)
                new Vector3(-0.3f, 0.2f, 0),   // too far out
                new Vector3(0, 0.5f, 0.1f),    // torso
            };
            var left = LimbPoints.Select(pts, segs, 0, 0.12f);
            Check(left.Count == 2 && left.Contains(pts[0]) && left.Contains(pts[1]),
                  "limb points: nearest limb only, within the window, not past the joint to the torso");
            var all = LimbPoints.SelectAll(pts, segs, new[] { 0.12f, 0.12f });
            Check(all[0].SequenceEqual(left) && all[1].SequenceEqual(LimbPoints.Select(pts, segs, 1, 0.12f)), "limb points: all owners in one pass, the same as one by one");
            var (d, t) = segs[0].Measure(new Vector3(-0.1f, -0.05f, 0));
            Check(Near(d, 0.05f) && t < 0f, "segment: distance clamped at the end, t before it negative");
        }
    }
}
