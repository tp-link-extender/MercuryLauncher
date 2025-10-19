module Config

open System.Reflection
open System.IO

let name = "Mercury"
let domain = "xtcy.dev"

// Read the contents of an embedded resource as bytes
let readEmbeddedResourceAsBytes (resourceName: string) =
    let assembly = Assembly.GetExecutingAssembly()
    use stream = assembly.GetManifestResourceStream resourceName

    if isNull stream then
        failwithf "Resource '%s' not found in assembly." resourceName
    else
        use memoryStream = new MemoryStream()
        stream.CopyTo memoryStream
        memoryStream.ToArray()

let icon = readEmbeddedResourceAsBytes "MercuryLauncher.icon.png"
