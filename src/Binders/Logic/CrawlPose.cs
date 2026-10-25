using System;
using System.Numerics;

namespace Binders.Logic
{
    /// <summary>
    /// The crawl on elbows and knees, as directions and points (any right- or left-handed frame: only dot products and lengths are
    /// used). The game's crawl puts her on hands and knees with her back level [observed 2026-10-25:
    /// shoulders 46 cm, hips 42 cm over the floor, hands under the shoulders, shins flat behind the knees]; in the binders the
    /// forearms are folded against the upper arms and the shins against the thighs, so the chest has to come down until the
    /// elbows reach the floor.
    /// </summary>
    public static class CrawlPose
    {
        /// <summary>
        /// Where the shoulders' midpoint has to go: <paramref name="shoulders"/> turned about <paramref name="pivot"/> (the spine
        /// joint) in the vertical plane so it is <paramref name="height"/> over the floor (<paramref name="floor"/>, along
        /// <paramref name="up"/>). Only ever lowered; never past straight ahead of the pivot level with the floor height asked.
        /// Returns the new shoulders' point.
        /// </summary>
        public static Vector3 LowerShoulders(Vector3 pivot, Vector3 shoulders, Vector3 floor, Vector3 up, float height)
        {
            Vector3 v = shoulders - pivot;
            float now = Vector3.Dot(shoulders - floor, up);
            if (now <= height) return shoulders; // already low enough
            float u = Vector3.Dot(v, up);
            float want = u - (now - height);     // the pivot-relative height asked for
            Vector3 flat = v - up * u;           // the rest (forward and sideways) - lengthened to keep the distance
            float r2 = v.LengthSquared();
            if (want * want > r2) want = -MathF.Sqrt(r2) * 0.999f; // out of reach: as low as the spine can turn
            float flatLen = flat.Length();
            if (flatLen < 1e-6f) return shoulders;
            float newFlat = MathF.Sqrt(Math.Max(r2 - want * want, 0f));
            return pivot + flat * (newFlat / flatLen) + up * want;
        }

        /// <summary>
        /// The elbow's place for a crawl step: below <paramref name="shoulder"/> on the floor (its joint <paramref name="elbowHeight"/>
        /// over it), moved horizontally by <paramref name="swing"/> (from the animation: hand offset, scaled), shortened to at
        /// most <paramref name="maxSwing"/> of the upper arm's length. Returns the point and the shoulder height that lets the
        /// upper arm (<paramref name="upperArm"/> long) reach it.
        /// </summary>
        public static (Vector3 Offset, float Drop) ElbowStep(Vector3 swing, Vector3 up, float upperArm, float maxSwing)
        {
            Vector3 flat = swing - up * Vector3.Dot(swing, up);
            float len = flat.Length(), max = upperArm * maxSwing;
            if (len > max) flat *= max / len;
            float drop = MathF.Sqrt(Math.Max(upperArm * upperArm - flat.LengthSquared(), 0f));
            return (flat, drop);
        }

        /// <summary>The folded segment's direction: <paramref name="along"/> (joint to the segment it folds against, e.g. elbow to
        /// shoulder) turned by <paramref name="gapRad"/> towards <paramref name="side"/> (the side it lies on).</summary>
        public static Vector3 Fold(Vector3 along, Vector3 side, float gapRad)
        {
            Vector3 a = Vector3.Normalize(along);
            Vector3 s = side - a * Vector3.Dot(side, a);
            if (s.LengthSquared() < 1e-10f) return a;
            s = Vector3.Normalize(s);
            return a * MathF.Cos(gapRad) + s * MathF.Sin(gapRad);
        }

        /// <summary>
        /// A sleeve on a folded limb: its axis halfway between the two segments (joint to the upper segment's far end and joint to
        /// the folded one's end) and its fold direction from the upper segment towards the folded one; <paramref name="fallback"/>
        /// when the two lie on each other.
        /// </summary>
        public static (Vector3 Axis, Vector3 Fold) Sleeve(Vector3 joint, Vector3 upperEnd, Vector3 foldedEnd, Vector3 fallback)
        {
            Vector3 a = Vector3.Normalize(upperEnd - joint), b = Vector3.Normalize(foldedEnd - joint);
            Vector3 axis = a + b;
            axis = axis.LengthSquared() < 1e-8f ? a : Vector3.Normalize(axis);
            Vector3 fold = b - a;
            fold -= axis * Vector3.Dot(fold, axis);
            if (fold.LengthSquared() < 1e-6f)
            {
                fold = fallback - axis * Vector3.Dot(fallback, axis);
                if (fold.LengthSquared() < 1e-8f) fold = Vector3.Cross(axis, Vector3.UnitX);
            }
            return (axis, Vector3.Normalize(fold));
        }
    }
}
