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
    | FailedToRemoveOldVersions of int
    | FailedToLaunch of exn
    | FailedToRegister of exn

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
    if Environment.OSVersion.Platform = PlatformID.Win32NT then
        Path.Combine(versionPath s v, $"{name}Launcher.exe")
    else
        Path.Combine(versionPath s v, $"{name}Launcher")

let playerPath s v =
    Path.Combine(versionPath s v, $"{name}PlayerBeta.exe")

let studioPath s v =
    Path.Combine(versionPath s v, $"{name}StudioBeta.exe")

let appData () =
    if Environment.OSVersion.Platform = PlatformID.Win32NT then
        Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData
    else
        "/usr/share"

let getPath (v: string) =
    let path = [| appData (); name |]

    Ok(path |> Path.Combine, v)

let downloadBuffer = 8192

let rec readStream (write: byte[] -> int -> unit) (s: Stream) =
    let buffer = Array.zeroCreate<byte> downloadBuffer
    let r = s.Read(buffer, 0, buffer.Length)

    if r > 0 then
        write buffer r
        readStream write s
    else
        ()

let downloadClient (client: HttpClient) (u: Event<Update>) v =
    try
        use response =
            client.GetAsync($"{setupUrl}/{v}", HttpCompletionOption.ResponseHeadersRead).Result

        u.Trigger(Indeterminate false)

        match response.StatusCode with
        | HttpStatusCode.OK ->
            let contentLength = response.Content.Headers.ContentLength

            let upd =
                match Option.ofNullable contentLength with
                | Some length ->
                    fun (r: int64) ->
                        let progress = float (r * 100L) / float length
                        printfn $"Download progress: {progress:F2}%%"
                        // MEMORY LEEEEEEEEEEEAK!!!!!!!!!!!!!!!!!!
                        u.Trigger(Progress progress)
                        u.Trigger(Text $"Downloading client... ({r / 1000L}k / {length / 1000L}k)")

                        // check memory usage
                        let pc = Process.GetCurrentProcess()
                        let mem = pc.PrivateMemorySize64 / 1024L / 1024L
                        printfn $"Memory usage: {mem} MB"
                | None -> fun (_: int64) -> ()

            use stream = response.Content.ReadAsStreamAsync().Result
            use ms = new MemoryStream()

            let write (b: byte[]) (r: int) =
                ms.Write(b, 0, r)
                upd ms.Length

            readStream write stream

            Ok(ms.ToArray())
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

