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

    public static bool UsesUniversalShapingEngine(OpenTypeTag script)
    {
        string value = script.ToString().ToLowerInvariant();
        return value is
            "bng3" or "dev3" or "gjr3" or "gur3" or "knd3" or "mlm3" or "ory3" or "tml3" or "tel3" or
            "tibt" or "mong" or "sinh" or "java" or "marc" or "limb" or "tale" or "bugi" or "khar" or
            "sylo" or "tfng" or "bali" or "nkoo" or "phag" or "cham" or "kali" or "lepc" or "rjng" or
            "saur" or "sund" or "egyp" or "kthi" or "mtei" or "lana" or "tavt" or "batk" or "brah" or
            "mand" or "cakm" or "plrd" or "shrd" or "takr" or "dupl" or "gran" or "khoj" or "sind" or
            "mahj" or "mani" or "modi" or "hmng" or "phlp" or "sidd" or "tirh" or "ahom" or "mult" or
            "adlm" or "bhks" or "newa" or "gonm" or "soyo" or "zanb" or "dogr" or "gong" or "rohg" or
            "maka" or "medf" or "sogo" or "sogd" or "elym" or "nand" or "hmnp" or "wcho" or "chrs" or
            "diak" or "kits" or "yezi" or "cpmn" or "ougr" or "tnsa" or "toto" or "vith" or "kawi" or "nagm";
    }
}
