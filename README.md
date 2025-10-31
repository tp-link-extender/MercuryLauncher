# Mercury Launcher

Launcher for [Mercury Core](https://github.com/tp-link-extender/MercuryCore)-based game creation platforms

## Build

Comes with approximate sizes

### Windows

`dotnet publish --no-self-contained --nologo -o ./out -r win-x64`  
13.47MB + .NET 9

`dotnet publish --self-contained --nologo -o ./out -r win-x64`  
20.48MB trimmed + no symbols

### Linux

`dotnet publish --no-self-contained --nologo -o ./out -r linux-x64`  
13.33MB + .NET 9

`dotnet publish --nologo -o ./out -r linux-x64`  
20.71MB trimmed + no symbols
