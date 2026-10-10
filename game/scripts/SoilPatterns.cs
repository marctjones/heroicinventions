using Godot;
using HeroicInventions.Sim.Game;

namespace HeroicInventions;

/// <summary>
/// The soils' patterns (docs/art-direction.md 12.22): a geologic map draws each unit in a colour and a lithologic pattern (FGDC-STD-013-2006,
/// section 37), so units of near the same colour still tell apart. <see cref="SoilLook.PatternFor"/> says which soil takes which; this is the
/// one GLSL text that draws them, put into the ground's shader (TerrainView), the navigation map's (NavMap) and its key's swatches, so a
/// swatch shows the mark the ground and the map show by construction.
/// <para>
/// A cell's pattern goes to the shaders as weights, one byte a pattern slot (<see cref="SoilLook.Pattern"/>'s numbers) in two RGBA8 textures:
/// 255 in its own slot, 0 in the rest, sampled linearly, so at a contact between two soils the two patterns hand over across a cell, as the
/// colours do, and no slot number is ever blended into another. The marks are drawn in a darker tone of the soil's own colour (a lighter one
/// on the dark basalt sand; pale bluish flecks for ice), on a grid fixed to the world, anti-aliased, and faded out before a motif gets small
/// enough to shimmer.
/// </para>
/// </summary>
public static class SoilPatterns
{
    /// <summary>Sets a cell's eight pattern weights (bytes k*4 to k*4+3 of each texture) to the one slot of <paramref name="p"/>.</summary>
    public static void Put(SoilLook.Pattern p, byte[] a, byte[] b, int k)
    {
        for (int c = 0; c < 4; c++) { a[k * 4 + c] = 0; b[k * 4 + c] = 0; }
        int slot = (int)p;
        if (slot < 4) a[k * 4 + slot] = 255; else b[k * 4 + slot - 4] = 255;
    }

    /// <summary>The pattern whose weight is largest at cell k (what the shader draws in the middle of that cell).</summary>
    public static SoilLook.Pattern At(byte[] a, byte[] b, int k)
    {
        int best = 0, weight = -1;
        for (int s = 0; s < SoilLook.PatternSlots; s++)
        {
            int w = s < 4 ? a[k * 4 + s] : b[k * 4 + s - 4];
            if (w > weight) { weight = w; best = s; }
        }
        return (SoilLook.Pattern)best;
    }

