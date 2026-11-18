// Usage: AsmScan <assembly.dll> [out.txt]
// Writes one block per type: "namespace.Type : Base" then its methods/properties.
// Reads metadata only (no loading/executing of game code).
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 1) { Console.Error.WriteLine("usage: AsmScan <dll> [out.txt]"); return 1; }

using var fs = File.OpenRead(args[0]);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();
var output = args.Length > 1 ? new StreamWriter(args[1]) : Console.Out;

string Name(EntityHandle h)
{
    if (h.IsNil) return "";
    switch (h.Kind)
    {
        case HandleKind.TypeDefinition:
            var td = md.GetTypeDefinition((TypeDefinitionHandle)h);
            return Full(md.GetString(td.Namespace), md.GetString(td.Name));
        case HandleKind.TypeReference:
            var tr = md.GetTypeReference((TypeReferenceHandle)h);
            return Full(md.GetString(tr.Namespace), md.GetString(tr.Name));
        case HandleKind.TypeSpecification:
            return "<generic>";
        default: return h.Kind.ToString();
    }
}
static string Full(string ns, string n) => string.IsNullOrEmpty(ns) ? n : ns + "." + n;

// Il2CppInterop plumbing we don't care about.
static bool Noise(string n) =>
    n.StartsWith("NativeMethodInfoPtr_") || n.StartsWith("NativeFieldInfoPtr_") ||
    n == ".cctor" || n.StartsWith("get_") || n.StartsWith("set_");

int types = 0;
foreach (var th in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(th);
    string name = md.GetString(t.Name);
    if (name == "<Module>" || name.StartsWith("<")) continue;
    string full = Full(md.GetString(t.Namespace), name);
    if (!t.GetDeclaringType().IsNil) full = Name(t.GetDeclaringType()) + "/" + name;
    types++;
    output.WriteLine($"TYPE {full} : {Name(t.BaseType)}");
    if (Name(t.BaseType) == "System.Enum")
        foreach (var fh in t.GetFields())
        {
            var f = md.GetFieldDefinition(fh);
            if ((f.Attributes & FieldAttributes.Static) == 0) continue; // skip value__
            var c = f.GetDefaultValue();
            string val = c.IsNil ? "?" : md.GetBlobReader(md.GetConstant(c).Value).ReadConstant(md.GetConstant(c).TypeCode)?.ToString();
            output.WriteLine($"  E {md.GetString(f.Name)} = {val}");
        }
    foreach (var ph in t.GetProperties())
    {
        var pd = md.GetPropertyDefinition(ph);
        string ptype;
        try { ptype = pd.DecodeSignature(new SigNames(md), null).ReturnType; } catch { ptype = "?"; }
        output.WriteLine($"  P {md.GetString(pd.Name)} : {ptype}");
    }
    foreach (var mh in t.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        string mn = md.GetString(m.Name);
        if (Noise(mn)) continue;
        var sig = m.DecodeSignature(new SigNames(md), null);
        var pnames = m.GetParameters().Select(p => md.GetParameter(p)).Where(p => p.SequenceNumber > 0)
                      .Select(p => md.GetString(p.Name)).ToList();
        var ps = string.Join(", ", sig.ParameterTypes.Select((pt, i) => i < pnames.Count ? $"{pt} {pnames[i]}" : pt));
        output.WriteLine($"  M {mn}({ps}) : {sig.ReturnType}");
    }
}
output.Flush();
Console.Error.WriteLine($"{types} types");
return 0;

/// Turns signature blobs into readable type names (short names, good enough for recon).
sealed class SigNames : ISignatureTypeProvider<string, object>
{
    readonly MetadataReader md;
    public SigNames(MetadataReader md) => this.md = md;
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString().ToLowerInvariant();
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    { var t = r.GetTypeDefinition(h); var n = r.GetString(t.Name);
      return t.GetDeclaringType().IsNil ? n : GetTypeFromDefinition(r, t.GetDeclaringType(), k) + "/" + n; }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    { var t = r.GetTypeReference(h); var n = r.GetString(t.Name);
      return t.ResolutionScope.Kind == HandleKind.TypeReference ? GetTypeFromReference(r, (TypeReferenceHandle)t.ResolutionScope, k) + "/" + n : n; }
    public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, c);
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericTypeParameter(object c, int i) => "T" + i;
    public string GetGenericMethodParameter(object c, int i) => "M" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetModifiedType(string mod, string unmod, bool req) => unmod;
    public string GetPinnedType(string e) => e;
}
