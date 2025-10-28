module Program

open Avalonia
open Config
open FSharp.Core.Result
open System.IO
open System.Diagnostics
open System.IO.Compression
open System.Net
open System.Net.Http
open System
open Window
open Microsoft.Win32

type ErrorType =
    | VersionTooLong
    | VersionMissing
    | VersionFailedToGet of HttpStatusCode
    | ClientFailedToGet of HttpStatusCode
    | FailedToConnect of exn
    | FailedToDownload of exn
    | FailedToUnpack of exn
    | FailedToInstall of exn
    | ClientNotFound
    | FailedToRemoveOldVersions
    | FailedToLaunch of exn
    | BadLaunch of exn

let (>>=) f x = bind x f

let log i =
    printfn $"[LOG] {i}"
    Ok i

let url = $"https://{domain}"
let setupUrl = $"https://setup.{domain}"
let versionUrl = $"{setupUrl}/version"
let authUrl = $"{url}/negotiate" // /Login/Negotiate.ashx
let joinUrl ticket = $"{url}/game/join?ticket=%s{ticket}"
let launcherScheme = $"{name.ToLowerInvariant()}-launcher"
let authTicket = "test" // LRORL

let requestVersion (client: HttpClient) =
    try
        use response = (client.GetAsync versionUrl).Result

        match response.StatusCode with
        | HttpStatusCode.OK -> Ok(response.Content.ReadAsStringAsync().Result)
        | code -> Error(VersionFailedToGet code)
    with
    | :? AggregateException as e -> Error(FailedToConnect e.InnerException)
    | e -> Error(FailedToConnect e)

let validateVersion (v: string) =
    if v.Length > 20 then Error VersionTooLong
    elif v.Length = 0 then Error VersionMissing
    else Ok v

// add the versions directory to the path
let versionsPath s = Path.Combine(s, "Versions")

// add the version to the path
let versionPath s v =
    Path.Combine(versionsPath s, $"version-%s{v}")

let launcherPath s v =
    Path.Combine(versionPath s v, $"{name}Launcher.exe")

let playerPath s v =
    Path.Combine(versionPath s v, $"{name}PlayerBeta.exe")

let studioPath s v =
    Path.Combine(versionPath s v, $"{name}StudioBeta.exe")

let getPath (v: string) =
    let path = [|
        Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
        name
    |]

    Ok(path |> Path.Combine, v)

let downloadClient (client: HttpClient) v =
    try
        use response = (client.GetAsync $"{setupUrl}/{v}").Result

        match response.StatusCode with
        | HttpStatusCode.OK -> Ok(response.Content.ReadAsByteArrayAsync().Result)
        | code -> Error(ClientFailedToGet code)
    with
    | :? AggregateException as e -> Error(FailedToDownload e.InnerException)
    | e -> Error(FailedToDownload e)

let ungzipClient (data: byte array) =
    // we have the data, we'd like to un-gzip it
    try
        let tar = new MemoryStream()

        (new GZipStream(new MemoryStream(data), CompressionMode.Decompress)).CopyTo tar

        tar.Seek(0, SeekOrigin.Begin) |> ignore
        Ok tar
    with e ->
        Error(FailedToUnpack e)

let untarClient p v (tar: MemoryStream) =
    let path = versionPath p v

    try
        // create the directory if it doesn't exist
        Directory.CreateDirectory path |> ignore

        // extract the tar file
        Formats.Tar.TarFile.ExtractToDirectory(tar, path, true)

        Ok(p, v)
    with e ->
        Error(FailedToInstall e)

let ensurePath (p, v) = Ok(File.Exists(playerPath p v), p, v)

let launch ticket (p, v) =
    let procArgs = [| $"--play"; "-a"; authUrl; "-t"; authTicket; "-j"; joinUrl ticket |]

    try
        Ok(Process.Start(playerPath p v, procArgs))
    with e ->
        Error(FailedToLaunch e)

// Register the protocol handler to this application
let registerURI (p, v) =
    try
        let key =
            Registry.CurrentUser.CreateSubKey($"Software\\Classes\\{launcherScheme}", true)

        // same as reg structure created by 2016 launcher
        key.SetValue("", $"URL: {name} Protocol")
        key.SetValue("URL Protocol", "")

        let shellKey = key.CreateSubKey "shell"
        let openKey = shellKey.CreateSubKey "open"
        let commandKey = openKey.CreateSubKey "command"

        let exePath = launcherPath p v
        commandKey.SetValue("", $"\"{exePath}\" %%1")

        Ok(p, v)
    with e ->
        Error(BadLaunch e)

let checkThatItLaunchedCorrectly (p: Process) =
    try
        if p.HasExited then Error ClientNotFound else Ok()
    with e ->
        Error(FailedToLaunch e)

let clearOldVersions (p: string) v () =
    let path = versionsPath p

    if Directory.Exists path then
        let failedVersions =
            Directory.GetDirectories path
            |> Array.filter (fun d -> d <> versionPath p v)
            |> Array.map (fun d ->
                try
                    Directory.Delete(d, true)
                    Ok()
                with e ->
                    Error e)
            |> Array.filter _.IsError
            |> _.Length

        if failedVersions = 0 then
            Ok()
        else
            Error FailedToRemoveOldVersions
    else
        Error ClientNotFound

