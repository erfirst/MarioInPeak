# MarioInPeak

A mod for Peak that imports Mario from Super Mario 64 as the player character.

## Instructions

Place [sm64-win64.dll](https://github.com/sashaantipov2012/libsm64-unity-bepinex/releases) and YOUR OWN COPY of Super Mario 64 rom in the same directory as the Peak executable. 

Add the mod through Thunderstore (or your preferred mod manager of choice) with the [BepInEx pack for Peak](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.3). Launch the modded instance and load into an offline game. Once you land on the island you should be playing as Mario!

### Thunderstore Packaging

This template comes with Thunderstore packaging built-in, using [TCLI](<https://github.com/thunderstore-io/thunderstore-cli>).

You can build Thunderstore packages by running:

```sh
dotnet build -c Release -target:PackTS -v d
```

> [!NOTE]  
> You can learn about different build options with `dotnet build --help`.  
> `-c` is short for `--configuration` and `-v d` is `--verbosity detailed`.

The built package will be found at `artifacts/thunderstore/`.

### References
Thank you to sashaantipov2012 for creating a plug-in template for LibSM64 to be used in Unity games through [BepInEx](https://github.com/sashaantipov2012/libsm64-unity-bepinex?tab=readme-ov-file).

