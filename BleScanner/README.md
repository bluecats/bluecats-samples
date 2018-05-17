# BLE Scanner Sample (.NET Core)
A commandline BLE scanner sample, quite similar to the popular `hcitool`. The app prints out scanned Bluetooth Low Energy Advertisment packets. The format of each line is as follows:
`{BluetoothAddress} {RSSI} {Advertisment data}`

If `-l` Loop option is added to command line arguments, BLE RSSI Packets will be forwarded to the Loop Location Engine on port `19111`. Loop Location Engine can be running on the localhost or on a remote host.

## Requirements
.NET Core Runtime: [Win](https://www.microsoft.com/net/download/windows/run), [Linux](https://www.microsoft.com/net/download/linux/run), [macOS](https://www.microsoft.com/net/download/macos/run)

## Build
Visual Studio: [Win](https://www.microsoft.com/net/download/windows/build), [Linux](https://www.microsoft.com/net/download/linux/build), [macOS](https://www.microsoft.com/net/download/macos/build)

## Usage
```
Usage:
  dotnet BleScanner.dll <serial name> [ -h | -l ]

Example:
  dotnet BleScanner.dll com3 -l

  -l                         Send to Loop Location Engine (UDP)
  -h, --help                 Print this message
  ```

  ![Screenshot](https://user-images.githubusercontent.com/9400300/38267540-7a51564e-3741-11e8-91fd-989476c6d877.PNG)
