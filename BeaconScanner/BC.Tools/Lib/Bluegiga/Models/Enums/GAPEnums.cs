//
// Bluegiga specific GAP enumerations
//

namespace BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums {


    public enum BGGAPDiscoverModes : byte {

        Limited      = 0x00, // Discover only limited discoverable devices
        Generic      = 0x01, // Discover limited and generic discoverable devices
        Observation  = 0x02  // Discover all devices

    }

    public enum BGGAPAdvertiserAddressType : byte {

        RandomAddress  = 0x01, // Fake, randomly generated address
        PublicAddress  = 0x00  // Real Bluetooth MAC address

    }

    public enum BGGAPPacketType : byte {

        ConnectableAd     = 0x00, // Scannable and connectable
        NonConnectableAd  = 0x02, // Non-scannable and non-connectable
        ScanResponse      = 0x04, // Scan response
        DiscoverableAd    = 0x06  // Scannable and non-connectable

    }

}