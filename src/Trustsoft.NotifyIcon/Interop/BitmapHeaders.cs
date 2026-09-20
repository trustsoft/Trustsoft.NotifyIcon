using System.Runtime.InteropServices;

namespace Trustsoft.NotifyIcon.Interop;

/// <summary>
/// The bitmap information structures of <c>wingdi.h</c> and the small factories that fill them in
/// for the two device-independent bitmaps an icon needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Header layout is the contract, so the size is pinned by a test.</b>
/// <see cref="BITMAPV5HEADER"/> is 124 bytes and its <c>bV5Size</c> field must say so: a caller
/// that passes a smaller size makes GDI ignore the fields beyond it (the colour masks and the
/// profile fields) and a caller that passes a larger size makes GDI read memory that is not
/// there. <see cref="BITMAPINFOHEADER"/> is the 40-byte version-3 header and shares the first
/// forty bytes of the version-5 header field for field.
/// </para>
/// <para>
/// <b>Why the version-5 header for a 32bpp icon.</b> The colour bitmap carries an alpha channel
/// (the shell composites the icon against the desktop); a version-5 header is the form that can
/// describe a 32bpp <c>BI_RGB</c> bitmap with an unused-but-present colour mask block, and it is
/// what the icon path in the shell expects. The <c>BI_RGB</c> compression makes the alpha bytes
/// of the DIB the icon's transparency - there is no run-length encoding involved.
/// </para>
/// <para>
/// <b>Why the mask uses <see cref="BITMAPINFOHEADER"/> plus a two-entry colour table.</b> A
/// monochrome (1bpp) DIB always has a colour table - GDI reads two entries for it whether the
/// caller asks for them or not, because the table cannot be elided for one bit per pixel. Passing
/// only a header would therefore make GDI read eight bytes past the structure. Wrapping the
/// header and the two entries in <see cref="BITMAPINFO"/> keeps every byte GDI reads inside the
/// marshalled structure. The icon's AND mask is consumed as bits, not as colours, so the two
/// entries only exist to fill the space GDI will read.
/// </para>
/// <para>
/// <b>Negative height is not a stylistic choice.</b> <c>biHeight &lt; 0</c> selects a
/// <em>top-down</em> DIB, so row 0 of the pixel buffer is the top row of the image. With a
/// positive height GDI treats the buffer as bottom-up and the icon is rendered vertically
/// mirrored - a bug that looks like a broken image rather than a header mistake.
/// </para>
/// <para>
/// <c>internal</c> by design: nothing here is part of the shipping public API (D002/D010), and
/// the structures never appear in a public signature.
/// </para>
/// </remarks>
internal static class BitmapHeaders
{
    /// <summary>
    /// <c>DIB_RGB_COLORS</c> (wingdi.h): the colour table holds literal RGB values. This is the
    /// only value the icon path uses.
    /// </summary>
    internal const uint DIB_RGB_COLORS = 0;

    /// <summary>
    /// <c>BI_RGB</c> (wingdi.h): no compression - the default for both bitmaps here. For a 32bpp
    /// DIB this is also what makes the fourth byte of every pixel the alpha channel.
    /// </summary>
    internal const uint BI_RGB = 0;

    /// <summary>The size in bytes of <see cref="BITMAPV5HEADER"/>, written to <c>bV5Size</c>.</summary>
    internal const int BitmapV5HeaderSize = 124;

    /// <summary>The size in bytes of <see cref="BITMAPINFOHEADER"/>, written to <c>biSize</c>.</summary>
    internal const int BitmapInfoHeaderSize = 40;

    /// <summary>The number of colour-table entries a monochrome DIB has.</summary>
    internal const int MonochromePaletteEntries = 2;