let handleError (c: Event<Control>) =
    function
    | VersionTooLong ->
        c.Trigger(
            ErrorMessage
                $"There was an error when trying to get the version from {name}.\n\
                The version string for {name} is too long."
        )
    | VersionMissing ->
        c.Trigger(
            ErrorMessage
                $"There was an error when trying to get the version from {name}.\n\
                The version string for {name} is missing."
        )
    | VersionFailedToGet code ->
        c.Trigger(
            ErrorMessage
                $"There was an error when trying to get the version from {name}.\n\
                The server returned a {code} status code."
        )
    | ClientFailedToGet code ->
        c.Trigger(
            ErrorMessage
                $"There was an error when trying to download the {name} client.\n\
                The server returned a {code} status code."
        )
    | FailedToConnect ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to connect to {name}.\n\
                Please check your internet connection and try again.\n\
                \n\
                Details: {ex.Message}"
        )
    | FailedToDownload ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to download the {name} client.\n\
                Please check your internet connection and try again.\n\
                \n\
                Details: {ex.Message}"
        )
    | FailedToUnpack ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to unpack the {name} client.\n\
                \n\
                Details: {ex.Message}"
        )
    | FailedToInstall ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to install the {name} client.\n\
                Please make sure write permissions are given to the installation directory, and there are no existing files with the same name.\n\
                \n\
                Details: {ex.Message}"
        )
    | ClientNotFound ->
        c.Trigger(
            ErrorMessage
                $"The {name} client was not found.\n\
                Please make sure that the client is installed and try again."
        )
    | FailedToRemoveOldVersions ->
        c.Trigger(
            ErrorMessage
                $"Failed to remove old versions of the {name} client.\n\
                Please make sure write permissions are given to the versions directory."
        )
    | FailedToLaunch ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to launch {name}.\n\
                \n\
                Details: {ex.Message}"
        )
    | BadLaunch ex ->
        c.Trigger(
            ErrorMessage
                $"The {name} client launched, but it did not start correctly.\n\
                \n\
                Details: {ex.Message}"
        )

let trigger (u: Event<Update>) update d =
    u.Trigger update
    Ok d

let downloadAndInstall (client: HttpClient) (u: Event<Update>) (d, p, v) =
    if d then
        Ok(p, v)
    else
        downloadClient client v
        >>= trigger u (Text "Unpacking client...")
        >>= ungzipClient
        >>= trigger u (Text "Installing client...")
        >>= untarClient p v

let launchAndComplete (c: Event<Control>) (u: Event<Update>) ticket (p, v) =
    if ticket = "" then
        u.Trigger(Text $"Clearing old versions...")
        let r = clearOldVersions p v ()

        if r.IsOk then
            u.Trigger(Progress 100)
            u.Trigger(Indeterminate false)
            u.Trigger(Text "Done!")

            c.Trigger(SuccessMessage $"{name} has been successfully installed and is ready to use!")

        // TODO: redirect to site
        r
    else
        u.Trigger(Text $"Starting {name}...")

        launch ticket (p, v)
        >>= trigger u (Text $"Finishing up...")
        >>= checkThatItLaunchedCorrectly
        >>= trigger u (Text $"Clearing old versions...")
        >>= clearOldVersions p v

let init ticket (c: Event<Control>) (u: Event<Update>) =
    u.Trigger(Text $"Connecting to {name}...")

    use client = new HttpClient()

    let result =
        requestVersion client
        >>= log
        >>= validateVersion
        >>= getPath
        >>= log
        >>= trigger u (Text "Getting client...")
        >>= ensurePath
        >>= log
        >>= trigger u (Text "Downloading client...")
        >>= downloadAndInstall client u
        >>= log
        >>= trigger u (Text "Registering protocol...")
        >>= registerURI
        >>= launchAndComplete c u ticket

    match result with
    | Ok _ ->
        u.Trigger(Progress 100)
        u.Trigger(Indeterminate false)
        u.Trigger(Text "Done!")
        c.Trigger Shutdown
    | Error e -> handleError c e // this will trigger the shutdown itself

    printfn "done"

let startApp xfn =
    AppBuilder
        .Configure(fun () -> App xfn)
        .UsePlatformDetect()
        .UseSkia()
        .WithInterFont()
        .StartWithClassicDesktopLifetime
        [||]

[<EntryPoint; STAThread>]
let main args =
    let ticket =
        if args = [||] then
            ""
        else
            let mainArg = args[0]

            if not (mainArg.StartsWith launcherScheme) then
                // control.Trigger(ErrorMessage $"The first argument must be a {launcherScheme} URL.")
                // printfn $"The first argument must be a {launcherScheme} URL." |> ignore

                Environment.Exit 1

            mainArg.Substring(launcherScheme.Length + 1)

    // start app as a thread
    startApp (init ticket)
