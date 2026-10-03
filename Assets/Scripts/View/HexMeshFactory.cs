using Catan.Core;
using UnityEngine;

namespace Catan.View
{
    /// <summary>Builds a hex prism mesh whose corners come straight from the Core layout, so view and data can't disagree.</summary>
    public static class HexMeshFactory
    {
        /// <summary>
        /// Pointy-top hex prism centered on the origin, base at y = 0 and top at y = height.
        /// <paramref name="gap"/> (0..1) shrinks the hex so neighboring tiles show a thin seam.
        /// </summary>
        public static Mesh CreatePrism(float size, float gap, float height)
        {
            var corners = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                var p = HexLayout.ToPlane(Vertex.OfCorner(Hex.Zero, i), size * (1f - gap));
                corners[i] = new Vector3(p.X, 0f, -p.Y); // plane y points down the screen; world z points up it
            }

            var verts = new Vector3[7 + 6 * 4];
            var normals = new Vector3[verts.Length];
            var tris = new int[6 * 3 + 6 * 6];
            Vector3 up = Vector3.up;
            Vector3 rise = Vector3.up * height;

            // Top cap: fan around the center. Corners run clockwise seen from above, which is Unity's front face.
            verts[0] = rise;
            normals[0] = up;
            for (int i = 0; i < 6; i++)
            {
                verts[1 + i] = corners[i] + rise;
                normals[1 + i] = up;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + i;
                tris[i * 3 + 2] = 1 + (i + 1) % 6;
            }

            // Side walls: one flat-shaded quad per side.
            int v = 7;
            int t = 18;
            for (int i = 0; i < 6; i++)
            {
                Vector3 ci = corners[i];
                Vector3 cj = corners[(i + 1) % 6];
                Vector3 outward = ((ci + cj) * 0.5f).normalized;

                verts[v] = ci;               // bottom i
                verts[v + 1] = ci + rise;    // top i
                verts[v + 2] = cj + rise;    // top j
                verts[v + 3] = cj;           // bottom j
                for (int k = 0; k < 4; k++) normals[v + k] = outward;

                tris[t++] = v;
                tris[t++] = v + 2;
                tris[t++] = v + 1;
                tris[t++] = v;
                tris[t++] = v + 3;
                tris[t++] = v + 2;
                v += 4;
            }

            var mesh = new Mesh { name = "HexPrism", vertices = verts, normals = normals, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
