using System.IO.Compression;
using System.Security.Cryptography;
using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// Draws a CAPTCHA answer as a PNG, with no graphics library at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why by hand.</b> <c>System.Drawing</c> needs libgdiplus, which the Linux
/// containers do not have, and a font file is one more thing to ship. The
/// alphabet is twenty-five characters, so each has a 5x7 bitmap here; each is
/// drawn scaled, sheared, shifted and coloured at random over a speckled
/// background with lines through it, then written as an RGB PNG (IHDR, one
/// zlib IDAT, IEND).
/// </para>
/// <para>
/// <b>What it is not.</b> A determined solver with OCR can read this, as it can
/// most image CAPTCHAs. What it buys is the point of a CAPTCHA on a sign-in
/// form: a password-guessing script now needs a solve per attempt, each one
/// single use, on top of the account lockout - without a third party learning
/// who signs in to a Samaaj.
/// </para>
/// </remarks>
public sealed class CaptchaRenderer : ICaptchaRenderer
{
    private const int Width = 200;
    private const int Height = 70;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['C'] = ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01111", "10000", "10000", "10011", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['J'] = ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
        ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['6'] = ["01110", "10000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00001", "01110"],
    };

    /// <summary>Every character the domain may ask for has a glyph - a test holds this.</summary>
    public static bool CanDraw(char c) => Glyphs.ContainsKey(c);

    public byte[] RenderPng(string answer)
    {
        var pixels = new byte[Width * Height * 3];

        // A speckled light background.
        for (var i = 0; i < Width * Height; i++)
        {
            var shade = (byte)RandomNumberGenerator.GetInt32(218, 256);
            pixels[i * 3] = shade;
            pixels[(i * 3) + 1] = (byte)Math.Max(0, shade - RandomNumberGenerator.GetInt32(0, 12));
            pixels[(i * 3) + 2] = shade;
        }

        var slot = (Width - 20) / Math.Max(answer.Length, 1);

        for (var index = 0; index < answer.Length; index++)
        {
            if (!Glyphs.TryGetValue(answer[index], out var glyph))
            {
                continue;
            }

            var scale = RandomNumberGenerator.GetInt32(5, 7);
            var shear = (RandomNumberGenerator.GetInt32(-35, 36) / 100.0) * scale;
            var left = 10 + (index * slot) + RandomNumberGenerator.GetInt32(-3, 4);
            var top = RandomNumberGenerator.GetInt32(4, Math.Max(5, Height - (7 * scale) - 4));
            var (r, g, b) = (
                (byte)RandomNumberGenerator.GetInt32(0, 110),
                (byte)RandomNumberGenerator.GetInt32(0, 90),
                (byte)RandomNumberGenerator.GetInt32(40, 140));

            for (var row = 0; row < 7; row++)
            {
                var offset = (int)Math.Round((row - 3) * shear / 3.0);

                for (var column = 0; column < 5; column++)
                {
                    if (glyph[row][column] != '1')
                    {
                        continue;
                    }

                    FillRect(pixels, left + (column * scale) + offset, top + (row * scale), scale, scale, r, g, b);
                }
            }
        }

        // Lines through it, and loose dots, in mid tones.
        for (var line = 0; line < 4; line++)
        {
            var shade = (byte)RandomNumberGenerator.GetInt32(80, 170);
            DrawLine(pixels,
                RandomNumberGenerator.GetInt32(0, Width), RandomNumberGenerator.GetInt32(0, Height),
                RandomNumberGenerator.GetInt32(0, Width), RandomNumberGenerator.GetInt32(0, Height),
                shade);
        }

        for (var dot = 0; dot < 350; dot++)
        {
            var shade = (byte)RandomNumberGenerator.GetInt32(60, 200);
            SetPixel(pixels, RandomNumberGenerator.GetInt32(0, Width), RandomNumberGenerator.GetInt32(0, Height),
                shade, shade, shade);
        }

        return EncodePng(pixels);
    }

    private static void FillRect(byte[] pixels, int x, int y, int w, int h, byte r, byte g, byte b)
    {
        for (var dy = 0; dy < h; dy++)
        {
            for (var dx = 0; dx < w; dx++)
            {
                SetPixel(pixels, x + dx, y + dy, r, g, b);
            }
        }
    }

    private static void DrawLine(byte[] pixels, int x0, int y0, int x1, int y1, byte shade)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            SetPixel(pixels, x0, y0, shade, shade, shade);
            SetPixel(pixels, x0, y0 + 1, shade, shade, shade);

            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            var twice = 2 * error;

            if (twice >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (twice <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void SetPixel(byte[] pixels, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return;
        }

        var i = ((y * Width) + x) * 3;
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
    }

    // ---- PNG ---------------------------------------------------------------------

    private static byte[] EncodePng(byte[] rgb)
    {
        using var output = new MemoryStream();

        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, Width);
        WriteBigEndian(header, 4, Height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: RGB
        WriteChunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < Height; y++)
                {
                    zlib.WriteByte(0);   // filter: none
                    zlib.Write(rgb, y * Width * 3, Width * 3);
                }
            }

            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, (int)Crc32([.. typeBytes, .. data]));
        output.Write(crc);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
