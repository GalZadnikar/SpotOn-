using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ExpandPuzzle.EditorTools
{
    /// <summary>
    /// Draws the few simple white UI sprites the HUD needs (circle, rounded panel, star, icons) and
    /// saves them as PNGs in Assets/_Game/UI. Existing files are kept, so they can be replaced with real art.
    /// </summary>
    public static class UiSpriteGen
    {
        public const string Dir = "Assets/_Game/UI";

        public static Sprite Circle() { return Ensure("ui_circle", 128, 0, (x, y) => x * x + y * y <= 0.97f * 0.97f); }

        public static Sprite Rounded()
        {
            const float r = 0.56f;
            return Ensure("ui_rounded", 96, 30, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                if (ax > 0.98f || ay > 0.98f) return false;
                float cx = ax - (0.98f - r), cy = ay - (0.98f - r);
                if (cx <= 0f || cy <= 0f) return true;
                return cx * cx + cy * cy <= r * r;
            });
        }

        public static Sprite Star()
        {
            // Chunky five-pointed star, one point up.
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = (90f + i * 36f) * Mathf.Deg2Rad;
                float rad = (i % 2 == 0) ? 0.97f : 0.52f;
                pts[i] = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad - 0.04f);
            }
            return Ensure("ui_star", 128, 0, (x, y) => InPolygon(pts, x, y));
        }

        /// <summary>Counter-clockwise curved arrow.</summary>
        public static Sprite IconUndo() { return Ensure("ui_icon_undo", 128, 0, (x, y) => ArcArrow(x, y, -60f, 150f, true)); }

        /// <summary>Clockwise arrow going almost all the way round.</summary>
        public static Sprite IconRestart() { return Ensure("ui_icon_restart", 128, 0, (x, y) => ArcArrow(x, y, 100f, 400f, false)); }

        // ------------------------------------------------------------ shapes

        static bool ArcArrow(float x, float y, float fromDeg, float toDeg, bool headAtEnd)
        {
            const float r0 = 0.44f, r1 = 0.66f;
            float mid = (r0 + r1) * 0.5f;

            float r = Mathf.Sqrt(x * x + y * y);
            if (r >= r0 && r <= r1)
            {
                float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                while (a < fromDeg) a += 360f;
                while (a >= fromDeg + 360f) a -= 360f;
                if (a <= toDeg) return true;
            }

            // Arrow head at one end of the arc, pointing along the direction of travel.
            float ha = (headAtEnd ? toDeg : fromDeg) * Mathf.Deg2Rad;
            Vector2 radial = new Vector2(Mathf.Cos(ha), Mathf.Sin(ha));
            Vector2 tangent = headAtEnd ? new Vector2(-radial.y, radial.x) : new Vector2(radial.y, -radial.x);
            Vector2 p = radial * mid;
            var tri = new[] { p + tangent * 0.46f, p + radial * 0.34f, p - radial * 0.34f };
            return InPolygon(tri, x, y);
        }

        static bool InPolygon(Vector2[] poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > y) != (poly[j].y > y) &&
                    x < (poly[j].x - poly[i].x) * (y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        // ------------------------------------------------------------ plumbing

        /// <param name="inside">Shape test in a -1..1 square (y up).</param>
        static Sprite Ensure(string name, int size, int border, Func<float, float, bool> inside)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game")) AssetDatabase.CreateFolder("Assets", "_Game");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game", "UI");

            string path = Dir + "/" + name + ".png";
            if (!File.Exists(path))
            {
                const int ss = 4;   // samples per pixel side, for smooth edges
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int py = 0; py < size; py++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < ss; sy++)
                            for (int sx = 0; sx < ss; sx++)
                            {
                                float x = ((px + (sx + 0.5f) / ss) / size) * 2f - 1f;
                                float y = ((py + (sy + 0.5f) / ss) / size) * 2f - 1f;
                                if (inside(x, y)) hits++;
                            }
                        pixels[py * size + px] = new Color32(255, 255, 255, (byte)(hits * 255 / (ss * ss)));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.spriteBorder = new Vector4(border, border, border, border);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
