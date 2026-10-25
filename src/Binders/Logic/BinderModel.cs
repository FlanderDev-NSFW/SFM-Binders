using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Binders.Logic
{
    public enum BinderKind : byte { Arm = 0, Leg = 1 }

    /// <summary>One sleeve as exported (Blender coordinates, metres: x = her left, y = back, z = up along the sleeve).</summary>
    public sealed class BinderPart
    {
        public BinderKind Kind;
        public int Side;                 // 0 = her left, 1 = her right
        public Vector3 Bottom, Top;      // centres of the cup's lowest ring and of the opening
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Indices;

        /// <summary>Length of the sleeve along its axis (bottom ring to opening).</summary>
        public float Length => Vector3.Distance(Bottom, Top);

        /// <summary>The sleeve's widest reach to either side of its axis (across the fold), in the file's units.</summary>
        public float HalfWidth()
        {
            if (_halfWidth >= 0f) return _halfWidth;
            var (axis, fold) = Frame();
            Vector3 right = Vector3.Cross(fold, axis);
            float max = 0f;
            foreach (var p in Positions) max = Math.Max(max, Math.Abs(Vector3.Dot(p - Bottom, right)));
            return _halfWidth = max;
        }

        private float _halfWidth = -1f;

        /// <summary>
        /// The sleeve's own axes in the file: <c>Axis</c> from the cup up to the opening, <c>Fold</c> across it towards the folded
        /// part of the limb - the forearm lies in front of the upper arm (-y, the character faces -y), the shin behind the thigh (+y).
        /// </summary>
        public (Vector3 Axis, Vector3 Fold) Frame()
        {
            Vector3 axis = Vector3.Normalize(Top - Bottom);
            Vector3 fold = new Vector3(0f, Kind == BinderKind.Arm ? -1f : 1f, 0f);
            fold = Vector3.Normalize(fold - axis * Vector3.Dot(fold, axis));
            return (axis, fold);
        }

        /// <summary>
        /// Vertices in the sleeve's local frame for Unity: origin at <see cref="Bottom"/>, local y = the axis, z = the fold, x =
        /// z x y (Unity's right for <c>Quaternion.LookRotation(fold, axis)</c>). Going from Blender's right-handed axes to Unity's
        /// left-handed ones mirrors, so the triangles' winding is flipped.
        /// </summary>
        public (Vector3[] Positions, Vector3[] Normals, int[] Indices) ToLocal()
        {
            var (axis, fold) = Frame();
            Vector3 right = Vector3.Cross(fold, axis); // det(right, axis, fold) = -1: the mirror into Unity's handedness
            Vector3 L(Vector3 v) => new Vector3(Vector3.Dot(v, right), Vector3.Dot(v, axis), Vector3.Dot(v, fold));
            var pos = new Vector3[Positions.Length];
            var nrm = new Vector3[Normals.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = L(Positions[i] - Bottom);
            for (int i = 0; i < nrm.Length; i++) nrm[i] = L(Normals[i]);
            var idx = new int[Indices.Length];
            for (int i = 0; i < idx.Length; i += 3)
            {
                idx[i] = Indices[i];
                idx[i + 1] = Indices[i + 2];
                idx[i + 2] = Indices[i + 1];
            }
            return (pos, nrm, idx);
        }
    }

    /// <summary>The binders exported by tools/export_model.py (format described there).</summary>
    public sealed class BinderModel
    {
        public const uint Magic = 0x52444E42; // "BNDR"
        public List<BinderPart> Parts = new List<BinderPart>();

        public BinderPart Get(BinderKind kind, int side)
        {
            foreach (var p in Parts) if (p.Kind == kind && p.Side == side) return p;
            return null;
        }

        public static BinderModel Read(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a binders model (magic)");
            uint version = r.ReadUInt32();
            if (version != 1) throw new InvalidDataException("unsupported model version " + version);
            var m = new BinderModel();
            uint count = r.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                var p = new BinderPart { Kind = (BinderKind)r.ReadByte(), Side = r.ReadByte(), Bottom = V(r), Top = V(r) };
                if ((byte)p.Kind > 1 || p.Side > 1) throw new InvalidDataException("bad part header");
                int n = checked((int)r.ReadUInt32());
                p.Positions = new Vector3[n];
                p.Normals = new Vector3[n];
                p.Uvs = new Vector2[n];
                for (int k = 0; k < n; k++) p.Positions[k] = V(r);
                for (int k = 0; k < n; k++) p.Normals[k] = V(r);
                for (int k = 0; k < n; k++) p.Uvs[k] = new Vector2(r.ReadSingle(), r.ReadSingle());
                int ni = checked((int)r.ReadUInt32());
                if (ni % 3 != 0) throw new InvalidDataException("index count is not a multiple of 3");
                p.Indices = new int[ni];
                for (int k = 0; k < ni; k++)
                {
                    uint idx = r.ReadUInt32();
                    if (idx >= n) throw new InvalidDataException("index out of range");
                    p.Indices[k] = (int)idx;
                }
                if (p.Length < 1e-3f) throw new InvalidDataException("sleeve without length");
                m.Parts.Add(p);
            }
            foreach (BinderKind k in new[] { BinderKind.Arm, BinderKind.Leg })
                for (int s = 0; s < 2; s++)
                    if (m.Get(k, s) == null) throw new InvalidDataException(k + " sleeve " + s + " missing");
            return m;
        }

        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
