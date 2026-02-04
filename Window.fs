module Window

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.Hosts
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Themes.Fluent
open Config
open Microsoft.FSharp.Control
open System
open System.Threading
open Avalonia.Threading

type Update =
    | Text of string
    | Progress of float
    | Indeterminate of bool

type Control =
    | SuccessMessage of string
    | ErrorMessage of string
    | Shutdown

let width = 500
let height = 320
let blurRadius = 20

let mainaccent = Color.FromRgb(119uy, 51uy, 255uy) // Mercury hsl(260 100 60)
let darker = Color.FromRgb(9uy, 8uy, 10uy)
let background = Color.FromRgb(15uy, 14uy, 17uy)
let accent = Color.FromRgb(29uy, 28uy, 31uy)

let viewPopup text =
    Component(fun _ ->
        let padding = 10

        Border.create [
            Border.margin (Thickness(padding, padding, padding, int (padding * 2)))
            Border.child (
                TextBlock.create [
                    TextBlock.text text
                    TextBlock.fontSize 14
                    TextBlock.fontWeight FontWeight.SemiBold
                    TextBlock.foreground (SolidColorBrush Colors.White)
                    TextBlock.textAlignment TextAlignment.Left
                    TextBlock.horizontalAlignment HorizontalAlignment.Left
                    TextBlock.textWrapping TextWrapping.Wrap
                ]
            )
        ])

type PopupWindow(text) =
    inherit HostWindow()

    do
        base.Title <- $"{name} Launcher"
        base.Width <- 450
        base.SizeToContent <- SizeToContent.Height
        base.CornerRadius <- CornerRadius 15
        base.Background <- SolidColorBrush darker

        // set icon (not taskbar icon afaict)
        base.Icon <- new WindowIcon(new IO.MemoryStream(icon))
        base.Content <- viewPopup text

let view (u: IEvent<Update list>) =
    Component(fun ctx ->
        let textState = ctx.useState ("Initialising launcher...", renderOnChange = false)
        let progress = ctx.useState (0., renderOnChange = false)
        let indeterminate = ctx.useState (true, renderOnChange = false)

        let mutable sub: IDisposable = null

        sub <-
            u.Subscribe(fun updates ->
                for update in updates do
                    match update with
                    | Text t -> textState.Set t
                    | Progress p -> progress.Set p
                    | Indeterminate i -> indeterminate.Set i

                if sub <> null then
                    sub.Dispose()
                    sub <- null

                ctx.forceRender ())

        let textSize = 24
        let padding = 20

        let children: Types.IView list = [
            TextBlock.create [
                TextBlock.dock Dock.Top
                TextBlock.fontSize textSize
                TextBlock.fontWeight FontWeight.SemiBold
                TextBlock.verticalAlignment VerticalAlignment.Center
                TextBlock.horizontalAlignment HorizontalAlignment.Center
                TextBlock.text textState.Current
            ]
            ProgressBar.create [
                ProgressBar.dock Dock.Bottom
                ProgressBar.isIndeterminate indeterminate.Current
                ProgressBar.value progress.Current
                ProgressBar.height 8
                ProgressBar.cornerRadius (CornerRadius 4)
                ProgressBar.foreground (SolidColorBrush mainaccent)
                ProgressBar.background (SolidColorBrush accent)
                ProgressBar.horizontalAlignment HorizontalAlignment.Stretch
                ProgressBar.verticalAlignment VerticalAlignment.Center
            ]
            Image.create [
                // centre in the window
                Image.dock Dock.Bottom
                Image.source (new Imaging.Bitmap(new IO.MemoryStream(icon)))
                Image.stretch Stretch.Uniform
                Image.width 128
                Image.height 128
                Image.horizontalAlignment HorizontalAlignment.Center
                Image.verticalAlignment VerticalAlignment.Center
            ]
        ]

        let panel =
            DockPanel.create [ DockPanel.margin (Thickness padding); DockPanel.children children ]

        // add background behind panel with some transparency and rounded corners
        let margin = 5
        let cornerRadius = 30

        let bg =
            Border.create [
                Border.background (SolidColorBrush mainaccent)
                // blur background
                Border.effect (BlurEffect(Radius = blurRadius))
                Border.margin (Thickness blurRadius)
                // blur radius
                Border.cornerRadius (CornerRadius cornerRadius)
            ]

        // create container with background and corner radius
        let fg =
            Border.create [
                Border.background (SolidColorBrush mainaccent)
                Border.margin (Thickness(int (margin + blurRadius)))
                Border.cornerRadius (CornerRadius(int (cornerRadius - margin)))
                Border.child (
                    Border.create [
                        Border.background (SolidColorBrush darker)
                        Border.margin (Thickness 4)
                        Border.cornerRadius (CornerRadius(int (cornerRadius - margin - 4)))
                        Border.child panel
                    ]
                )
            ]

        Grid.create [ Grid.children [ bg; fg ] ])

type MainWindow(xfn) =
    inherit HostWindow()

    let controlEvent = new Event<Control>()
    let updateEvent = new Event<Update list>()

    do
        base.Title <- $"{name} Launcher"
        base.Width <- int (575 + blurRadius * 2)
        base.Height <- int (326 + blurRadius * 2)
        base.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        // remove title bar
        base.SystemDecorations <- SystemDecorations.None
        base.CornerRadius <- CornerRadius 15
        // set transparent background
        base.Background <- SolidColorBrush Colors.Transparent

        // set icon (not taskbar icon afaict)
        base.Icon <- new WindowIcon(new IO.MemoryStream(icon))
        let comp = view updateEvent.Publish
        base.Content <- comp

        printfn "Subscribing to control events"

        controlEvent.Publish.Subscribe (function
            | SuccessMessage text ->
                Dispatcher.UIThread.Post(fun () ->
                    let window = new PopupWindow(text)
                    window.Closed.Subscribe(fun _ -> controlEvent.Trigger Shutdown) |> ignore
                    window.Show())
            | ErrorMessage text ->
                Dispatcher.UIThread.Post(fun () ->
                    let window = new PopupWindow(text)
                    window.Closed.Subscribe(fun _ -> controlEvent.Trigger Shutdown) |> ignore
                    window.Show())
            | Shutdown ->
                printfn "Byebye"
                Thread.Sleep 100 // give the UI a chance to update before closing
                Environment.Exit 1)
        |> ignore

    override _.Render(context: DrawingContext) : unit = base.Render(context: DrawingContext)

    override _.OnLoaded _ =
        printfn "Initialized MainWindow"
        // start in another thread
        async { xfn controlEvent updateEvent } |> Async.Start
        printfn "Finished xfn"

type App(xfn) =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime -> desktopLifetime.MainWindow <- MainWindow xfn
        | _ -> ()
