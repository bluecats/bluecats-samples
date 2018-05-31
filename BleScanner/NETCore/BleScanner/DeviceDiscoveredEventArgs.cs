using System;
using BlueCats.Ble.Serial.BC0xx.Events;

namespace BleScanner {

    public class DeviceDiscoveredEventArgs : EventArgs {

        public DeviceDiscoveredEvent DiscoveredEvent { get; set; }

        public DeviceDiscoveredEventArgs( DeviceDiscoveredEvent discoveredEvent ) {
            DiscoveredEvent = discoveredEvent;
        }

    }

}