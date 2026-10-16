# Build

Requires a .NET SDK with .NET 6 targeting support, game 1.6.15 reference assemblies, and SMAPI 4.5.2. Tests require .NET 8.

```sh
dotnet build DragonToothBlacklist.csproj -c Release \
  -p:GameRefs=/path/to/game-reference-dlls \
  -p:SmapiRefs=/path/to/smapi-reference-folder
dotnet run --project Tests/Tests.csproj -c Release
```

Reference DLLs are supplied by the developer and are not included. Do not redistribute game, SMAPI, or Harmony assemblies.
