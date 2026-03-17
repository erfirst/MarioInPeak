# libsm64-unity-bepinex

Modification of [libsm64-unity-melonloader](https://github.com/headshot2017/libsm64-unity-melonloader), intended for modding Unity games through [Bepinex](https://github.com/BepInEx).

This template covers the adaption of libsm64-unity-melonloader so that it can be used as a Bepinex mod, however you must make some changes in order to adapt it to the game you want to mod.

* Create a new project in Visual Studio using the Bepinex template.
* Copy everything in this project into yours.

You can either compile the libsm64 DLL yourself or download precompiled DLLs from the [Releases tab](https://github.com/sashaantipov2012/libsm64-unity-bepinex/releases).

The compiled libsm64 DLL (`sm64-win32.dll`/`sm64-win64.dll`) and the SM64 ROM (`sm64.z64`) must be placed in the same directory as the game's executable.