    /// <summary>
    /// Returns the row stride in bytes of a monochrome (1bpp) DIB of the given width.
    /// </summary>
    /// <param name="pixelSize">The bitmap width in pixels.</param>
    /// <returns>The stride in bytes, always a multiple of two.</returns>
    /// <remarks>
    /// This is the classic DIB rounding rule <c>((width * bitsPerPixel + 15) / 16) * 2</c>
    /// evaluated for one bit per pixel: rows are four-byte aligned in DWORD terms, or two-byte
    /// aligned in this form. It is <b>not</b> <c>width / 8</c> - for a width that is not a
    /// multiple of eight those two differ, and a buffer sized with the wrong stride skews every
    /// row after the first.
    /// </remarks>
    internal static int GetMaskStride(int pixelSize) => ((pixelSize + 15) / 16) * 2;

    /// <summary>
    /// Builds the top-down 32bpp <c>BI_RGB</c> header for the icon's colour bitmap.
    /// </summary>
    /// <param name="pixelSize">The bitmap width and height in pixels (icons are square here).</param>
    /// <param name="sizeImage">The total pixel buffer size in bytes, <c>stride * height</c>.</param>
    /// <returns>The header, with every field explicitly initialised.</returns>
    /// <remarks>
    /// Every field is assigned even where the value is zero. The unused fields are the colour
    /// masks, the endpoint/gamma/profile block and the reserved word: leaving them to default
    /// would be equivalent, but assigning them makes it visible that they were considered and
    /// that this is a <c>BI_RGB</c> DIB (in which the mask fields are ignored) rather than a
    /// bitmap whose colour space was simply forgotten.
    /// </remarks>
    internal static BITMAPV5HEADER CreateTopDownColorHeader(int pixelSize, uint sizeImage) => new()
    {
        bV5Size = BitmapV5HeaderSize,
        bV5Width = pixelSize,
        bV5Height = -pixelSize,
        bV5Planes = 1,
        bV5BitCount = 32,
        bV5Compression = BI_RGB,
        bV5SizeImage = sizeImage,
        bV5XPelsPerMeter = 0,
        bV5YPelsPerMeter = 0,
        bV5ClrUsed = 0,
        bV5ClrImportant = 0,
        bV5RedMask = 0,
        bV5GreenMask = 0,
        bV5BlueMask = 0,
        bV5AlphaMask = 0,
        bV5CSType = 0,
        bV5Endpoints = new CIEXYZTRIPLE(new CIEXYZ(0, 0, 0), new CIEXYZ(0, 0, 0), new CIEXYZ(0, 0, 0)),
        bV5GammaRed = 0,
        bV5GammaGreen = 0,
        bV5GammaBlue = 0,
        bV5Intent = 0,
        bV5ProfileData = 0,
        bV5ProfileSize = 0,
        bV5Reserved = 0,
    };

    /// <summary>
    /// Builds the top-down 1bpp monochrome header plus colour table for the icon's AND mask.
    /// </summary>
    /// <param name="pixelSize">The bitmap width and height in pixels.</param>
    /// <param name="sizeImage">The total mask buffer size in bytes,
    /// <see cref="GetMaskStride"/> times the height.</param>
    /// <returns>The header and the two colour entries GDI will read.</returns>
    internal static BITMAPINFO CreateTopDownMaskInfo(int pixelSize, uint sizeImage) => new()
    {
        bmiHeader = new BITMAPINFOHEADER
        {
            biSize = BitmapInfoHeaderSize,
            biWidth = pixelSize,
            biHeight = -pixelSize,
            biPlanes = 1,
            biBitCount = 1,
            biCompression = BI_RGB,
            biSizeImage = sizeImage,
            biXPelsPerMeter = 0,
            biYPelsPerMeter = 0,
            biClrUsed = MonochromePaletteEntries,
            biClrImportant = 0,
        },
        // The conventional monochrome DIB palette: entry 0 is the colour of a zero bit, entry 1
        // the colour of a set bit. An icon mask is read as bits, so these only fill the space.
        bmiColors0 = 0x00FFFFFF,
        bmiColors1 = 0x00000000,
    };
}

