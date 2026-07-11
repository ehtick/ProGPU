using System.Globalization;
using System.IO.Compression;
using System.Text;
using ProGPU.Scene;

namespace SkiaSharp;

public abstract class SKWStream : IDisposable
{
    internal abstract Stream BaseStream { get; }

    public long BytesWritten => BaseStream.CanSeek ? BaseStream.Position : 0;

    public bool Write(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        BaseStream.Write(buffer, 0, buffer.Length);
        return true;
    }

    public virtual void Flush() => BaseStream.Flush();
    public abstract void Dispose();
}

public sealed class SKFileWStream : SKWStream
{
    private readonly FileStream _stream;

    public SKFileWStream(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _stream = File.Create(path);
    }

    internal override Stream BaseStream => _stream;
    public override void Dispose() => _stream.Dispose();
}

public static class SKSvgCanvas
{
    public static SKCanvas Create(SKRect bounds, SKWStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return CreateCore(bounds, stream.BaseStream);
    }

    public static SKCanvas Create(SKRect bounds, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return CreateCore(bounds, stream);
    }

    private static SKCanvas CreateCore(SKRect bounds, Stream stream)
    {
        var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height));
        var context = new DrawingContext();
        var written = false;

        void Flush()
        {
            if (written)
            {
                return;
            }

            written = true;
            var page = SKOutputRasterizer.Capture(context, width, height);
            var svg = string.Create(
                CultureInfo.InvariantCulture,
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{bounds.Width}\" height=\"{bounds.Height}\" viewBox=\"0 0 {bounds.Width} {bounds.Height}\"><image width=\"100%\" height=\"100%\" href=\"data:image/png;base64,{Convert.ToBase64String(page.Png)}\"/></svg>");
            var bytes = Encoding.UTF8.GetBytes(svg);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        return new SKCanvas(context, width, height, SKContextHelper.GetContext(), Flush);
    }
}

public sealed class SKDocument : IDisposable
{
    private enum DocumentKind
    {
        Pdf,
        Xps,
    }

    private sealed class Page
    {
        public required float Width { get; init; }
        public required float Height { get; init; }
        public required DrawingContext Context { get; init; }
        public SKOutputRasterizer.PageData? Captured { get; set; }
    }

    public const float DefaultRasterDpi = 72f;

    private readonly Stream _stream;
    private readonly DocumentKind _kind;
    private readonly List<Page> _pages = new();
    private bool _closed;

    private SKDocument(Stream stream, DocumentKind kind)
    {
        _stream = stream;
        _kind = kind;
    }

