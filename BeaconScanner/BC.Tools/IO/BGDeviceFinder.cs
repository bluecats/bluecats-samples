using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BlueCats.Tools.Portable.IO;
using BlueCats.Tools.UWP.IO;

namespace BlueCats.Tools.UWP.Lib.Bluegiga {

    public static class BGDeviceFinder {

        public static async Task< List< ISerialDevice > > GetConnectedSerialDevicesAsync() {

            var connectedSerialDevices = await UWPSerialDevice.GetDevicesAsync()
                .ConfigureAwait( false );           
            
            var bgSerialDevices = (
                from device in connectedSerialDevices
                where device.Name.Contains( "Low Energy Dongle" )
                   || device.Name.Contains( "Bluegiga" ) 
                select (ISerialDevice) device
            ).ToList();

            return bgSerialDevices;
        }

    }

}