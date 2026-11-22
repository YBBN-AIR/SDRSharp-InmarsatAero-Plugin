# SDRSharp.InmarsatAero

An [SDR#](https://airspy.com/download/) plugin that decodes **Inmarsat Aero ACARS** messages sent by satellite to
aircraft, on the L-band around 1545 MHz. It handles all three common speeds (600, 1200 and 10500 bps), decodes
many channels at once in a single copy of SDR#, and is designed to get the most out of **weak signals**: small
antennas, poor sites and the faint 10500 bps channels.

## Install

1. Download the latest `SDRSharp.InmarsatAero-vX.Y.Z.zip` from [Releases](../../releases).
2. Close SDR# and extract the zip into SDR#'s `Plugins` folder. You should end up with
   `<SDR#>\Plugins\SDRSharp.InmarsatAero\SDRSharp.InmarsatAero.dll`.
3. Start SDR# and open **Inmarsat Aero** from the Plugins menu.

If the plugin doesn't appear, update SDR# to the latest version and check `PluginError.log` in the SDR# folder.
To update the plugin, replace the folder. To remove it, delete the folder.

## Getting started

1. Tune SDR# so the satellite's Aero channels are inside the visible spectrum. The plugin listens to everything
   SDR# receives, so you don't need to tune the VFO onto each channel.
2. Tick **Enabled**.
3. Either add channels yourself (frequency and speed, then **Add / Update**), or tick **Auto scan whole band** and
   let the plugin find them.
4. Messages appear in the **Log** tab, aircraft in the **Aircraft** tab, and satellite information in the
   **System** tab.

## Features

- **Many channels at once** from one copy of SDR#.
- **Auto scan** finds active channels across the band, works out their speed and starts decoding them. It can find
  channels too faint to see on the waterfall.
- **Automatic frequency correction** for your receiver's tuning error.
- **Signal quality per channel**, shown as how many dB the channel is above the point where decoding fails.
  Useful for comparing antennas and LNAs.
- **Aircraft tab** listing each aircraft heard: ICAO address, registration, type, country, what it last did and
  which channel it is using. Military aircraft are flagged, with a **Military only** filter. Registration, type and
  military flag need an aircraft database, such as tar1090-db's `aircraft.csv.gz`, loaded with **Browse...**.
- **System tab** showing what the satellite broadcasts about itself: satellite positions, ground stations and
  channel assignments.
- **UDP output** in JAERO's JSON format, so messages can be fed into tools such as acarshub. A second destination
  can be added.

## How it works

For each channel, the plugin:

1. Picks the channel out of everything SDR# is receiving and removes any interference spikes inside it.
2. Locks onto the exact frequency of the satellite's signal.
3. Turns the signal back into bits, keeping track of how confident it is about each one.
4. Finds the start of each frame of data.
5. Corrects errors using the error-correction code built into the signal.
6. Checks each piece of data, puts the pieces back together into messages and displays them.

## Built for weak signals

Most of the effort in this plugin has gone into decoding signals at the edge of reception:

- **Steadier tracking on 10500 bps.** Once a channel is locked and the signal is stable, the plugin gradually
  tightens its tracking so it is less disturbed by noise. If the signal starts to wander, it loosens again. In
  tests with real recordings this took a faint 10500 channel from no messages at all to a steady stream.
- **Uses uncertain bits instead of discarding them.** The error correction can still recover messages when around
  one bit in seven is received wrongly.
- **Recovers from dropouts** without starting over.
- **Removes interference spikes** that would otherwise stop a channel decoding.
- **Sensitive scanning** that finds channels you can't see on the waterfall.
- **Realistic signal readings**, measured from the errors actually being corrected, so you can tell whether a
  change to your antenna has helped.

## Impact on SDR#

The plugin only reads SDR#'s incoming signal. It doesn't change your audio or display. Decoding runs on its own
background thread, so SDR# stays responsive. It sends messages out over UDP only, and saves nothing except its
settings.

## Building from source

Requires the .NET 10 SDK on Windows and an SDR# install to compile against:

```
dotnet build -c Release -p:SDRSharpDir=C:\path\to\sdrsharp\
```

The release zip is written to the `dist` folder.

## Credits

The frame format, error-correction setup, message reassembly, ACARS parsing, JSON output format and System tab
field layouts follow **[JAERO](https://github.com/jontio/JAERO)** by Jonathan Olds, used under the MIT License
(see `THIRD-PARTY-NOTICES.txt`). All other code is original to this project.

## Licence

Released under the [MIT License](LICENSE): you're free to use, copy, modify and share this plugin, including in
your own projects, as long as the copyright notice is kept. It comes with no warranty. Third-party notices are in
`THIRD-PARTY-NOTICES.txt`.
