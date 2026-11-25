using System.Collections.Generic;
using UnityEngine;

namespace ExpandPuzzle
{
    /// <summary>Colours of the board for one stretch of levels.</summary>
    [System.Serializable]
    public struct BoardPalette
    {
        public string name;
        [Tooltip("Top surface and background - they are one piece.")]
        public Color surface;
        [Tooltip("Bottom of the pit.")]
        public Color pit;
        [Tooltip("The rounded floor tiles.")]
        public Color pad;

        public BoardPalette(string name, int surface, int pit, int pad)
        {
            this.name = name;
            this.surface = MaterialFactory.Hex(surface);
            this.pit = MaterialFactory.Hex(pit);
            this.pad = MaterialFactory.Hex(pad);
        }
    }

    /// <summary>
    /// All materials and colours the game draws with. The scene builder fills the material slots with
    /// Toony Colors Pro assets (Assets/_Game/Materials) so the look can be tuned in the inspector.
    /// Any slot left empty gets a default material created at runtime.
    /// At runtime the game works on copies, so recolouring never touches the assets.
    /// </summary>
    [System.Serializable]
    public class GameTheme
    {
        [Header("Board")]
        public Material boardSurface;
        public Material boardPit;
        public Material floorPad;
        public Material target;
        public Material targetMark;
        public Material forbidden;
        public Material forbiddenMark;

        [Header("Blob")]
        public Material blob;
        public Material eye;
        public Material eyeShine;

        [Header("Pump")]
        public Material pumpBody;
        public Material pumpNozzle;
        public Material pumpKnob;

        [Header("Marks pressed into the blob (badgeLight = glow around the check)")]
        public Material badgeLight;
        public Material badgeDark;
        public Material glyphGood;
        public Material glyphBad;

        [Header("Colours")]
        [Tooltip("Blob / pump / target colours by index. 0 is the blob's own colour.")]
        public Color[] cellColors =
        {
            MaterialFactory.Hex(0xFF5A4F), // coral
            MaterialFactory.Hex(0x3FCB6E), // green
            MaterialFactory.Hex(0xA26BF5), // purple
            MaterialFactory.Hex(0xFF9F2E), // orange
        };

        [Header("Blob shape (change while playing - the blob updates at once)")]
        [Range(0.2f, 1.2f)] public float blobHeight = 0.62f;
        [Tooltip("Gap between the blob and the edge of its cells.")]
        [Range(0f, 0.2f)] public float blobMargin = 0.05f;
        [Tooltip("How round the outer corners are.")]
        [Range(0f, 0.45f)] public float blobCornerRadius = 0.24f;
        [Tooltip("Size of the rounded top edge.")]
        [Range(0f, 0.4f)] public float blobBevel = 0.14f;

        [Header("Where two colours meet")]
        [Tooltip("How two colours of the blob meet: an edge between two pieces, a soft blend, or a plain hard line.")]
        public ColorSeam colorSeam = ColorSeam.Edge;
        [Tooltip("Edge: gap between two colours (0 = the pieces touch).")]
        [Range(0f, 0.1f)] public float seamGap = 0f;
        [Tooltip("Edge: how round the little notch is where the edge reaches the outside of the blob.")]
        [Range(0f, 0.24f)] public float seamCorner = 0.1f;
        [Tooltip("Edge: how deep and wide the groove between two colours is. Small = a fine crease, so the blob still reads as one piece.")]
        [Range(0f, 0.4f)] public float seamBevel = 0.14f;
        [Tooltip("Soft blend: how far the colours run into each other, in cells on each side of the seam.")]
        [Range(0f, 0.5f)] public float colorBlend = 0.2f;

        [Tooltip("Spare blocks lined up behind every pump (purely visual, 0 = none).")]
        [Range(0, 4)] public int pumpSupplyBlocks = 2;

        [Tooltip("The board changes to the next palette every few levels.")]
        public BoardPalette[] palettes =
        {
            new BoardPalette("Mint", 0xD2ECCB, 0x80BE90, 0x9BD0A6),
            new BoardPalette("Lavender", 0xDCD8F8, 0x978FDC, 0xAEA8E8),
            new BoardPalette("Sky", 0xCFEAF8, 0x78B9DE, 0x96CCE9),
            new BoardPalette("Sand", 0xF8EAC8, 0xDDB970, 0xE9CB8C),
        };
        public int levelsPerPalette = 3;

