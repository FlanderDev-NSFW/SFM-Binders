using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Binders.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Binders
{
    /// <summary>
    /// Shapes each sleeve to the limb inside it (<see cref="SleeveShape"/>): her body mesh is baked at render time, split by
    /// limb (<see cref="LimbPoints"/>), and every sleeve's inner wall follows that limb's outline ring by ring, with
    /// Model.ClearanceCm to spare. Also how deep the joint sits in the cup (her elbow / knee resting on the cup's floor) and how
    /// wide each sleeve ends up (the knees move apart for it).
    /// The bake's vertices need her body object's scale: BakeMesh(mesh, true), then position + rotation * (lossyScale x vertex). Her
    /// body object sits under Manustia, scaled 1.3125 by her height setting; without that scale (position + rotation * vertex, as
    /// SpreaderBar does) the bake came out 24 % small and pulled towards her feet (the sleeves fitted to it were far too tight and
    /// off her limbs). With it, a mesh of the bake drawn over her (BinderController.DebugGhost) covers her drawn body exactly
    /// [observed 2026-10-25]; baked in onPreCull, onPreRender or at the end of LateUpdate made no difference.
    /// </summary>
    internal sealed class BinderFitter
    {
        private readonly SleeveShape[] _shapes = new SleeveShape[4];
        private Mesh _bake;
        private SkinnedMeshRenderer _body;
        private readonly List<NVector3> _points = new List<NVector3>();
        public string Status = "not fitted";

        // Around each limb: how far from its bone line skin can be (m), and where along the segments it stops belonging to the
        // sleeve (near the shoulder / hip the skin is the torso's; the wrist and hand rest outside on the shoulder).
        private const float ArmWindow = 0.1f, LegWindow = 0.15f;
        private const float MinHalf = 0.012f; // m: a ring never narrower than this
        private const float MaxGrow = 3f;     // a ring never wider than this times the model's
        private const int MinPoints = 25;     // skin points a ring needs to count (fewer: the ring below)

        public BinderFitter(BinderModel model)
        {
            for (int i = 0; i < BinderPoser.Sleeves.Length; i++)
            {
                var b = BinderPoser.Sleeves[i];
                var part = model.Get(b.Kind, b.Side);
                var (pos, nrm, _) = part.ToLocal();
                _shapes[i] = SleeveShape.From(pos, nrm, part.Length);
            }
        }

        /// <summary>The sleeves as modelled again.</summary>
        public void Reset(BinderRig rig)
        {
            for (int i = 0; i < _shapes.Length; i++) rig.SetShape(BinderPoser.Sleeves[i].Kind, BinderPoser.Sleeves[i].Side, _shapes[i].Local);
            foreach (var h in _history) h.Clear();
            _next = -1;
            _byLimb = null;
            Status = "not fitted";
        }

        /// <summary>Fits kept per sleeve: the sleeve covers them all (the last <see cref="HistoryFits"/> x the fit interval).</summary>
        public const int HistoryFits = 6;
        private readonly Queue<(Ring[] Rings, float Cup)>[] _history =
            { new Queue<(Ring[], float)>(), new Queue<(Ring[], float)>(), new Queue<(Ring[], float)>(), new Queue<(Ring[], float)>() };
        public float LastMs; // diagnostics: the longest step of the last fit (ms)

        // A fit runs over five frames - the bake, then one sleeve per frame - so no frame takes more than a few ms (all at once
        // it took 7.5 ms) [observed 2026-10-25]. Everything a sleeve needs is taken at the bake, so the steps after it see the
        // body as baked.
        private int _next = -1;                      // the sleeve the next Step fits; -1 = no fit under way
        private List<NVector3>[] _byLimb;
        private readonly (NVector3 J, NVector3 Axis, NVector3 Fold)[] _frames = new (NVector3, NVector3, NVector3)[4];
        private readonly float[] _scales = new float[4];
        private readonly string[] _parts = new string[4];

        public bool Busy => _next >= 0;

        // Which limb each vertex of her body belongs to, decided once in a pose where that is clear. Classified in the crawl, skin
        // near a limb went to it - her breasts to the thighs that came close to them, the sides of her chest and hips to the upper
        // arms and thighs - and the sleeves grew around them [observed 2026-10-25]. The bake keeps the mesh's vertex order, so the
        // labels hold in every pose.
        private int[] _labels;

        public bool HasLabels => _labels != null;

        public void ForgetLabels() => _labels = null;

        /// <summary>
        /// LateUpdate, before the pose: for one moment her arms straight out to the sides and her legs straight down and apart
        /// (from her spine), bake, label each vertex by its nearest limb, and put her bones back - nothing of it is drawn.
        /// </summary>
        public void Label(Animator anim, BinderRig rig)
        {
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var moved = new[] {
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg };
            var saved = new Quaternion[moved.Length];
            for (int k = 0; k < moved.Length; k++) saved[k] = B(moved[k]).localRotation;
            try
            {
                Transform hips = B(HumanBodyBones.Hips), neck = B(HumanBodyBones.Neck);
                Vector3 up = (neck.position - hips.position).normalized;
                Vector3 across = B(HumanBodyBones.RightUpperArm).position - B(HumanBodyBones.LeftUpperArm).position;
                Vector3 toRight = (across - up * Vector3.Dot(across, up)).normalized;
                void Straight(HumanBodyBones a, HumanBodyBones b, HumanBodyBones c, Vector3 dir)
                {
                    BinderPoser.Aim(B(a), B(b), B(a).position + dir);
                    BinderPoser.Aim(B(b), B(c), B(b).position + dir);
                }
                Straight(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -toRight);
                Straight(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, toRight);
                var lf = B(HumanBodyBones.LeftMiddleProximal);
                var rf = B(HumanBodyBones.RightMiddleProximal);
                if (lf != null) BinderPoser.Aim(B(HumanBodyBones.LeftHand), lf, B(HumanBodyBones.LeftHand).position - toRight);
                if (rf != null) BinderPoser.Aim(B(HumanBodyBones.RightHand), rf, B(HumanBodyBones.RightHand).position + toRight);
                Vector3 legL = (-up * 0.9f - toRight * 0.42f).normalized, legR = (-up * 0.9f + toRight * 0.42f).normalized; // 25 deg out
                Straight(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, legL);
                Straight(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, legR);
                if (!Bake(anim, rig)) return;
                _labels = LimbPoints.Classify(_points, Segments(anim), new[] { ArmWindow, ArmWindow, LegWindow, LegWindow });
                int[] count = new int[4];
                foreach (int o in _labels) if (o >= 0) count[o]++;
                LabelStatus = "labels: arms " + count[0] + " / " + count[1] + ", legs " + count[2] + " / " + count[3] + " of " + _labels.Length + " vertices";
            }
            finally
            {
                for (int k = 0; k < moved.Length; k++) B(moved[k]).localRotation = saved[k];
            }
        }

        public string LabelStatus = "no labels";

        /// <summary>Her skeleton as segments: the four sleeves' limbs (owners 0 .. 3; near the shoulder / hip the skin is the
        /// torso's, the hand rests outside) and the rest of her (-1).</summary>
        private static List<Segment> Segments(Animator anim)
        {
            NVector3 N(HumanBodyBones b) => BinderPoser.N(anim.GetBoneTransform(b).position);
            var segs = new List<Segment>
            {
                new Segment(N(HumanBodyBones.LeftLowerArm), N(HumanBodyBones.LeftUpperArm), 0, 0.85f),
                new Segment(N(HumanBodyBones.LeftLowerArm), N(HumanBodyBones.LeftHand), 0, 0.9f),
                new Segment(N(HumanBodyBones.RightLowerArm), N(HumanBodyBones.RightUpperArm), 1, 0.85f),
                new Segment(N(HumanBodyBones.RightLowerArm), N(HumanBodyBones.RightHand), 1, 0.9f),
                new Segment(N(HumanBodyBones.LeftLowerLeg), N(HumanBodyBones.LeftUpperLeg), 2, 0.8f),
                new Segment(N(HumanBodyBones.LeftLowerLeg), N(HumanBodyBones.LeftFoot), 2),
                new Segment(N(HumanBodyBones.RightLowerLeg), N(HumanBodyBones.RightUpperLeg), 3, 0.8f),
                new Segment(N(HumanBodyBones.RightLowerLeg), N(HumanBodyBones.RightFoot), 3),
                new Segment(N(HumanBodyBones.Hips), N(HumanBodyBones.Neck), -1),
                new Segment(N(HumanBodyBones.Neck), N(HumanBodyBones.Head), -1),
                new Segment(N(HumanBodyBones.LeftUpperArm), N(HumanBodyBones.RightUpperArm), -1),
                new Segment(N(HumanBodyBones.LeftUpperLeg), N(HumanBodyBones.RightUpperLeg), -1),
            };
            void Hand(HumanBodyBones wrist, HumanBodyBones finger)
            {
                var f = anim.GetBoneTransform(finger);
                if (f != null) segs.Add(new Segment(N(wrist), BinderPoser.N(f.position), -1));
            }
            Hand(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal);
            Hand(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);
            return segs;
        }

        /// <summary>Start a fit: bake her body, split it by limb (the labels), note each sleeve's frame and scale.</summary>
        public bool Begin(Animator anim, Transform root, BinderPoser.Sizes sizes, BinderRig rig)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (!Bake(anim, rig)) { Status = "no body mesh"; return false; }
            if (_labels == null || _labels.Length != _points.Count) { Status = "no labels (her body mesh changed?)"; _labels = null; return false; }
            _byLimb = new List<NVector3>[4];
            for (int o = 0; o < 4; o++) _byLimb[o] = new List<NVector3>();
            for (int k = 0; k < _labels.Length; k++) if (_labels[k] >= 0) _byLimb[_labels[k]].Add(_points[k]);
            for (int i = 0; i < _frames.Length; i++)
            {
                _frames[i] = BinderPoser.Frame(anim, root, i);
                _scales[i] = sizes.Scale[i];
            }
            _next = 0;
            LastMs = (float)watch.Elapsed.TotalMilliseconds;
            return true;
        }

        /// <summary>Fit the next sleeve of the fit under way; true once the last one is done.</summary>
        public bool Step(BinderPoser.Sizes sizes, BinderRig rig, float clearance)
        {
            if (_next < 0) return false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FitSleeve(_next, sizes, rig, clearance);
            LastMs = Math.Max(LastMs, (float)watch.Elapsed.TotalMilliseconds);
            if (++_next < _shapes.Length) return false;
            _next = -1;
            _byLimb = null;
            Status = string.Concat(_parts);
            return true;
        }

        private void FitSleeve(int i, BinderPoser.Sizes sizes, BinderRig rig, float clearance)
        {
            var b = BinderPoser.Sleeves[i];
            string name = b.Kind + (b.Side == 0 ? "L" : "R");
            var pts = _byLimb[i];
            if (pts.Count < 50) { _parts[i] = name + ": " + pts.Count + " points; "; return; }
            var (j, axis, fold) = _frames[i];
            NVector3 right = NVector3.Cross(axis, fold); // the sleeve's local x (Quaternion.LookRotation(fold, axis))
            float scale = _scales[i];
            // The joint rests on the cup's floor: its lowest skin there, plus the clearance, above the inner floor.
            float low = float.MaxValue;
            foreach (var p in pts) low = Math.Min(low, NVector3.Dot(p - j, axis));
            float cup = -low + clearance + _shapes[i].InnerBottom * scale;
            var local = new List<NVector3>(pts.Count);
            foreach (var p in pts)
            {
                NVector3 d = p - j;
                local.Add(new NVector3(NVector3.Dot(d, right), NVector3.Dot(d, axis) + cup, NVector3.Dot(d, fold)) / scale);
            }
            var rings = _shapes[i].FitRings(local, clearance / scale, MinHalf / scale, MaxGrow, MinPoints);
            if (rings == null) { _parts[i] = name + ": no rings; "; return; }
            // Over the last few fits (the crawl moves her limbs inside the sleeves; one moment's fit let skin show at the next)
            // [observed 2026-10-25].
            var history = _history[i];
            history.Enqueue((rings, cup));
            while (history.Count > HistoryFits) history.Dequeue();
            rings = SleeveShape.Union(history.Select(h => h.Rings));
            cup = history.Max(h => h.Cup);
            var verts = _shapes[i].Deform(rings);
            rig.SetShape(b.Kind, b.Side, verts);
            sizes.Cup[i] = cup;
            sizes.Half[i] = SleeveShape.HalfWidth(verts) * scale;
            sizes.Fitted[i] = true;
            Ring top = rings[rings.Length - 1], mid = rings[rings.Length / 2];
            _parts[i] = name + ": " + pts.Count + " points, cup " + Cm(cup) + ", opening " + Cm(top.Hx * scale) + " x " + Cm(top.Hz * scale)
                        + ", middle " + Cm(mid.Hx * scale) + " x " + Cm(mid.Hz * scale) + " cm half; ";
        }

        private static string Cm(float m) => (m * 100f).ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>Her body mesh as drawn now, in world space (see the class notes on the scale).</summary>
        private bool Bake(Animator anim, BinderRig rig)
        {
            if (_body == null)
                foreach (var r in anim.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                    if (r != null && r.name == "Selestia_body") _body = r;
            if (_body == null || !_body.enabled) return false;
            if (_bake == null) _bake = new Mesh { name = "Binders.BodyBake", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _body.BakeMesh(_bake, true);
            var verts = _bake.vertices;
            Transform t = _body.transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            _points.Clear();
            // System.Numerics: Unity's vector maths goes through the game's runtime per call (18 600 vertices).
            NVector3 np = BinderPoser.N(pos), ns = BinderPoser.N(t.lossyScale);
            var nr = new System.Numerics.Quaternion(rot.x, rot.y, rot.z, rot.w);
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                _points.Add(np + NVector3.Transform(new NVector3(v.x, v.y, v.z) * ns, nr));
            }
            ShowGhost(pos, rot, rig.Leather);
            return _points.Count > 0;
        }

        private GameObject _ghost; // BinderController.DebugGhost


        private void ShowGhost(Vector3 pos, Quaternion rot, Material like)
        {
            if (!BinderController.DebugGhost)
            {
                if (_ghost != null) { UnityEngine.Object.Destroy(_ghost); _ghost = null; }
                return;
            }
            if (_ghost == null)
            {
                _ghost = new GameObject("Binders.Ghost");
                _ghost.AddComponent<MeshFilter>();
                var r = _ghost.AddComponent<MeshRenderer>();
                r.sharedMaterial = like;
            }
            var copy = new Mesh { name = "Binders.GhostMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            copy.vertices = _bake.vertices;
            copy.triangles = _bake.triangles;
            copy.RecalculateNormals();
            copy.RecalculateBounds();
            var mf = _ghost.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
            mf.sharedMesh = copy;
            _ghost.transform.SetPositionAndRotation(pos, rot);
            _ghost.transform.localScale = _body.transform.lossyScale;
            _ghost.layer = _body.GetComponentInParent<Animator>().gameObject.layer; // the sleeves' layer (drawn by the main camera)
        }

        public void Destroy()
        {
            if (_ghost != null) UnityEngine.Object.Destroy(_ghost);
            if (_bake != null) UnityEngine.Object.Destroy(_bake);
            _bake = null;
        }
    }
}
