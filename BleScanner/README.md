# BCX0X BLE Scanner Sample  (.NETCore)
A commandline BLE scanner sample, quite similar to the popular `hcitool`. The app prints out scanned Bluetooth Low Energy Advertisment packets.

BLE Serial API Docs: ([link](https://developer.bluecats.com/documentation/libraries/serial-Home))

## Dev Requirements
* Visual Studio
* .NET Core Runtime: [download Win](https://www.microsoft.com/net/download/windows/run), [download Linux](https://www.microsoft.com/net/download/linux/run), [download macOS](https://www.microsoft.com/net/download/macos/run)

## Build
Build Visual Studio project or run `build.ps1` build script

## Usage
If `-l` Loop option is added to command line arguments, BLE RSSI Packets will be forwarded to the Loop Location Engine on port `9942`. Loop Location Engine can be running on the localhost or on a remote host.

 The format of each line is as follows:
```{BluetoothAddress} {RSSI} {Advertisment data}```

```
Usage:
  dotnet BleScanner.dll <serial name> [ -h | -l ]

Example:
  dotnet BleScanner.dll com3 -l

  -l                         Send to Loop Location Engine (UDP)
  -h, --help                 Print this message
  ```

  ![Screenshot](https://user-images.githubusercontent.com/9400300/38267540-7a51564e-3741-11e8-91fd-989476c6d877.PNG)