/// <summary>
/// The <c>CIEXYZ</c> structure from <c>wingdi.h</c>: one colour-space coordinate triple.
/// </summary>
/// <remarks>
/// Defined only because <see cref="BITMAPV5HEADER"/> embeds three of them; the icon path never
/// uses a colour space and assigns zeros.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct CIEXYZ
{
    /// <summary>The x coordinate.</summary>
    public int ciexyzX;

    /// <summary>The y coordinate.</summary>
    public int ciexyzY;

    /// <summary>The z coordinate.</summary>
    public int ciexyzZ;

    /// <summary>Initializes a new instance of the <see cref="CIEXYZ"/> struct.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="z">The z coordinate.</param>
    internal CIEXYZ(int x, int y, int z)
    {
        ciexyzX = x;
        ciexyzY = y;
        ciexyzZ = z;
    }
}

/// <summary>
/// The <c>CIEXYZTRIPLE</c> structure from <c>wingdi.h</c>: the red, green and blue endpoints of
/// the bitmap's colour space. 36 bytes, all read by GDI when the header claims version 5.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CIEXYZTRIPLE
{
    /// <summary>The red endpoint.</summary>
    public CIEXYZ ciexyzRed;

    /// <summary>The green endpoint.</summary>
    public CIEXYZ ciexyzGreen;

    /// <summary>The blue endpoint.</summary>
    public CIEXYZ ciexyzBlue;

    /// <summary>Initializes a new instance of the <see cref="CIEXYZTRIPLE"/> struct.</summary>
    /// <param name="red">The red endpoint.</param>
    /// <param name="green">The green endpoint.</param>
    /// <param name="blue">The blue endpoint.</param>
    internal CIEXYZTRIPLE(CIEXYZ red, CIEXYZ green, CIEXYZ blue)
    {
        ciexyzRed = red;
        ciexyzGreen = green;
        ciexyzBlue = blue;
    }
}

/// <summary>
/// The <c>BITMAPINFOHEADER</c> structure from <c>wingdi.h</c> (the version-3 header): 40 bytes,
/// and the first forty bytes of <see cref="BITMAPV5HEADER"/> field for field.
/// </summary>
/// <remarks>
/// Used here for the monochrome AND mask, where the version-3 header plus the two-entry colour
/// table in <see cref="BITMAPINFO"/> is exactly the description GDI expects.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    /// <summary>Structure size in bytes; must be <see cref="BitmapHeaders.BitmapInfoHeaderSize"/>.</summary>
    public uint biSize;

    /// <summary>Bitmap width in pixels.</summary>
    public int biWidth;

    /// <summary>Bitmap height in pixels; negative for a top-down bitmap.</summary>
    public int biHeight;

    /// <summary>Number of colour planes; always 1.</summary>
    public ushort biPlanes;

    /// <summary>Bits per pixel.</summary>
    public ushort biBitCount;

    /// <summary>Compression, <c>BI_RGB</c> here.</summary>
    public uint biCompression;

    /// <summary>Total pixel buffer size in bytes.</summary>
    public uint biSizeImage;

    /// <summary>Horizontal resolution in pixels per metre; unused.</summary>
    public int biXPelsPerMeter;

    /// <summary>Vertical resolution in pixels per metre; unused.</summary>
    public int biYPelsPerMeter;

    /// <summary>Number of colour-table entries; two for a monochrome DIB.</summary>
    public uint biClrUsed;

    /// <summary>Number of colour-table entries that matter; unused.</summary>
    public uint biClrImportant;
}

