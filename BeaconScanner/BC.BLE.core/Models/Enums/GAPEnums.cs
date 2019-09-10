
namespace BlueCats.Bluetooth.Core.Models.Enums {

    // Used in the headers of Ad-structures within Ad-data and scan response data.
    public enum GAPAdType : byte {

        Flags                                    = 0x01,
        IncompleteListOf16­BitServiceClassUUIDs   = 0x02,
        CompleteListOf16­BitServiceClassUUIDs     = 0x03,
        IncompleteListOf32­BitServiceClassUUIDs   = 0x04,
        CompleteListOf32­BitServiceClassUUIDs     = 0x05,
        IncompleteListOf128­BitServiceClassUUIDs  = 0x06,
        CompleteListOf128­BitServiceClassUUIDs    = 0x07,
        ShortenedLocalName                       = 0x08,
        CompleteLocalName                        = 0x09,
        TxPowerLevel                             = 0x0A,
        ClassOfDevice                            = 0x0D,
        SimplePairingHashC                       = 0x0E,
        SimplePairingHashC­192                    = 0x0E,
        SimplePairingRandomizerR                 = 0x0F,
        SimplePairingRandomizerR­192              = 0x0F,
        DeviceId                                 = 0x10,
        SecurityManagerTkValue                   = 0x10,
        SecurityManagerOutOfBandFlags            = 0x11,
        SlaveConnectionIntervalRange             = 0x12,
        ListOf16­BitServiceSolicitationUUIDs      = 0x14,
        ListOf32­BitServiceSolicitationUUIDs      = 0x1F,
        ListOf128­BitServiceSolicitationUUIDs     = 0x15,
        ServiceData                              = 0x16,
        ServiceData­16­BitUUID                     = 0x16,
        ServiceData­32­BitUUID                     = 0x20,
        ServiceData­128­BitUUID                    = 0x21,
        PublicTargetAddress                      = 0x17,
        RandomTargetAddress                      = 0x18,
        Appearance                               = 0x19,
        AdvertisingInterval                      = 0x1A,
        LeBluetoothDeviceAddress                 = 0x1B,
        LeRole                                   = 0x1C,
        SimplePairingHashC­256                    = 0x1D,
        SimplePairingRandomizerR­256              = 0x1E,
        ThreeDInformationData                    = 0x3D,
        ManufacturerSpecificData                 = 0xFF

    }

}