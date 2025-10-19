module Program

open Avalonia
open Window

[<EntryPoint>]
let main args =
    AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .UseSkia()
        .WithInterFont()
        .StartWithClassicDesktopLifetime args
