// *See Bluetooth SIG Core Specification v4.0, Volume 3, Part G, Section 3

using System;

namespace BlueCats.Bluetooth.Core.Models.Enums {

    // Attribute Type UUIDs
    public enum GATTAttributeType : ushort {

        ServicePrimaryUUID                     = 0x2800,
        ServiceSecondaryUUID                   = 0x2801,
        ServiceIncludeUUID                     = 0x2802,
        CharacteristicUUID                     = 0x2803, // read-only
        CharacteristicExtendedPropertiesUUID   = 0x2900,
        CharacteristicUserDescriptionUUID      = 0x2901,
        CharacteristicClientConfigurationUUID  = 0x2902, // writable
        CharacteristicServerConfigurationUUID  = 0x2903, 
        CharacteristicFormatUUID               = 0x2904, 
        CharacteristicAggregateFormatUUID      = 0x2905 

    }

    // Bit-field indexes used in GATTAttributeTypes.CharacteristicUUID attribute 
    [Flags]
    public enum GATTCharacteristicProperties : ushort {

        Broadcast                  = 0x0001,
        Read                       = 0x0002,
        WriteWithoutResponse       = 0x0004,
        Write                      = 0x0008,
        Notify                     = 0x0010,
        Indicate                   = 0x0020,
        AuthenticatedSignedWrites  = 0x0040,
        ExtendedProperties         = 0x0080

    }

    // Bit-field indexes used in GATTAttributeTypes.CharacteristicExtendedPropertiesUUID attribute
    [Flags]
    public enum GATTCharacteristicExtendedProperties : ushort {

        ReliableWrite        = 0x0001,
        WritableAuxiliaries  = 0x0002

    }

    // Bit-field indexes used in GATTAttributeTypes.CharacteristicClientConfigurationUUID attribute
    [Flags]
    public enum GATTClientCharacteristicClientConfigurations : ushort {

        Notification  = 0x0001,
        Indication    = 0x0002

    }

    // Bit-field indexes used in GATTAttributeTypes.CharacteristicServerConfigurationUUID attribute
    [Flags]
    public enum GATTClientCharacteristicServerConfigurations : ushort {

        Broadcast  = 0x0001

    }

    

}