let suMove (src: string) (dst: string) =
    let cmd = $"mkdir -p \"{dst}\" && mv -f \"{src}/*\" \"{dst}\""
    let cmd2 = cmd.Replace("\"", "\\\"")

    let psi =
        ProcessStartInfo(
            FileName = "pkexec",
            Arguments = $"sh -c \"{cmd2}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        )

    let proc = Process.Start psi

    if proc = null then
        Error "Failed to start move process"
    else
        proc.WaitForExit()
        let exitCode = proc.ExitCode
        let error = proc.StandardError.ReadToEnd()
        Ok(exitCode, error)

let untarClient p v (tar: MemoryStream) =
    let path = versionPath p v

    try
        let tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory tempDir |> ignore

        printfn $"Extracting to temp dir: {tempDir}"

        // extract the tar file
        Formats.Tar.TarFile.ExtractToDirectory(tar, tempDir, true)

        printfn $"Moving to final dir: {path}"

        // move the temp directory to the final location
        if Environment.OSVersion.Platform = PlatformID.Win32NT then
            // on windows we can just do a normal move
            if Directory.Exists path then
                Directory.Delete(path, true)

            Directory.Move(tempDir, path)
            Ok(p, v)
        else
            // on linux we need sudo permissions to move to /usr/share
            match suMove tempDir path with
            | Ok(exitCode, error) ->
                printfn $"move exited with code: {exitCode}"

                if exitCode <> 0 then
                    Error(FailedToInstall(Exception $"move failed: {error}"))
                else
                    Ok(p, v)
            | Error msg -> Error(FailedToInstall(Exception msg))
    with e ->
        Error(FailedToInstall e)

let ensurePath (p, v) = Ok(File.Exists(playerPath p v), p, v)

let startProcess (exePath: string) (args: string array) = Process.Start(exePath, args)

let startProcessWine (exePath: string) (args: string array) =
    let allArgs = Array.append [| exePath |] args

    Process.Start("wine", String.Join(" ", allArgs))

let launch ticket (p, v) =
    let procArgs = [| $"--play"; "-a"; authUrl; "-t"; authTicket; "-j"; joinUrl ticket |]

    try
        let fn =
            if Environment.OSVersion.Platform = PlatformID.Win32NT then
                startProcess
            else
                startProcessWine

        Ok(fn (playerPath p v) procArgs)
    with e ->
        Error(FailedToLaunch e)

// Register the protocol handler to this application
let registerURIWindows (p, v) =
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
        Error(FailedToRegister e)

let addToMimeapps (dir: string) (filename: string) =
    let mimeappsList = Path.Combine(dir, "mimeapps.list")

    if File.Exists mimeappsList then
        File.AppendAllText(mimeappsList, $"\nx-scheme-handler/{launcherScheme}={filename};\n")
    else
        File.WriteAllText(
            mimeappsList,
            $"[Default Applications]\n\
            x-scheme-handler/{launcherScheme}={filename};\n"
        )

let registerURILinux (p, v) =
    let applicationsDir = "/usr/share/applications"

    Directory.CreateDirectory applicationsDir |> ignore

    printfn $"Applications dir: {applicationsDir}"
    let execPath = launcherPath p v

    let desktopContents =
        $"[Desktop Entry]\n\
            Name={name} Launcher\n\
            Comment=Handle {launcherScheme} links\n\
            Exec=\"{execPath}\" %%U\n\
            Type=Application\n\
            Terminal=false\n\
            NoDisplay=false\n\
            Categories=Utility;Application;\n\
            URL={launcherScheme}:\n\
            MimeType=x-scheme-handler/{launcherScheme};\n"

    printfn $"{desktopContents}"

    let desktopFilename = $"{name.ToLowerInvariant()}-launcher.desktop"
    let desktopPath = Path.Combine(applicationsDir, desktopFilename)

    try
        File.WriteAllText(desktopPath, desktopContents)
        addToMimeapps applicationsDir desktopFilename

        // also write to local applications dir
        let localApplicationsDir =
            Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData, "applications")

        Directory.CreateDirectory localApplicationsDir |> ignore

        let localDesktopPath = Path.Combine(localApplicationsDir, desktopFilename)
        File.WriteAllText(localDesktopPath, desktopContents)
        addToMimeapps localApplicationsDir desktopFilename

        // now register the handler
        let psi =
            ProcessStartInfo(
                FileName = "xdg-mime",
                Arguments = $"default {desktopFilename} x-scheme-handler/{launcherScheme}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            )

        use proc = Process.Start psi
        proc.WaitForExit()

        // Since that all didn't seem to work, I'm seriously out of ideas now

        if proc.ExitCode <> 0 then
            let err = proc.StandardError.ReadToEnd()
            Error(FailedToRegister(Exception $"xdg-mime failed: {err}"))
        else
            Ok(p, v)
    with e ->
        Error(FailedToRegister e)

let checkThatItLaunchedCorrectly (p: Process) =
    if p.HasExited then Error ClientNotFound else Ok()

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
            Error(FailedToRemoveOldVersions failedVersions)
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
    | FailedToRemoveOldVersions n ->
        let ex = if n = 1 then "" else "s"

        c.Trigger(
            ErrorMessage
                $"Failed to remove {n} old version{ex} of the {name} client.\n\
                Please make sure write permissions are given to the versions directory."
        )
    | FailedToLaunch ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to launch {name}.\n\
                \n\
                Details: {ex.Message}"
        )
    | FailedToRegister ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to register the {name} protocol handler.\n\
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
        downloadClient client u v
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

let yes x a =
    x ()
    Ok a

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
        >>= yes (fun () -> log "register time" |> ignore)
        >>= trigger u (Text "Registering protocol...")
        >>= (if Environment.OSVersion.Platform = PlatformID.Win32NT then
                 registerURIWindows
             else
                 registerURILinux)
        >>= yes (fun () -> log "registered" |> ignore)
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
