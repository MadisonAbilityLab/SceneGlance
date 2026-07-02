# SceneGlance — Unity Frontend (HoloLens 2)

The augmented-reality frontend for SceneGlance, deployed on Microsoft HoloLens 2. It streams
sensor video to the [Python backend](../python), receives recognition results, and renders the
object augmentations in world space.

## Unity version

**Unity 2022.3.25f1** (LTS). Open this `unity/` folder with exactly this version via Unity Hub to
avoid package/API mismatches.

Required modules (install through Unity Hub → Installs → Add Modules):
- Universal Windows Platform Build Support
- Windows Build Support (IL2CPP)

For a complete first-time environment setup, follow Microsoft's official guide:
[Install the tools for Mixed Reality development](https://learn.microsoft.com/en-us/windows/mixed-reality/develop/install-the-tools?tabs=unity).

## Setup & build

1. Open the `unity/` folder in Unity Hub with **2022.3.25f1**.
2. Open the **`Main`** scene (`Assets/Scenes/Main.unity`).
3. `File → Build Settings → Universal Windows Platform → Switch Platform`, and make sure **`Main`**
   is the scene listed under *Scenes In Build*.
4. Set **Architecture = ARM64**, **Build Type = Release**.
5. `Build` into a build folder, then open the generated `.sln` in **Visual Studio 2022**.
6. In Visual Studio, select **ARM64 + Release** and deploy to the HoloLens 2 (USB via *Device*,
   or Wi-Fi via *Remote Machine* using the headset's IP).

**First deployment:** enable [*Research Mode*](https://learn.microsoft.com/en-us/windows/mixed-reality/develop/advanced-concepts/research-mode)
on the HoloLens (`Settings → Updates → For Developers`); the backend requires it to access camera intrinsics.

> Note: The bundled `hl2ss.dll` sensor-streaming plugin (`Assets/Plugins/WSA/`) is a build modified
> to support UDP transmission, based on https://github.com/iamwyh2019/hl2ss.

> Optional (debug logging): to view on-device logs on your PC, select the **`DebugServer`**
> component in the `Main` scene and set its **address** to your PC's IP and **port** to `9031` in the
> Inspector *before deploying*, then run [`debugserver.py`](../python/debugserver.py) on that PC.