        Material[] m_BlobByColor;
        Material[] m_MarkByColor;
        Material[] m_PumpByColor;
        Material[] m_GrooveByColor;
        Material[] m_GrooveEdgeByColor;
        Material m_BlobBlend;
        bool m_Prepared;

        public int ColorCount { get { return Mathf.Max(1, cellColors.Length); } }

        public Color SurfaceColor(int levelIndex)
        {
            return PaletteFor(levelIndex).surface;
        }

        public BoardPalette PaletteFor(int levelIndex)
        {
            if (palettes == null || palettes.Length == 0) return new BoardPalette("Mint", 0xD2ECCB, 0x80BE90, 0x9BD0A6);
            int per = Mathf.Max(1, levelsPerPalette);
            return palettes[(levelIndex / per) % palettes.Length];
        }

        /// <summary>Call once before use: fills empty slots and switches to runtime copies.</summary>
        public void Prepare()
        {
            if (m_Prepared) return;
            m_Prepared = true;

            boardSurface = Copy(boardSurface, MaterialFactory.Slot.BoardSurface);
            boardPit = Copy(boardPit, MaterialFactory.Slot.BoardPit);
            floorPad = Copy(floorPad, MaterialFactory.Slot.FloorPad);
            target = Copy(target, MaterialFactory.Slot.Target);
            targetMark = Copy(targetMark, MaterialFactory.Slot.TargetMark);
            forbidden = Copy(forbidden, MaterialFactory.Slot.Forbidden);
            forbiddenMark = Copy(forbiddenMark, MaterialFactory.Slot.ForbiddenMark);
            blob = Copy(blob, MaterialFactory.Slot.Blob);
            eye = Copy(eye, MaterialFactory.Slot.Eye);
            eyeShine = Copy(eyeShine, MaterialFactory.Slot.EyeShine);
            pumpBody = Copy(pumpBody, MaterialFactory.Slot.PumpBody);
            pumpNozzle = Copy(pumpNozzle, MaterialFactory.Slot.PumpNozzle);
            pumpKnob = Copy(pumpKnob, MaterialFactory.Slot.PumpKnob);
            badgeLight = Copy(badgeLight, MaterialFactory.Slot.BadgeLight);
            badgeDark = Copy(badgeDark, MaterialFactory.Slot.BadgeDark);
            glyphGood = Copy(glyphGood, MaterialFactory.Slot.GlyphGood);
            glyphBad = Copy(glyphBad, MaterialFactory.Slot.GlyphBad);

            int n = ColorCount;
            m_BlobByColor = new Material[n];
            m_MarkByColor = new Material[n];
            m_PumpByColor = new Material[n];
            m_GrooveByColor = new Material[n];
            m_GrooveEdgeByColor = new Material[n];
            for (int i = 0; i < n; i++)
            {
                Color c = cellColors.Length > i ? cellColors[i] : Color.white;
                m_BlobByColor[i] = new Material(blob);
                MaterialFactory.Tint(m_BlobByColor[i], c);
                m_MarkByColor[i] = new Material(targetMark);
                MaterialFactory.Tint(m_MarkByColor[i], c);
                m_PumpByColor[i] = new Material(pumpBody);
                MaterialFactory.Tint(m_PumpByColor[i], c);

                // Engraved marks: a darker groove and a lighter far edge, both in the cell's own colour.
                float h, s, v;
                Color.RGBToHSV(c, out h, out s, out v);
                m_GrooveByColor[i] = Matte(blob, Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f), v * 0.6f));
                m_GrooveEdgeByColor[i] = Matte(blob, Color.Lerp(c, Color.white, 0.5f));
            }

