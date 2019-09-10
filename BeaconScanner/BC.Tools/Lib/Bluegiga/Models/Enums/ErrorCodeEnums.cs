//
// Bluegiga specific error codes
//

namespace BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums {

    public enum BGErrorCode : ushort {

        NoError = 0x0000,
        InvalidParameter = 0x0180,
        DeviceInWrongState = 0x0181,
        OutOfMemory = 0x0182,
        FeatureNotImplemented = 0x0183,
        CommandNotRecognized = 0x0184,
        Timeout = 0x0185,
        NotConnected = 0x0186,
        Flow = 0x0187,
        UserAttribute = 0x0188,
        InvalidLicenseKey = 0x0189,
        CommandTooLong = 0x018A,
        OutOfBonds = 0x018B,
        AuthenticationFailure = 0x0205,
        PinOrKeyMissing = 0x0206,
        MemoryCapacityExceeded = 0x0207,
        ConnectionTimeout = 0x0208,
        ConnectionLimitExceeded = 0x0209,
        CommandDisallowed = 0x020C,
        InvalidCommandParameters = 0x0212,
        RemoteUserTerminatedConnection = 0x0213,
        ConnectionTerminatedByLocalHost = 0x0216,
        LlResponseTimeout = 0x0222,
        LlInstantPassed = 0x0228,
        ControllerBusy = 0x023A,
        UnacceptableConnectionInterval = 0x023B,
        DirectedAdvertisingTimeout = 0x023C,
        MicFailure = 0x023D,
        ConnectionFailedToBeEstablished = 0x023E,
        PasskeyEntryFailed = 0x0301,
        OobDataIsNotAvailable = 0x0302,
        AuthenticationRequirements = 0x0303,
        ConfirmValueFailed = 0x0304,
        PairingNotSupported = 0x0305,
        EncryptionKeySize = 0x0306,
        CommandNotSupported = 0x0307,
        UnspecifiedReason = 0x0308,
        RepeatedAttempts = 0x0309,
        InvalidParameters = 0x030A,
        InvalidHandle = 0x0401,
        ReadNotPermitted = 0x0402,
        WriteNotPermitted = 0x0403,
        InvalidPdu = 0x0404,
        InsufficientAuthentication = 0x0405,
        RequestNotSupported = 0x0406,
        InvalidOffset = 0x0407,
        InsufficientAuthorization = 0x0408,
        PrepareQueueFull = 0x0409,
        AttributeNotFound = 0x040A,
        AttributeNotLong = 0x040B,
        InsufficientEncryptionKeySize = 0x040C,
        InvalidAttributeValueLength = 0x040D,
        UnlikelyError = 0x040E,
        InsufficientEncryption = 0x040F,
        UnsupportedGroupType = 0x0410,
        InsufficientResources = 0x0411,
        ApplicationErrorCodes = 0x0480

    }

}