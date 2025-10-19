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

type Update =
    | Text of string
    | Progress of float
    | Indeterminate of bool
    | SuccessMessage of string
    | ErrorMessage of string
    | Shutdown

let width = 500
let height = 320
let blurRadius = 20

let darker = Color.FromRgb(10uy, 9uy, 8uy)
let accent = Color.FromRgb(119uy, 51uy, 255uy) // Mercury hsl(260 100 60)

let view (updateEvent: Event<Update>) =
    Component (fun ctx ->
        let textState = ctx.useState "Initialising launcher..."

        updateEvent.Publish.Subscribe (function
            | Text text -> textState.Set text
            | Progress progress -> ()
            | Indeterminate indeterminate -> ()
            | SuccessMessage message -> ()
            | ErrorMessage message -> ()
            | Shutdown -> ())
        |> ignore

        let textSize = 24
        let textPadding = 20

        let children: Types.IView list =
            [ TextBlock.create [
                  TextBlock.dock Dock.Top
                  TextBlock.fontSize textSize
                  TextBlock.fontWeight FontWeight.SemiBold
                  TextBlock.margin (Thickness(0, textPadding, 0, 0))
                  TextBlock.verticalAlignment VerticalAlignment.Center
                  TextBlock.horizontalAlignment HorizontalAlignment.Center
                  TextBlock.text textState.Current
              ]
              Image.create [
                  // centre in the window
                  Image.dock Dock.Bottom
                  Image.margin (Thickness(0, 0, 0, int (textSize + textPadding)))
                  Image.source (new Imaging.Bitmap(new System.IO.MemoryStream(icon)))
                  Image.stretch Stretch.Uniform
                  Image.width 128
                  Image.height 128
                  Image.horizontalAlignment HorizontalAlignment.Center
                  Image.verticalAlignment VerticalAlignment.Center
              ] ]

        let panel =
            DockPanel.create [
                DockPanel.children children
            ]

        // add background behind panel with some transparency and rounded corners
        let margin = 5
        let cornerRadius = 30

        let background =
            Border.create [
                Border.background (SolidColorBrush accent)
                // blur background
                Border.effect (BlurEffect(Radius = blurRadius))
                Border.margin (Thickness blurRadius)
                // blur radius
                Border.cornerRadius (CornerRadius cornerRadius)
            ]

        // create container with background and corner radius
        let foreground =
            Border.create [
                Border.background (SolidColorBrush accent)
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

        Grid.create [
            Grid.children [ background; foreground ]
        ])


type MainWindow(xfn) =
    inherit HostWindow()

    let updateEvent = new Event<Update>()

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
        base.Icon <- new WindowIcon(new System.IO.MemoryStream(icon))
        base.Content <- view updateEvent

    override this.OnLoaded(e: Interactivity.RoutedEventArgs) : unit =
        printfn "Initialized MainWindow"
        // start in another thread
        async { xfn updateEvent } |> Async.Start
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
