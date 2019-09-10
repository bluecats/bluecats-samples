//
// Bluegiga specific ATT enumerations
//

namespace BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums {

    public enum BGATTClientAttributeValueType : byte {

        Read        = 0x00,
        Notify      = 0x01,
        Indicate    = 0x02,
        ReadByType  = 0x03,
        ReadBlob    = 0x04,
        RspReq      = 0x05

    }

}