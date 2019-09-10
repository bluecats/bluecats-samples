using System;
using BlueCats.Bluetooth.BLEProtocols.Base;

namespace BlueCats.Bluetooth.BLEProtocols.AdModels.Apple {

    public class IBeaconAdModel : AdModel {

        public string ProximityUUID { get; set; }
        public UInt32 Major { get; set; }
        public UInt32 Minor { get; set; }
        public sbyte MeasuredPower { get; set; }

    }

}