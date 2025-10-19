module Window

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.FuncUI.Hosts
open Avalonia.Controls
open Avalonia.FuncUI
open Avalonia.FuncUI.DSL
open Avalonia.Layout

let view () =
    Component (fun ctx ->
        let state = ctx.useState 0

        let children: Types.IView list =
            [ Button.create [
                  Button.dock Dock.Bottom
                  Button.onClick (fun _ -> state.Set(state.Current - 1))
                  Button.content "-"
                  Button.horizontalAlignment HorizontalAlignment.Stretch
                  Button.horizontalContentAlignment HorizontalAlignment.Center
              ]
              Button.create [
                  Button.dock Dock.Bottom
                  Button.onClick (fun _ -> state.Set(state.Current + 1))
                  Button.content "+"
                  Button.horizontalAlignment HorizontalAlignment.Stretch
                  Button.horizontalContentAlignment HorizontalAlignment.Center
              ]
              TextBlock.create [
                  TextBlock.dock Dock.Top
                  TextBlock.fontSize 48.0
                  TextBlock.verticalAlignment VerticalAlignment.Center
                  TextBlock.horizontalAlignment HorizontalAlignment.Center
                  TextBlock.text (string state.Current)
              ] ]

        DockPanel.create [
            DockPanel.children children
        ])

type MainWindow() =
    inherit HostWindow()

    do
        base.Title <- "Counter Example"
        base.Content <- view ()

type App() =
    inherit Application()

    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.RequestedThemeVariant <- Styling.ThemeVariant.Dark

    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktopLifetime -> desktopLifetime.MainWindow <- MainWindow()
        | _ -> ()
