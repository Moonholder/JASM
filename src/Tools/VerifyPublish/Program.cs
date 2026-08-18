using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: VerifyPublish <assembly-path> <Type|Member> [<Type|Member> ...]");
    return 2;
}

var assemblyPath = args[0];
using var stream = File.OpenRead(assemblyPath);
using var peReader = new PEReader(stream);
var metadata = peReader.GetMetadataReader();

var failures = 0;
for (var i = 1; i < args.Length; i++)
{
    var pair = args[i].Split('|');
    if (pair.Length != 2)
    {
        Console.Error.WriteLine($"Invalid member spec: {args[i]}");
        failures++;
        continue;
    }

    var found = TryFindType(metadata, pair[0], out var typeHandle)
                && HasMethod(metadata, typeHandle, pair[1]);
    Console.WriteLine($"{(found ? "OK  " : "FAIL")} {pair[0]}.{pair[1]}");
    if (!found)
        failures++;
}

return failures == 0 ? 0 : 1;

static bool TryFindType(MetadataReader md, string fullName, out TypeDefinitionHandle handle)
{
    var lastDot = fullName.LastIndexOf('.');
    var ns = lastDot >= 0 ? fullName[..lastDot] : string.Empty;
    var name = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;

    foreach (var tdh in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(tdh);
        if (md.StringComparer.Equals(td.Namespace, ns) && md.StringComparer.Equals(td.Name, name))
        {
            handle = tdh;
            return true;
        }
    }

    handle = default;
    return false;
}

static bool HasMethod(MetadataReader md, TypeDefinitionHandle typeHandle, string methodName)
{
    var td = md.GetTypeDefinition(typeHandle);
    foreach (var mh in td.GetMethods())
    {
        if (md.StringComparer.Equals(md.GetMethodDefinition(mh).Name, methodName))
            return true;
    }

    return false;
}
