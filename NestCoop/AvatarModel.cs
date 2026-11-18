using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace NestCoop;

/// <summary>
/// The floating-head avatar model (0.9.8). Modelled in art/avatar-head.blend, exported by art/export_avatar.py to
/// NestCoop/Assets/avatar-head.mesh.txt (embedded in the dll). Text format: "g material r g b metallic smoothness"
/// starts a colour group, "t x y z x y z x y z" is one triangle in Unity coordinates; origin = eye level.
/// Each group becomes one Mesh (flat shaded: every triangle has its own three vertices, normals recalculated);
/// the "Band" group is tinted with the player's colour. Meshes and the untinted materials are built once and shared;
/// they're marked DontUnloadUnusedAsset so a scene change can't unload them while no avatar uses them.
/// </summary>
internal static class AvatarModel
{
    const string Resource = "NestCoop.avatar-head.mesh.txt";
    public const string TintGroup = "Band";

    sealed class Group
    {
        public string Name;
        public Color Color;
        public float Metallic, Smoothness;
        public Mesh Mesh;
        public Material Shared; // null for the tinted group
    }

    static List<Group> groups;
    static bool tried;

    /// <summary>Build the head under <paramref name="parent"/> (local origin = eye level). False if the model is unavailable.</summary>
    public static bool Build(Transform parent, Color tint, Func<Color, float, float, Material> makeMaterial)
    {
        if (!Load(makeMaterial)) return false;
        foreach (var g in groups)
        {
            var go = new GameObject($"Head {g.Name}");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = g.Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = g.Shared ?? makeMaterial(tint, g.Metallic, g.Smoothness);
        }
        return true;
    }

    static bool Load(Func<Color, float, float, Material> makeMaterial)
    {
        if (groups != null) return true;
        if (tried) return false;
        tried = true;
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
            if (s == null) { Mod.Log.Warning($"AvatarModel: resource {Resource} missing; using capsules"); return false; }
            using var reader = new StreamReader(s);
            var parsed = new List<(Group g, List<Vector3> verts)>();
            List<Vector3> cur = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split(' ');
                if (p[0] == "g")
                {
                    var g = new Group { Name = p[1], Color = new Color(F(p[2]), F(p[3]), F(p[4])), Metallic = F(p[5]), Smoothness = F(p[6]) };
                    cur = new List<Vector3>();
                    parsed.Add((g, cur));
                }
                else if (p[0] == "t" && cur != null && p.Length == 10)
                    for (int i = 0; i < 3; i++) cur.Add(new Vector3(F(p[1 + i * 3]), F(p[2 + i * 3]), F(p[3 + i * 3])));
            }

            var result = new List<Group>();
            int tris = 0;
            foreach (var (g, verts) in parsed)
            {
                if (verts.Count == 0) continue;
                var idx = new int[verts.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                var mesh = new Mesh { name = $"NestCoop head {g.Name}" };
                mesh.vertices = verts.ToArray();
                mesh.triangles = idx;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                g.Mesh = mesh;
                if (g.Name != TintGroup)
                {
                    g.Shared = makeMaterial(g.Color, g.Metallic, g.Smoothness);
                    g.Shared.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                result.Add(g);
                tris += verts.Count / 3;
            }
            groups = result;
            Mod.Log.Msg($"AvatarModel: head loaded ({result.Count} parts, {tris} triangles)");
            return true;
        }
        catch (Exception e)
        {
            Mod.Log.Error($"AvatarModel: couldn't load the head model, using capsules: {e}");
            return false;
        }
    }

    static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
