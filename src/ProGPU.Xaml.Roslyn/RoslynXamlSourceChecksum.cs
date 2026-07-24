using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace ProGPU.Xaml.Roslyn;

/// <summary>
/// Computes host- and source-encoding-independent identities from the Unicode
/// source sequence using UTF-8 without a preamble and SHA-256.
/// </summary>
public static class RoslynXamlSourceChecksum
{
    private static readonly Encoding CanonicalEncoding =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public static string ComputeHex(SourceText sourceText)
    {
        if (sourceText == null)
            throw new ArgumentNullException(nameof(sourceText));
        var bytes = CanonicalEncoding.GetBytes(sourceText.ToString());
        byte[] checksum;
        using (var algorithm = SHA256.Create())
            checksum = algorithm.ComputeHash(bytes);
        var builder = new StringBuilder(checksum.Length * 2);
        foreach (var value in checksum)
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