    /// <summary>
    /// GLSL: <c>vec2 soil_marks(vec4 wa, vec4 wb, vec2 q, float px)</c>, the coverage (0 to 1) of the marks at q (a unit grid: one motif a
    /// cell) for the pattern weights wa (slots 0-3) and wb (4-7), px the size of a pixel in q's units; x the ordinary marks, y the ice's flecks.
    /// </summary>
    public const string Glsl = """
        vec2 sp_hash(vec2 c) { return fract(sin(vec2(dot(c, vec2(127.1, 311.7)), dot(c, vec2(269.5, 183.3)))) * 43758.5453); }
        float sp_cov(float d, float px) { return 1.0 - smoothstep(-px, px, d); }
        float sp_seg(vec2 p, vec2 a, vec2 b) { vec2 pa = p - a, ba = b - a; return length(pa - ba * clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0)); }
        float sp_tri(vec2 p, float r) {   // an equilateral triangle's signed distance (after Inigo Quilez)
            const float k = 1.7320508;
            p.x = abs(p.x) - r; p.y = p.y + r / k;
            if (p.x + k * p.y > 0.0) p = vec2(p.x - k * p.y, -k * p.x - p.y) / 2.0;
            p.x -= clamp(p.x, -2.0 * r, 0.0);
            return -length(p) * sign(p.y);
        }
        vec2 sp_rot(vec2 p, float a) { float c = cos(a), s = sin(a); return vec2(c * p.x - s * p.y, s * p.x + c * p.y); }
        // one pattern's coverage, by its slot (SoilLook.Pattern)
        float soil_mark(int id, vec2 q, float px) {
            if (id == 1) {   // sand (FGDC 607): stipple, a dot in each of a finer grid's cells
                vec2 g = q * 1.6; vec2 c = floor(g); vec2 h = sp_hash(c);
                return sp_cov((length(g - c - (0.25 + 0.5 * h)) - (0.07 + 0.05 * h.y)) / 1.6, px);
            }
            vec2 c = floor(q); vec2 f = q - c; vec2 h = sp_hash(c); vec2 h2 = sp_hash(c + 37.0);
            if (id == 2) {   // silt, fine soil (FGDC 616): short level dashes, a dot beside some
                vec2 ce = 0.3 + 0.4 * h;
                float d = sp_seg(f, ce - vec2(0.17, 0.0), ce + vec2(0.17, 0.0)) - 0.04;
                if (h2.x > 0.4) d = min(d, length(f - (0.15 + 0.7 * h2)) - 0.05);
                return sp_cov(d, px);
            }
            if (id == 3) {   // clay (FGDC 620): rows of long thin dashes, staggered
                float row = floor(q.y * 2.0);
                float x = q.x + 0.5 * mod(row, 2.0) + 0.3 * sp_hash(vec2(row, 3.0)).x;
                float d = length(vec2(max(abs(fract(x) - 0.5) - 0.33, 0.0), (abs(fract(q.y * 2.0) - 0.5)) / 2.0)) - 0.035;
                return sp_cov(d, px);
            }
            if (id == 4) {   // rubble, talus, breccia (FGDC 605): angular fragments, turned every way
                vec2 p = sp_rot(f - (0.32 + 0.36 * h), 6.2832 * h2.x);
                return sp_cov(sp_tri(p, 0.11 + 0.07 * h2.y), px);
            }
            if (id == 5) {   // bedrock, massive rock: jointed blocks, as a cracked pavement (irregular, so never mistaken for the 1 m grid)
                vec2 g = q * 0.8; vec2 gc = floor(g); vec2 gf = g - gc;
                float f1 = 8.0, f2 = 8.0;
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++) {
                        vec2 o = vec2(float(i), float(j));
                        vec2 r = o + 0.15 + 0.7 * sp_hash(gc + o) - gf;
                        float d2 = dot(r, r);
                        if (d2 < f1) { f2 = f1; f1 = d2; } else if (d2 < f2) { f2 = d2; }
                    }
                return sp_cov((sqrt(f2) - sqrt(f1)) * 0.5 / 0.8 - 0.045, px);   // about the distance to the joint, in q's units
            }
            if (id == 6) {   // ice-cemented soil: flecks, short and thin, level or slanting
                vec2 ce = 0.3 + 0.4 * h;
                vec2 a = sp_rot(vec2(0.13, 0.0), h2.x > 0.5 ? 1.05 : 0.0);
                return sp_cov(sp_seg(f, ce - a, ce + a) - 0.03, px);
            }
            if (id == 7) {   // spoil, dug or tipped (FGDC 681, till or diamicton: unsorted): open rings and dots
                float d = abs(length(f - (0.3 + 0.4 * h)) - 0.14) - 0.048;
                d = min(d, length(f - fract(0.3 + 0.4 * h + 0.5)) - 0.065);
                return sp_cov(d, px);
            }
            return 0.0;
        }
        vec2 soil_marks(vec4 wa, vec4 wb, vec2 q, float px) {
            // a little sharper than the linear hand-over, so a contact reads as a line, not a 5 m smear
            wa = smoothstep(0.2, 0.8, wa); wb = smoothstep(0.2, 0.8, wb);
            float m = 0.0;
            if (wa.y > 0.01) m += wa.y * soil_mark(1, q, px);
            if (wa.z > 0.01) m += wa.z * soil_mark(2, q, px);
            if (wa.w > 0.01) m += wa.w * soil_mark(3, q, px);
            if (wb.x > 0.01) m += wb.x * soil_mark(4, q, px);
            if (wb.y > 0.01) m += wb.y * soil_mark(5, q, px);
            if (wb.w > 0.01) m += wb.w * soil_mark(7, q, px);
            float ice = wb.z > 0.01 ? wb.z * soil_mark(6, q, px) : 0.0;
            return vec2(clamp(m, 0.0, 1.0), ice);
        }
        // the marks on a soil's colour in sRGB (the map and the key's swatches): a darker tone of the soil, a lighter one on a dark soil,
        // and the ice's flecks pale and bluish
        vec3 soil_marked_srgb(vec3 sc, vec2 m) {
            vec3 tone = dot(sc, vec3(0.2126, 0.7152, 0.0722)) < 0.33 ? sc * 1.45 + 0.03 : sc * 0.72;
            vec3 col = mix(sc, tone, 0.85 * m.x);
            return mix(col, min(sc * vec3(1.2, 1.32, 1.55) + 0.06, vec3(1.0)), 0.9 * m.y);
        }
        """;

    /// <summary>The key's swatch: a soil's colour with its pattern, drawn by the map's own GLSL at the map's own size of motif.</summary>
    public const string SwatchShader = "shader_type canvas_item;\nuniform vec3 base;   // sRGB, as the map's soils texture\nuniform vec4 sw_a;\nuniform vec4 sw_b;\nuniform vec2 size_px;\nuniform float motif_px = 12.0;\n"
        + Glsl + """
        void fragment() {
            COLOR = vec4(soil_marked_srgb(base, soil_marks(sw_a, sw_b, UV * size_px / motif_px, 1.0 / motif_px)), 1.0);
        }
        """;

    /// <summary>One-hot weights of a pattern, as the two uniforms the swatch shader reads.</summary>
    public static (Vector4 A, Vector4 B) Weights(SoilLook.Pattern p)
    {
        var a = new byte[4]; var b = new byte[4];
        Put(p, a, b, 0);
        return (new Vector4(a[0], a[1], a[2], a[3]) / 255f, new Vector4(b[0], b[1], b[2], b[3]) / 255f);
    }
}