            // One material for a blob whose colours come from a texture (soft seams between colours).
            // Its shadow tint is the average of what the single-colour materials use.
            m_BlobBlend = new Material(blob);
            m_BlobBlend.name = "Blob (blended colours)";
            MaterialFactory.Tint(m_BlobBlend, Color.white);
            if (m_BlobBlend.HasProperty("_SColor"))
            {
                Color sum = Color.clear;
                for (int i = 0; i < n; i++) sum += MaterialFactory.ShadeTint(cellColors.Length > i ? cellColors[i] : Color.white);
                sum /= n;
                sum.a = 1f;
                m_BlobBlend.SetColor("_SColor", sum);
            }
        }

        static Material Matte(Material source, Color color)
        {
            var m = new Material(source);
            if (m.HasProperty("_UseSpecular")) m.SetFloat("_UseSpecular", 0f);
            m.DisableKeyword("TCP2_SPECULAR");
            if (m.HasProperty("_UseRim")) m.SetFloat("_UseRim", 0f);
            m.DisableKeyword("TCP2_RIM_LIGHTING");
            MaterialFactory.Tint(m, color);
            return m;
        }

        static Material Copy(Material source, MaterialFactory.Slot slot)
        {
            if (source == null) return MaterialFactory.Create(slot);
            var m = new Material(source);
            m.name = source.name + " (runtime)";
            return m;
        }

        public void ApplyPalette(int levelIndex)
        {
            BoardPalette p = PaletteFor(levelIndex);
            MaterialFactory.Tint(boardSurface, p.surface);
            MaterialFactory.Tint(boardPit, p.pit);
            MaterialFactory.Tint(floorPad, p.pad);
        }

        public Material[] BlobMaterials { get { return m_BlobByColor; } }

        /// <summary>White blob material; the colours come from a texture set by the blob itself.</summary>
        public Material BlobBlendMaterial { get { return m_BlobBlend; } }

        /// <summary>Dot on a target tile: yellow when any colour fits, otherwise the colour it needs.</summary>
        public Material MarkMaterial(int color)
        {
            return color < 0 ? targetMark : m_MarkByColor[Mathf.Clamp(color, 0, m_MarkByColor.Length - 1)];
        }

        /// <summary>Plain pumps keep their own look, coloured pumps wear their colour.</summary>
        public Material PumpMaterial(int color)
        {
            return color < 0 ? pumpBody : m_PumpByColor[Mathf.Clamp(color, 0, m_PumpByColor.Length - 1)];
        }

        /// <summary>Dark groove colour for marks pressed into a cell of this colour.</summary>
        public Material GrooveMaterial(int color)
        {
            return m_GrooveByColor[Mathf.Clamp(color, 0, m_GrooveByColor.Length - 1)];
        }

        /// <summary>Light edge of that groove.</summary>
        public Material GrooveEdgeMaterial(int color)
        {
            return m_GrooveEdgeByColor[Mathf.Clamp(color, 0, m_GrooveEdgeByColor.Length - 1)];
        }

        public Color CellColor(int color)
        {
            return cellColors[Mathf.Clamp(color, 0, cellColors.Length - 1)];
        }
    }

    /// <summary>How two colours of the blob meet.</summary>
    public enum ColorSeam { HardLine, SoftBlend, Edge }

    /// <summary>Creates the default materials (Toony Colors Pro 2 when available, URP Lit otherwise).</summary>
    public static class MaterialFactory
    {
        public enum Slot
        {
            BoardSurface, BoardPit, FloorPad, Target, TargetMark, Forbidden, ForbiddenMark,
            Blob, Eye, EyeShine, PumpBody, PumpNozzle, PumpKnob,
            BadgeLight, BadgeDark, GlyphGood, GlyphBad
        }

        public const string ToonShader = "Toony Colors Pro 2/Hybrid Shader 2";
        public const string LitShader = "Universal Render Pipeline/Lit";
        public const string UnlitShader = "Universal Render Pipeline/Unlit";

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }

        public static string AssetName(Slot slot)
        {
            return "M_" + slot;
        }

        public static Material Create(Slot slot)
        {
            Material m;
            switch (slot)
            {
                case Slot.BoardSurface:  m = Toon(Hex(0xD2ECCB), 0f, 0f); break;
                case Slot.BoardPit:      m = Toon(Hex(0x80BE90), 0f, 0f); break;
                case Slot.FloorPad:      m = Toon(Hex(0x9BD0A6), 0f, 0f); break;
                case Slot.Target:        m = Toon(Hex(0xFBF6E4), 0f, 0f); break;
                case Slot.TargetMark:    m = Toon(Hex(0xFFC62E), 0.2f, 0f); break;
                case Slot.Forbidden:     m = Toon(Hex(0x5F6F7C), 0f, 0f); break;
                case Slot.ForbiddenMark: m = Toon(Hex(0xFF4D5E), 0.2f, 0f); break;
                case Slot.Blob:          m = Toon(Hex(0xFF5A4F), 0.22f, 0.3f); break;
                case Slot.PumpBody:      m = Toon(Hex(0x4AA6F7), 0.25f, 0.2f); break;
                case Slot.PumpNozzle:    m = Toon(Hex(0x35618F), 0.2f, 0f); break;
                case Slot.PumpKnob:      m = Toon(Hex(0xFFC62E), 0.25f, 0.2f); break;
                case Slot.Eye:           m = Unlit(Hex(0x2A2730)); break;
                case Slot.EyeShine:      m = Unlit(Color.white); break;
                case Slot.BadgeLight:    m = Unlit(Hex(0xFFF9F2)); break;
                case Slot.BadgeDark:     m = Unlit(Hex(0x3B2F3F)); break;
                case Slot.GlyphGood:     m = Unlit(Hex(0x1DB954)); break;
                case Slot.GlyphBad:      m = Unlit(Hex(0xE0182D)); break;
                default:                 m = Toon(Color.white, 0f, 0f); break;
            }
            m.name = AssetName(slot);
            return m;
        }

        /// <summary>Shadow colour for a surface colour: a little darker, a little richer.</summary>
        public static Color Shade(Color c)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 1.25f + 0.05f), v * 0.78f);
        }

        /// <summary>Recolours a material made by this factory (works for the toon, lit and unlit variants).</summary>
        public static void Tint(Material m, Color color)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_SColor")) m.SetColor("_SColor", ShadeTint(color));
        }

        /// <summary>Toony Colors multiplies the albedo by the shadow colour, so the shade is expressed as a tint.</summary>
        public static Color ShadeTint(Color color)
        {
            Color shade = Shade(color);
            return new Color(
                Mathf.Clamp01(shade.r / Mathf.Max(color.r, 0.01f)),
                Mathf.Clamp01(shade.g / Mathf.Max(color.g, 0.01f)),
                Mathf.Clamp01(shade.b / Mathf.Max(color.b, 0.01f)), 1f);
        }

        /// <param name="color">Lit colour.</param>
        /// <param name="gloss">0 = matte, 1 = strong toon highlight.</param>
        /// <param name="rim">0 = no rim light.</param>
        public static Material Toon(Color color, float gloss, float rim)
        {
            Shader shader = Shader.Find(ToonShader);
            if (shader == null)
            {
                shader = Shader.Find(LitShader);
                if (shader == null) shader = Shader.Find("Standard");
                var lit = new Material(shader);
                Tint(lit, color);
                if (lit.HasProperty("_Smoothness")) lit.SetFloat("_Smoothness", Mathf.Lerp(0.1f, 0.7f, gloss));
                return lit;
            }

            var m = new Material(shader);
            m.SetColor("_HColor", Color.white);
            Tint(m, color);
            m.SetFloat("_RampThreshold", 0.5f);
            m.SetFloat("_RampSmoothing", 0.35f);

            if (gloss > 0.01f)
            {
                m.SetFloat("_UseSpecular", 1f);
                m.EnableKeyword("TCP2_SPECULAR");
                m.SetFloat("_SpecularType", 1f);
                m.EnableKeyword("TCP2_SPECULAR_STYLIZED");
                m.SetColor("_SpecularColor", new Color(gloss, gloss, gloss, 1f));
                m.SetFloat("_SpecularToonSize", 0.12f);
                m.SetFloat("_SpecularToonSmoothness", 0.25f);
            }

            if (rim > 0.01f)
            {
                m.SetFloat("_UseRim", 1f);
                m.EnableKeyword("TCP2_RIM_LIGHTING");
                m.SetColor("_RimColor", new Color(rim, rim, rim, 1f));
                m.SetFloat("_RimMin", 0.55f);
                m.SetFloat("_RimMax", 1f);
            }

            return m;
        }

        public static Material Unlit(Color color)
        {
            Shader shader = Shader.Find(UnlitShader);
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader);
            Tint(m, color);
            return m;
        }
    }
}