/// <summary>
/// The <c>BITMAPINFO</c> structure from <c>wingdi.h</c>: a <see cref="BITMAPINFOHEADER"/> followed
/// by its colour table. The two entries declared here are exactly what GDI reads for a monochrome
/// bitmap, so nothing outside this structure is ever read.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    /// <summary>The header.</summary>
    public BITMAPINFOHEADER bmiHeader;

    /// <summary>Colour-table entry for a zero bit (white by convention).</summary>
    public uint bmiColors0;

    /// <summary>Colour-table entry for a set bit (black by convention).</summary>
    public uint bmiColors1;
}

/// <summary>
/// The <c>BITMAPV5HEADER</c> structure from <c>wingdi.h</c>: 124 bytes, the description GDI reads
/// for the 32bpp colour bitmap an icon is built from.
/// </summary>
/// <remarks>
/// <para>
/// The documented field order is preserved exactly, including the <c>CIEXYZTRIPLE</c> colour
/// space endpoints that sit between <c>bV5CSType</c> and the gamma values - the single most
/// common way to get this structure wrong is to forget those 36 bytes, which shifts every
/// following field and makes <c>bV5Size</c> a lie.
/// </para>
/// <para>
/// No <c>Pack</c> is specified: every field is naturally aligned at its own offset, which is what
/// the 124-byte size in <c>bV5Size</c> describes.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPV5HEADER
{
    /// <summary>Structure size in bytes; must be <see cref="BitmapHeaders.BitmapV5HeaderSize"/>.</summary>
    public uint bV5Size;

    /// <summary>Bitmap width in pixels.</summary>
    public int bV5Width;

    /// <summary>Bitmap height in pixels; negative for a top-down bitmap.</summary>
    public int bV5Height;

    /// <summary>Number of colour planes; always 1.</summary>
    public ushort bV5Planes;

    /// <summary>Bits per pixel; 32 for the icon colour bitmap.</summary>
    public ushort bV5BitCount;

    /// <summary>Compression, <c>BI_RGB</c> here.</summary>
    public uint bV5Compression;

    /// <summary>Total pixel buffer size in bytes.</summary>
    public uint bV5SizeImage;

    /// <summary>Horizontal resolution in pixels per metre; unused.</summary>
    public int bV5XPelsPerMeter;

    /// <summary>Vertical resolution in pixels per metre; unused.</summary>
    public int bV5YPelsPerMeter;

    /// <summary>Number of colour-table entries; zero for 32bpp.</summary>
    public uint bV5ClrUsed;

    /// <summary>Number of colour-table entries that matter; unused.</summary>
    public uint bV5ClrImportant;

    /// <summary>Red channel mask; ignored for <c>BI_RGB</c>.</summary>
    public uint bV5RedMask;

    /// <summary>Green channel mask; ignored for <c>BI_RGB</c>.</summary>
    public uint bV5GreenMask;

    /// <summary>Blue channel mask; ignored for <c>BI_RGB</c>.</summary>
    public uint bV5BlueMask;

    /// <summary>Alpha channel mask; ignored for <c>BI_RGB</c>.</summary>
    public uint bV5AlphaMask;

    /// <summary>Colour space type; zero (<c>LCS_CALIBRATED_RGB</c>) and unused here.</summary>
    public uint bV5CSType;

    /// <summary>Colour space endpoints; 36 bytes, zeroed, but always present.</summary>
    public CIEXYZTRIPLE bV5Endpoints;

    /// <summary>Red gamma value; unused.</summary>
    public uint bV5GammaRed;

    /// <summary>Green gamma value; unused.</summary>
    public uint bV5GammaGreen;

    /// <summary>Blue gamma value; unused.</summary>
    public uint bV5GammaBlue;

    /// <summary>Rendering intent; unused.</summary>
    public uint bV5Intent;

    /// <summary>Offset of the embedded ICC profile; unused, so zero.</summary>
    public uint bV5ProfileData;

    /// <summary>Size of the embedded ICC profile; unused, so zero.</summary>
    public uint bV5ProfileSize;

    /// <summary>Reserved; must be zero.</summary>
    public uint bV5Reserved;
}
