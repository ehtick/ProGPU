using ProGPU.Text.Shaping;

namespace ProGPU.Text;

/// <summary>Resolves Unicode 17 script data to four-byte OpenType layout tags.</summary>
public static class OpenTypeScriptResolver
{
    public static OpenTypeTag GetScript(uint codePoint)
    {
        string script = UnicodeScriptData.GetScript(codePoint) switch
        {
            "hira" => "kana",
            "laoo" => "lao ",
            { Length: 4 } value => value,
            _ => "DFLT"
        };
        return new OpenTypeTag(script);
    }

    public static OpenTypeTag Infer(ReadOnlySpan<uint> codePoints)
    {
        foreach (uint codePoint in codePoints)
        {
            OpenTypeTag script = GetScript(codePoint);
            if (script != OpenTypeTag.DefaultScript) return script;
        }
        return OpenTypeTag.DefaultScript;
    }
}