    public static SKDocument CreatePdf(SKWStream stream, float dpi = DefaultRasterDpi)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new SKDocument(stream.BaseStream, DocumentKind.Pdf);
    }

    public static SKDocument CreatePdf(Stream stream, float dpi = DefaultRasterDpi)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new SKDocument(stream, DocumentKind.Pdf);
    }

    public static SKDocument CreateXps(SKWStream stream, float dpi = DefaultRasterDpi)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new SKDocument(stream.BaseStream, DocumentKind.Xps);
    }

    public static SKDocument CreateXps(Stream stream, float dpi = DefaultRasterDpi)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new SKDocument(stream, DocumentKind.Xps);
    }

    public SKCanvas BeginPage(float width, float height)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!(width > 0f) || !(height > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Page dimensions must be positive.");
        }

        var page = new Page
        {
            Width = width,
            Height = height,
            Context = new DrawingContext(),
        };
        _pages.Add(page);
        return new SKCanvas(
            page.Context,
            width,
            height,
            SKContextHelper.GetContext(),
            () => Capture(page));
    }

    public void EndPage()
    {
        if (_pages.Count > 0)
        {
            Capture(_pages[^1]);
        }
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        foreach (var page in _pages)
        {
            Capture(page);
        }

        if (_kind == DocumentKind.Pdf)
        {
            WritePdf();
        }
        else
        {
            WriteXps();
        }

        _stream.Flush();
        _closed = true;
    }

    private static void Capture(Page page)
    {
        page.Captured ??= SKOutputRasterizer.Capture(
            page.Context,
            Math.Max(1, (int)MathF.Ceiling(page.Width)),
            Math.Max(1, (int)MathF.Ceiling(page.Height)));
    }

    private void WritePdf()
    {
        var pageCount = _pages.Count;
        var objectCount = 2 + pageCount * 3;
        var objects = new byte[objectCount + 1][];
        objects[1] = Ascii("<< /Type /Catalog /Pages 2 0 R >>");

        var kids = new StringBuilder();
        for (var i = 0; i < pageCount; i++)
        {
            kids.Append(3 + i * 3).Append(" 0 R ");
        }

        objects[2] = Ascii($"<< /Type /Pages /Count {pageCount} /Kids [{kids}] >>");
        for (var i = 0; i < pageCount; i++)
        {
            var page = _pages[i];
            var captured = page.Captured!;
            var pageId = 3 + i * 3;
            var imageId = pageId + 1;
            var contentId = pageId + 2;
            var imageData = CompressRgb(captured.Rgba);
            var imageHeader = Ascii(
                $"<< /Type /XObject /Subtype /Image /Width {captured.Width} /Height {captured.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {imageData.Length} >>\nstream\n");
            objects[imageId] = Combine(imageHeader, imageData, Ascii("\nendstream"));

            var width = page.Width.ToString("0.###", CultureInfo.InvariantCulture);
            var height = page.Height.ToString("0.###", CultureInfo.InvariantCulture);
            var commands = Ascii($"q {width} 0 0 -{height} 0 {height} cm /Im{i + 1} Do Q\n");
            objects[contentId] = Combine(
                Ascii($"<< /Length {commands.Length} >>\nstream\n"),
                commands,
                Ascii("endstream"));
            objects[pageId] = Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /XObject << /Im{i + 1} {imageId} 0 R >> >> /Contents {contentId} 0 R >>");
        }

        using var document = new MemoryStream();
        document.Write(Ascii("%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n"));
        var offsets = new long[objectCount + 1];
        for (var id = 1; id <= objectCount; id++)
        {
            offsets[id] = document.Position;
            document.Write(Ascii($"{id} 0 obj\n"));
            document.Write(objects[id]);
            document.Write(Ascii("\nendobj\n"));
        }

        var xref = document.Position;
        document.Write(Ascii($"xref\n0 {objectCount + 1}\n0000000000 65535 f \n"));
        for (var id = 1; id <= objectCount; id++)
        {
            document.Write(Ascii($"{offsets[id]:D10} 00000 n \n"));
        }

        document.Write(Ascii($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        document.Position = 0;
        document.CopyTo(_stream);
    }

    private void WriteXps()
    {
        using var package = new ZipArchive(_stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(package, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/FixedDocSeq.fdseq\" ContentType=\"application/vnd.ms-package.xps-fixeddocumentsequence+xml\"/><Override PartName=\"/Documents/1/FixedDoc.fdoc\" ContentType=\"application/vnd.ms-package.xps-fixeddocument+xml\"/></Types>");
        WriteEntry(package, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"R1\" Type=\"http://schemas.microsoft.com/xps/2005/06/fixedrepresentation\" Target=\"/FixedDocSeq.fdseq\"/></Relationships>");
        WriteEntry(package, "FixedDocSeq.fdseq",
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><FixedDocumentSequence xmlns=\"http://schemas.microsoft.com/xps/2005/06\"><DocumentReference Source=\"/Documents/1/FixedDoc.fdoc\"/></FixedDocumentSequence>");

        var pages = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?><FixedDocument xmlns=\"http://schemas.microsoft.com/xps/2005/06\">");
        for (var i = 0; i < _pages.Count; i++)
        {
            pages.Append($"<PageContent Source=\"/Documents/1/Pages/{i + 1}.fpage\"/>");
            var page = _pages[i];
            var width = page.Width.ToString("0.###", CultureInfo.InvariantCulture);
            var height = page.Height.ToString("0.###", CultureInfo.InvariantCulture);
            WriteEntry(package, $"Documents/1/Pages/{i + 1}.fpage",
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?><FixedPage xmlns=\"http://schemas.microsoft.com/xps/2005/06\" Width=\"{width}\" Height=\"{height}\"><Path Data=\"M 0,0 L {width},0 {width},{height} 0,{height} Z\"><Path.Fill><ImageBrush ImageSource=\"/Resources/Images/{i + 1}.png\" Viewbox=\"0,0,1,1\" ViewboxUnits=\"RelativeToBoundingBox\" Viewport=\"0,0,1,1\" ViewportUnits=\"RelativeToBoundingBox\"/></Path.Fill></Path></FixedPage>");
            WriteBinaryEntry(package, $"Resources/Images/{i + 1}.png", page.Captured!.Png);
        }

        pages.Append("</FixedDocument>");
        WriteEntry(package, "Documents/1/FixedDoc.fdoc", pages.ToString());
    }

    private static byte[] CompressRgb(byte[] rgba)
    {
        var rgb = GC.AllocateUninitializedArray<byte>(rgba.Length / 4 * 3);
        for (int source = 0, destination = 0; source < rgba.Length; source += 4)
        {
            rgb[destination++] = rgba[source];
            rgb[destination++] = rgba[source + 1];
            rgb[destination++] = rgba[source + 2];
        }

        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressor.Write(rgb, 0, rgb.Length);
        }

        return output.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.Latin1.GetBytes(value);

    private static byte[] Combine(params byte[][] buffers)
    {
        var length = buffers.Sum(static buffer => buffer.Length);
        var result = GC.AllocateUninitializedArray<byte>(length);
        var offset = 0;
        foreach (var buffer in buffers)
        {
            Buffer.BlockCopy(buffer, 0, result, offset, buffer.Length);
            offset += buffer.Length;
        }

        return result;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content) =>
        WriteBinaryEntry(archive, path, Encoding.UTF8.GetBytes(content));

    private static void WriteBinaryEntry(ZipArchive archive, string path, byte[] content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    public void Dispose() => Close();
}

internal static class SKOutputRasterizer
{
    internal sealed record PageData(int Width, int Height, byte[] Rgba, byte[] Png);

    public static PageData Capture(DrawingContext context, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Context.Append(context);
            canvas.Flush();
        }

        context.Clear();
        var rgba = bitmap.CopyRgba8888Rows();
        Unpremultiply(rgba);
        using var output = new MemoryStream();
        var writer = new StbImageWriteSharp.ImageWriter();
        writer.WritePng(rgba, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        return new PageData(width, height, rgba, output.ToArray());
    }

    private static void Unpremultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha is 0 or 255)
            {
                continue;
            }

            pixels[i] = (byte)Math.Min(255, (pixels[i] * 255 + alpha / 2) / alpha);
            pixels[i + 1] = (byte)Math.Min(255, (pixels[i + 1] * 255 + alpha / 2) / alpha);
            pixels[i + 2] = (byte)Math.Min(255, (pixels[i + 2] * 255 + alpha / 2) / alpha);
        }
    }
}
