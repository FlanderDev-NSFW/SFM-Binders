using System;
using System.Collections.Generic;
using System.IO;
using Binders.Logic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Binders
{
    /// <summary>
    /// The four sleeves: one object each, its mesh in the sleeve's local frame (<see cref="BinderPart.ToLocal"/>: origin at the
    /// cup, y up the sleeve, z towards the folded segment), textured with the model's colour map on a copy of the game's handcuff
    /// material (UnityChan toon shader), so they are lit and outlined like the game's own items.
    /// </summary>
    internal sealed class BinderRig
    {
        private readonly BinderModel _model;
        private GameObject _root;
        private readonly Dictionary<(BinderKind, int), Transform> _sleeves = new Dictionary<(BinderKind, int), Transform>();
        private readonly Dictionary<(BinderKind, int), Mesh> _meshes = new Dictionary<(BinderKind, int), Mesh>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(); // meshes, material, texture

        public BinderRig(BinderModel model) { _model = model; }

        public bool Built => _root != null;

        /// <summary>The sleeves' material (diagnostics copy it).</summary>
        public Material Leather { get; private set; }

        public void Build(int layer)
        {
            Destroy();
            _root = new GameObject("Binders");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            Material mat = MakeMaterial();
            Leather = mat;
            foreach (var part in _model.Parts)
            {
                var g = new GameObject(part.Kind + (part.Side == 0 ? "L" : "R"));
                g.layer = layer;
                g.transform.SetParent(_root.transform, false);
                _sleeves[(part.Kind, part.Side)] = g.transform;
                var (pos, nrm, idx) = part.ToLocal();
                var mesh = new Mesh { name = "Binders." + g.name };
                if (pos.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                var v = new Vector3[pos.Length];
                var n = new Vector3[nrm.Length];
                var uv = new Vector2[part.Uvs.Length];
                for (int i = 0; i < v.Length; i++) v[i] = BinderPoser.U(pos[i]);
                for (int i = 0; i < n.Length; i++) n[i] = BinderPoser.U(nrm[i]);
                for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(part.Uvs[i].X, part.Uvs[i].Y);
                mesh.vertices = v;
                mesh.normals = n;
                mesh.uv = uv;
                mesh.triangles = idx;
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives map changes (UnloadUnusedAssets)
                _owned.Add(mesh);
                _meshes[(part.Kind, part.Side)] = mesh;
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = g.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            _root.SetActive(false);
        }

        /// <summary>Put a sleeve with its cup at <paramref name="cup"/>, its axis along <paramref name="axis"/> and its fold side
        /// towards <paramref name="fold"/>, scaled by <paramref name="scale"/>.</summary>
        public void Place(BinderKind kind, int side, NVector3 cup, NVector3 axis, NVector3 fold, float scale)
        {
            if (!_sleeves.TryGetValue((kind, side), out Transform t)) return;
            t.SetPositionAndRotation(BinderPoser.U(cup), Quaternion.LookRotation(BinderPoser.U(fold), BinderPoser.U(axis)));
            t.localScale = Vector3.one * scale;
        }

        /// <summary>A sleeve's shape: its vertices in its local frame (fitted to her, or as modelled).</summary>
        public void SetShape(BinderKind kind, int side, NVector3[] local)
        {
            if (!_meshes.TryGetValue((kind, side), out Mesh mesh)) return;
            var v = new Vector3[local.Length];
            for (int i = 0; i < v.Length; i++) v[i] = BinderPoser.U(local[i]);
            mesh.vertices = v;
            mesh.RecalculateBounds();
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>The game's handcuff material (name "tejyou..."), found among the loaded materials [observed 2026-10-11, SpreaderBar].</summary>
        private static Material FindHandcuffMaterial()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>());
                foreach (var o in all)
                {
                    var mat = o.TryCast<Material>();
                    if (mat != null && mat.name != null && mat.name.StartsWith("tejyou", StringComparison.Ordinal)) return mat;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("handcuff material lookup failed: " + e.Message); }
            Plugin.Logger.LogWarning("handcuff material not found; using a toon/standard shader");
            return null;
        }

        private Material MakeMaterial()
        {
            Material source = FindHandcuffMaterial();
            Material mat;
            if (source != null) mat = new Material(source);
            else
            {
                Shader shader = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
            }
            mat.name = "Binders.Leather";
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Texture2D tex = LoadTexture();
            Texture main = tex != null ? tex : Texture2D.whiteTexture;
            foreach (string name in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                if (mat.HasProperty(name)) mat.SetTexture(name, main);
            // Without the texture: the leather's red.
            Color baseColor = tex != null ? Color.white : new Color(0.68f, 0f, 0.02f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_1st_ShadeColor")) mat.SetColor("_1st_ShadeColor", baseColor * 0.7f);
            if (mat.HasProperty("_2nd_ShadeColor")) mat.SetColor("_2nd_ShadeColor", baseColor * 0.45f);
            _owned.Add(mat);
            return mat;
        }

        /// <summary>The colour map embedded in the DLL (PNG, 1024 px); null if it cannot be decoded.</summary>
        private Texture2D LoadTexture()
        {
            try
            {
                using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(Plugin.TextureResource);
                if (s == null) throw new FileNotFoundException("embedded resource " + Plugin.TextureResource + " missing");
                var buffer = new MemoryStream();
                s.CopyTo(buffer);
                byte[] bytes = buffer.ToArray();
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Binders.Color" };
                if (!ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes), true)) throw new InvalidDataException("not a PNG");
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.wrapMode = TextureWrapMode.Clamp;
                _owned.Add(tex);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] texture: " + tex.width + " x " + tex.height);
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("binders texture could not be loaded, plain red instead: " + e.Message);
                return null;
            }
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _sleeves.Clear();
            _meshes.Clear();
            foreach (var o in _owned) if (o != null) UnityEngine.Object.Destroy(o);
            _owned.Clear();
        }
    }
}
