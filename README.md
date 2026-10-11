# JoySmoother

JoySmoother is (currently) a Windows-only desktop app that reads (currently only) the left stick of an XInput controller and can send drift-smoothed values from it to a virtual Xbox 360 controller.
In short, the program: 

1. Reads the values of the left analog stick from the physical controller
2. According to a customizable logic, calculates the new values
3. Assigns these values to the created virtual controller and to its respective joystick

## Use

1. Connect the physical controller.
2. Run JoySmoother and identify its XInput slot by watching the connection state and live X/Y values. Move the left stick to see which slot responds.
3. Select that slot.
4. Enter the drift zone, velocity cutoff, and settle delay directly in their numeric fields. Values are applied immediately; invalid values are highlighted and prevent starting.
5. Select **Monitor only** mode to inspect the smoothed values without changing game input, or select **Virtual Xbox 360 controller** and press **Start** to enable filtered output.

When you press **Start** you should essentially hear the notification sound for a device being connected. Similarly when you press **Stop** a sound for disconnecting a device should play.

XInput exposes controller slots, not friendly device names. The app therefore shows the four available slots and their live readings to help identify the correct physical controller.

## Smoothing parameters

- **Drift zone**: Maximum distance from stick center considered potential drift.
- **Velocity cutoff**: Maximum per-poll change for the stick to count as stationary.
- **Settle delay**: Number of consecutive qualifying polls before the output is centered. Polling is approximately every 8 ms, so the delay is also adjustable in frames.

Accepted ranges: drift zone `0–10000`, velocity cutoff `0–1500`, and settle delay `1–60` frames.

## Requirements

- Windows with XInput support and .NET9/10 Desktop runtime installed
- A connected XInput-compatible controller
- The signed ViGEmBus driver for virtual-controller mode
    - https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0
    - The future objective is to change this driver to something completely different

The virtual controller is exposed as an Xbox 360 controller by ViGEm; its device label is determined by the virtual bus and is not renamed by JoySmoother.
Some games may also detect the physical controller, which can result in duplicate or otherwise mixed input.

## An example: Live for Speed

Live for Speed is a good game in sense that it supports multiple controls, and allows you to customize your controls to be a hybrid from multiple controllers.
When you start the program, the game shows you multiple controllers. Make sure you have not selected to use just one of them. Make sure it says atleast 2 controllers detected.
<img width="745" height="64" alt="image" src="https://github.com/user-attachments/assets/98402e85-77ca-498e-b82a-793b91a1f518" />

When you see atleast two contollers connected, just edit the steering axis to be the X-axis of the virtual controller.

Within the game itself, you can see the effects of the software when you compare the X-axis values of the real controller and the virtual counterpart.

In the game settings -> Misc -> change the "Analogue steer smooth" to something like 0.80 to smooth out the snappyness.

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
