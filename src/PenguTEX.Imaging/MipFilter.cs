using PenguTEX.Core;

namespace PenguTEX.Imaging;

public static class MipFilter
{
    public static double Coverage(PixelImage image, int alphaCutoff = 128) => Enumerable.Range(0, image.Width * image.Height).Count(i => image.Rgba[i * 4 + 3] >= alphaCutoff) / (double)(image.Width * image.Height);
    private static double Linear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    private static double Srgb(double value) => value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
    private static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    public static PixelImage Downsample(PixelImage input, TextureSemantic semantic, double opacityCoverage = 0, CancellationToken ct = default, int alphaCutoff = 128)
    {
        int width = Math.Max(1, input.Width / 2), height = Math.Max(1, input.Height / 2); byte[] result = new byte[width * height * 4];
        bool color = semantic is TextureSemantic.SrgbColor or TextureSemantic.OpacitySrgb or TextureSemantic.OpacityBlendSrgb;
        bool opacity = semantic is TextureSemantic.OpacitySrgb or TextureSemantic.OpacityBlendSrgb;
        bool normal = semantic is TextureSemantic.NormalRgb or TextureSemantic.NormalRg;
        // Area-weighted boxes include the final row/column of odd-sized textures.
        for (int y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested(); double y0 = y * (double)input.Height / height, y1 = (y + 1d) * input.Height / height;
            for (int x = 0; x < width; x++)
            {
                double x0 = x * (double)input.Width / width, x1 = (x + 1d) * input.Width / width;
                double r = 0, g = 0, b = 0, alpha = 0, total = 0, rgbWeight = 0, fallbackR = 0, fallbackG = 0, fallbackB = 0;
                for (int sy = (int)y0; sy < Math.Ceiling(y1); sy++)
                for (int sx = (int)x0; sx < Math.Ceiling(x1); sx++)
                {
                    double weight = (Math.Min(sx + 1, x1) - Math.Max(sx, x0)) * (Math.Min(sy + 1, y1) - Math.Max(sy, y0));
                    int at = (sy * input.Width + sx) * 4;
                    double pr = input.Rgba[at] / 255d, pg = input.Rgba[at + 1] / 255d, pb = input.Rgba[at + 2] / 255d, pa = input.Rgba[at + 3] / 255d;
                    if (normal)
                    {
                        pr = pr * 2 - 1; pg = pg * 2 - 1;
                        pb = semantic == TextureSemantic.NormalRg ? Math.Sqrt(Math.Max(0, 1 - pr * pr - pg * pg)) : pb * 2 - 1;
                    }
                    else if (color) { pr = Linear(pr); pg = Linear(pg); pb = Linear(pb); }
                    double rw = opacity ? weight * pa : weight;
                    r += pr * rw; g += pg * rw; b += pb * rw; rgbWeight += rw;
                    fallbackR += pr * weight; fallbackG += pg * weight; fallbackB += pb * weight;
                    alpha += pa * weight; total += weight;
                }
                if (rgbWeight > 0) { r /= rgbWeight; g /= rgbWeight; b /= rgbWeight; }
                else { r = fallbackR / total; g = fallbackG / total; b = fallbackB / total; }
                if (normal)
                {
                    double len = Math.Sqrt(r * r + g * g + b * b);
                    if (len < 1e-8) { r = 0; g = 0; b = 1; } else { r /= len; g /= len; b /= len; }
                    r = r * .5 + .5; g = g * .5 + .5; b = b * .5 + .5;
                }
                else if (color) { r = Srgb(r); g = Srgb(g); b = Srgb(b); }
                int dest = (y * width + x) * 4; result[dest] = Byte(r); result[dest + 1] = Byte(g); result[dest + 2] = Byte(b); result[dest + 3] = Byte(alpha / total);
            }
        }
        var output = new PixelImage(result, width, height);
        if (semantic == TextureSemantic.OpacitySrgb) PreserveCoverage(output, opacityCoverage, alphaCutoff);
        return output;
    }
    private static void PreserveCoverage(PixelImage image, double target, int alphaCutoff)
    {
        // Quantized alpha can only approximate coverage, especially in the tiny tail mips.
        double lo = 0, hi = 16, best = 1, bestError = double.PositiveInfinity;
        for (int n = 0; n < 18; n++)
        {
            double scale = n == 0 ? 1 : (lo + hi) / 2; int covered = 0;
            for (int at = 3; at < image.Rgba.Length; at += 4) if (Byte(image.Rgba[at] / 255d * scale) >= alphaCutoff) covered++;
            double actual = covered / (double)(image.Width * image.Height), error = Math.Abs(actual - target);
            if (error < bestError) { best = scale; bestError = error; }
            if (actual < target) lo = scale; else hi = scale;
        }
        for (int at = 3; at < image.Rgba.Length; at += 4) image.Rgba[at] = Byte(image.Rgba[at] / 255d * best);
    }
}
