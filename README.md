# JoySmoother

JoySmoother is a Windows desktop app that reads the left stick of an XInput controller and can send drift-smoothed values to a virtual Xbox 360 controller.

## Use

1. Connect the physical controller.
2. Run JoySmoother and identify its XInput slot by watching the connection state and live X/Y values. Move the left stick to see which slot responds.
3. Select that slot.
4. Enter the drift zone, velocity cutoff, and settle delay directly in their numeric fields. Values are applied immediately; invalid values are highlighted and prevent starting.
5. Select **Monitor only** to inspect the smoothed values without changing game input, or select **Virtual Xbox 360 controller** and press **Start** to enable filtered output.
6. Configure the game to use the virtual Xbox controller. Stop JoySmoother when the test is complete.

XInput exposes controller slots, not friendly device names. The app therefore shows the four available slots and their live readings to help identify the correct physical controller.

## Smoothing parameters

- **Drift zone**: Maximum distance from stick center considered potential drift.
- **Velocity cutoff**: Maximum per-poll change for the stick to count as stationary.
- **Settle delay**: Number of consecutive qualifying polls before the output is centered. Polling is approximately every 8 ms, so the delay is also adjustable in frames.

Accepted ranges: drift zone `0–10000`, velocity cutoff `0–1500`, and settle delay `1–60` frames.

## Requirements

- Windows with XInput support
- A connected XInput-compatible controller
- The signed ViGEmBus driver for virtual-controller mode

The virtual controller is exposed as an Xbox 360 controller by ViGEm; its device label is determined by the virtual bus and is not renamed by JoySmoother.
Some games may also detect the physical controller, which can result in duplicate input. If that happens, configure the game to use only the virtual controller and use a device-hiding tool only if needed.

## Build and publish

Build from the project folder:

```powershell
dotnet build
```

Publish a self-contained, single-file Windows x64 app:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The published app runs without a separately installed .NET runtime. Virtual-controller mode still requires the ViGEmBus driver.
