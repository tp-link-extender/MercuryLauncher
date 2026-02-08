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
    | FailedToGet of string * HttpStatusCode
    | FailedToDownload of string * exn
    | FailedToInstall of string * exn
    | FailedToConnect of exn
    | FailedToUnpack of exn
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

let launcherFilename () =
    if Environment.OSVersion.Platform = PlatformID.Win32NT then
        $"{name}Launcher_win-x64.exe"
    else
        $"{name}Launcher_linux-x64"

let launcherPath s v =
    Path.Combine(versionPath s v, launcherFilename ())

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

let download (client: HttpClient) (u: Event<Update list>) thing path =
    try
        use response =
            client.GetAsync($"{setupUrl}/{path}", HttpCompletionOption.ResponseHeadersRead).Result

        u.Trigger [ Indeterminate false ]

        match response.StatusCode with
        | HttpStatusCode.OK ->
            let contentLength = response.Content.Headers.ContentLength

            let upd =
                match Option.ofNullable contentLength with
                | Some length ->
                    fun (r: int64) ->
                        // THE MEMORY LEAK IS GONE
                        u.Trigger [
                            Progress(float (r * 100L) / float length)
                            Text $"Downloading {thing}... ({r / 1000L}k / {length / 1000L}k)"
                        ]
                | None -> fun (_: int64) -> ()

            use stream = response.Content.ReadAsStreamAsync().Result
            use ms = new MemoryStream()

            let write (b: byte[]) (r: int) =
                ms.Write(b, 0, r)
                upd ms.Length

            readStream write stream

            Ok(ms.ToArray())
        | code -> Error(FailedToGet(thing, code))
    with
    | :? AggregateException as e -> Error(FailedToDownload(thing, e.InnerException))
    | e -> Error(FailedToDownload(thing, e))

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
        let tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        Directory.CreateDirectory tempDir |> ignore

        printfn $"Extracting to temp dir: {tempDir}"

        // extract the tar file
        Formats.Tar.TarFile.ExtractToDirectory(tar, tempDir, true)

        printfn $"Moving to final dir {path}"

        // move the temp directory to the final location
        if Directory.Exists path then
            Directory.Delete(path, true)

        if not (Directory.Exists tempDir) then
            Error(FailedToInstall("client", Exception "Temporary extraction directory does not exist"))
        else
            printfn $"Moving directory from {tempDir}"

            // copy directory contents
            Directory.CreateDirectory path |> ignore

            for p in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories) do
                let relativePath = p.Substring(tempDir.Length).TrimStart Path.DirectorySeparatorChar
                let destPath = Path.Combine(path, relativePath)

                // get what parent of the dest path would be
                let destDir = Path.GetDirectoryName destPath

                if not (Directory.Exists destDir) then
                    Directory.CreateDirectory destDir |> ignore

                File.Copy(p, destPath)

            // delete the temp directory
            Directory.Delete(tempDir, true)

            printfn $"Client installed to {path}"

            Ok(p, v)

    with e ->
        Error(FailedToInstall("client", e))

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
            |> Array.filter (fun d ->
                printfn "comparing %s to %s" d (versionPath p v)
                d <> versionPath p v)
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
    | FailedToGet(thing, code) ->
        c.Trigger(
            ErrorMessage
                $"There was an error when trying to download the {name} {thing}.\n\
                The server returned a {code} status code."
        )
    | FailedToDownload(thing, ex) ->
        c.Trigger(
            ErrorMessage
                $"Failed to download the {name} {thing}.\n\
                Please check your internet connection and try again.\n\
                \n\
                Details: {ex.Message}"
        )
    | FailedToInstall(thing, ex) ->
        c.Trigger(
            ErrorMessage
                $"Failed to install the {name} {thing}.\n\
                Please make sure write permissions are given to the installation directory, and there are no existing files with the same name.\n\
                \n\
                Details: {ex.Message}"
        )
    | FailedToConnect ex ->
        c.Trigger(
            ErrorMessage
                $"Failed to connect to {name}.\n\
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

let trigger (u: Event<Update list>) updates d =
    u.Trigger updates
    Ok d

let saveLauncher (p, v) (data: byte array) =
    let path = launcherPath p v

    try
        File.WriteAllBytes(path, data)
        Ok(p, v)
    with e ->
        Error(FailedToInstall("launcher", e))

let yes x a =
    x ()
    Ok a

let downloadAndInstallClient (client: HttpClient) (u: Event<Update list>) (d, p, v) =
    if d then
        Ok(p, v)
    else
        download client u "client" v
        >>= trigger u [ Indeterminate true; Text "Unpacking client..." ]
        >>= ungzipClient
        >>= trigger u [ Text "Installing client..." ]
        >>= untarClient p v

let downloadAndInstallLauncher (client: HttpClient) (u: Event<Update list>) (p, v) =

    download client u "launcher" (launcherFilename ())
    >>= trigger u [ Text "Installing launcher..." ]
    >>= saveLauncher (p, v)

let launchAndComplete (c: Event<Control>) (u: Event<Update list>) ticket (p, v) =
    if ticket = "" then
        u.Trigger [ Text $"Clearing old versions..." ]
        let r = clearOldVersions p v ()

        if r.IsOk then
            u.Trigger [ Progress 100; Indeterminate false; Text "Done!" ]

            c.Trigger(SuccessMessage $"{name} has been successfully installed and is ready to use!")

        // TODO: redirect to site
        r
    else
        u.Trigger [ Text $"Starting {name}..." ]

        launch ticket (p, v)
        >>= trigger u [ Text $"Finishing up..." ]
        >>= checkThatItLaunchedCorrectly
        >>= trigger u [ Text $"Clearing old versions..." ]
        >>= clearOldVersions p v

let init ticket (c: Event<Control>) (u: Event<Update list>) =
    u.Trigger [ Text $"Connecting to {name}..."; Indeterminate false ]

    use client = new HttpClient()

    let result =
        requestVersion client
        >>= log
        >>= validateVersion
        >>= getPath
        >>= log
        >>= trigger u [ Text "Getting client..." ]
        >>= ensurePath
        >>= log
        >>= trigger u [ Text "Downloading client..." ]
        >>= downloadAndInstallClient client u
        >>= log
        >>= trigger u [ Text "Downloading launcher..." ]
        >>= downloadAndInstallLauncher client u
        >>= log
        >>= yes (fun () -> log "register time" |> ignore)
        >>= trigger u [ Text "Registering protocol..." ]
        >>= (if Environment.OSVersion.Platform = PlatformID.Win32NT then
                 registerURIWindows
             else
                 registerURILinux)
        >>= yes (fun () -> log "registered" |> ignore)
        >>= launchAndComplete c u ticket

    match result with
    | Ok _ ->
        u.Trigger [ Text "Done!"; Indeterminate false; Progress 100 ]
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
    // check if on linux
    if
        Environment.OSVersion.Platform <> PlatformID.Win32NT
        && not Environment.IsPrivilegedProcess
    then
        // request elevation
        printfn "Requesting elevation"
        let filename = Process.GetCurrentProcess().MainModule.FileName

        let envs = [|
            "DISPLAY=" + Environment.GetEnvironmentVariable "DISPLAY"
            "XAUTHORITY=" + Environment.GetEnvironmentVariable "XAUTHORITY"
        |]

        let psi =
            ProcessStartInfo(
                FileName = "pkexec",
                Arguments = $"""env {String.Join(" ", envs)} bash -c "{filename} {String.Join(" ", args)}" """
            )

        let proc = Process.Start psi
        proc.WaitForExit()

        printfn "Elevated process exited with code: %d" proc.ExitCode

        proc.ExitCode
    else
        printfn "Starting application"

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
