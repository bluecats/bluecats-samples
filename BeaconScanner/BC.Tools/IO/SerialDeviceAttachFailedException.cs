using System;

namespace BlueCats.Tools.UWP.IO {

    public class SerialDeviceAttachFailedException : Exception {

        public SerialDeviceAttachFailedException() { }

        public SerialDeviceAttachFailedException( string message ) : base( message ) { }

        public SerialDeviceAttachFailedException( string message, Exception innerException ) : base( message, innerException ) { }

    }

}