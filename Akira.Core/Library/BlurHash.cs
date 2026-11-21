using System.Text;

namespace Akira.Core.Library;

/// <summary>
/// Encodes the BlurHash of an image (the reference algorithm from blurha.sh): a few cosine components of its
/// colors in about 30 characters, which a client draws as a blurred picture while the image itself loads.
/// </summary>
public static class BlurHash
{
    private const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    /// <summary>
    /// Encodes pixels given as RGB bytes, row by row. Wide images get more components across, tall ones more down.
    /// </summary>
    public static string Encode(ReadOnlySpan<byte> rgb, int width, int height, int componentsX, int componentsY)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(componentsX, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(componentsX, 9);
        ArgumentOutOfRangeException.ThrowIfLessThan(componentsY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(componentsY, 9);
        if (width < 1 || height < 1 || rgb.Length < width * height * 3)
        {
            throw new ArgumentException("The pixels do not fill the given size.", nameof(rgb));
        }

        var linear = new double[width * height * 3];
        for (var index = 0; index < linear.Length; index++)
        {
            linear[index] = ToLinear(rgb[index]);
        }

        var factors = new (double R, double G, double B)[componentsX * componentsY];
        for (var j = 0; j < componentsY; j++)
        {
            for (var i = 0; i < componentsX; i++)
            {
                double r = 0, g = 0, b = 0;
                for (var y = 0; y < height; y++)
                {
                    var basisY = Math.Cos(Math.PI * j * y / height);
                    for (var x = 0; x < width; x++)
                    {
                        var basis = Math.Cos(Math.PI * i * x / width) * basisY;
                        var at = (y * width + x) * 3;
                        r += basis * linear[at];
                        g += basis * linear[at + 1];
                        b += basis * linear[at + 2];
                    }
                }

                var scale = (i == 0 && j == 0 ? 1.0 : 2.0) / (width * height);
                factors[j * componentsX + i] = (r * scale, g * scale, b * scale);
            }
        }

        var hash = new StringBuilder();
        Append(hash, componentsX - 1 + (componentsY - 1) * 9, 1);

        double maximum = 1;
        if (factors.Length > 1)
        {
            var actual = factors.Skip(1).Max(factor => Largest(factor));
            var quantised = (int)Math.Clamp(Math.Floor(actual * 166 - 0.5), 0, 82);
            maximum = (quantised + 1) / 166.0;
            Append(hash, quantised, 1);
        }
        else
        {
            Append(hash, 0, 1);
        }

        var dc = factors[0];
        var dcValue = (ToSrgb(dc.R) << 16) + (ToSrgb(dc.G) << 8) + ToSrgb(dc.B);
        Append(hash, dcValue, 4);
        foreach (var factor in factors.Skip(1))
        {
            var acValue = Quantise(factor.R, maximum) * 19 * 19 + Quantise(factor.G, maximum) * 19 + Quantise(factor.B, maximum);
            Append(hash, acValue, 2);
        }

        return hash.ToString();
    }

    private static double Largest((double R, double G, double B) factor)
    {
        var red = Math.Abs(factor.R);
        var green = Math.Abs(factor.G);
        var blue = Math.Abs(factor.B);
        return Math.Max(red, Math.Max(green, blue));
    }

    private static int Quantise(double value, double maximum)
    {
        var scaled = Math.Sign(value) * Math.Pow(Math.Abs(value / maximum), 0.5) * 9 + 9.5;
        return (int)Math.Clamp(Math.Floor(scaled), 0, 18);
    }

    private static double ToLinear(byte value)
    {
        var v = value / 255.0;
        if (v <= 0.04045)
        {
            return v / 12.92;
        }

        return Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static int ToSrgb(double value)
    {
        var v = Math.Clamp(value, 0, 1);
        if (v <= 0.0031308)
        {
            return (int)(v * 12.92 * 255 + 0.5);
        }

        return (int)((1.055 * Math.Pow(v, 1 / 2.4) - 0.055) * 255 + 0.5);
    }

    private static void Append(StringBuilder hash, int value, int length)
    {
        for (var position = 1; position <= length; position++)
        {
            var digit = value / (int)Math.Pow(83, length - position) % 83;
            hash.Append(Characters[digit]);
        }
    }
}
