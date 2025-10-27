# Mercury Launcher

Launcher for [Mercury Core](https://github.com/tp-link-extender/MercuryCore)-based game creation platforms

## Build

Comes with approximate sizes

### Windows

`dotnet publish --no-self-contained --nologo -o ./out -r win-x64`  
13.47MB + .NET 9

`dotnet publish --nologo -o ./out -r win-x64`  
80.95MB  
21.95MB trimmed  
21.91MB trimmed + no symbols

### Linux

`dotnet publish --no-self-contained --nologo -o ./out -r linux-x64`  
13.33MB + .NET 9

`dotnet publish --nologo -o ./out -r linux-x64`  
81.09MB  
23.69MB trimmed  
23.64MB trimmed + no symbols
