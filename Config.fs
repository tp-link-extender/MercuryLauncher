module Config

open System.IO
open System.Reflection

let name = "Mercury"
let domain = "mercs.dev"

let readEmbeddedResource (resourceName: string) =
    let assembly = Assembly.GetExecutingAssembly()
    use stream = assembly.GetManifestResourceStream resourceName

    if isNull stream then
        failwithf $"Resource '{resourceName}' not found in assembly."
    else
        use memoryStream = new MemoryStream()
        stream.CopyTo memoryStream
        memoryStream.ToArray()

let icon = readEmbeddedResource "MercuryLauncher.iconlight.png"
