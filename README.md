# Dragon Tooth Blacklist

A Stardew Valley SMAPI mod that lets each weapon reject unwanted **complete Dragon Tooth enchantment combinations**, ignoring enchantment levels.

[Download v1.1.0](https://github.com/passionfruitfruit/dragon-tooth-blacklist/releases/tag/v1.1.0) · [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53990) · [中文说明](README-中文.md)

## How it works

At the Volcano Forge or vanilla Mini-Forge, put the weapon on the left and the material on the right:

| Material | Result |
| --- | --- |
| 1 coal | Remove the current innate Dragon Tooth enchantments and blacklist their complete type combination for that weapon. |
| 1 normal quartz | Clear that weapon's blacklist while keeping its current enchantments. |

These two operations cost no Cinder Shards. Ordinary Dragon Tooth reforging keeps its usual material costs.

The blacklist compares the **whole set of enchantment types**, not levels and not partial matches. Banning Attack +3 also bans other levels of Attack alone, but allows Attack + Defense. Banning Speed alone allows Speed + Lightweight; banning Speed + Lightweight only rejects that exact pair of types. This applies to all vanilla innate enchantment types.

Each weapon keeps its own blacklist in item mod data. Prismatic Shard enchantments, gem forge upgrades, Galaxy Soul progress and weapon appearance are unaffected. Only vanilla-generated combinations can appear; the remaining numeric outcomes keep their relative vanilla probabilities. When every legal complete combination has been banned, the blacklist resets automatically.

## Install

Built against Stardew Valley **1.6.15** and **SMAPI 4.5.2**. No additional mod dependency. The released version has been confirmed working on Steam Deck by the requesting player.

1. Install [SMAPI](https://smapi.io/), then exit the game.
2. Download the install ZIP from **Releases**. GitHub's **Code → Download ZIP** contains source code, not the installable mod.
3. Extract the `DragonToothBlacklist` folder into your game's `Mods` directory, replacing an older copy if present.
4. Launch through SMAPI. No Generic Mod Config Menu entry is required.

On Steam Deck the usual Mods path is `~/.local/share/Steam/steamapps/common/Stardew Valley/Mods`. For another Steam library, use the game's **Properties → Installed Files → Browse**.

## Diagnostics and compatibility

With a weapon selected, run `dtb_check` in the SMAPI console to inspect its current combination, blacklist and legal outcome pool. If an operation is unavailable, check the complete SMAPI log for `Dragon Tooth blacklist` errors.

Other mods that patch the forge or innate enchantment generator have not been exhaustively tested. Changes to a future game generator are checked at runtime; unsupported blacklist operations are refused rather than generating invented combinations.

## Build and verification

The mod targets .NET 6; the console tests require .NET 8. Supply game reference DLLs and SMAPI reference DLLs locally:

```sh
dotnet build DragonToothBlacklist.csproj -c Release -p:GameRefs=/path/to/game-dlls -p:SmapiRefs=/path/to/smapi
dotnet run --project Tests/Tests.csproj -c Release
```

The original v1.1.0 delivery passed 71 automated checks and a Release build. This public release preserves the delivered DLL; publishing is not a new game test. See [VERIFICATION.md](VERIFICATION.md). The source and release contain no game, SMAPI or Harmony DLLs.

## License and credits

MIT, copyright Luke. Stardew Valley is by ConcernedApe; SMAPI and Harmony are separate dependencies. Code and release documentation were developed with substantial AI assistance, with requirements and gameplay feedback supplied by the requesting player.
