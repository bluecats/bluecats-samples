using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BlueCats.Tools.Portable.IO.Models;

namespace BlueCats.Tools.Portable.IO {

    public interface ISerialDevice {

        event EventHandler< IReadOnlyList< byte > > DataReceived;
        event EventHandler< Exception > ErrorReceived;

        string Name { get; }
        string UniqueIdentifier { get; }
        bool IsAttached { get; }
        bool IsDisposed { get; }
        uint? BaudRate { get; }
        SerialParity? Parity { get; }
        SerialStopBitCount? StopBitCount { get; }
        SerialHandshake? Handshake { get; }

        Task WriteAsync( IList< byte > dataOut );

        Task AttachAsync( uint baudRate = 9600, SerialParity parity = SerialParity.None, SerialStopBitCount stopBitCount = SerialStopBitCount.One, SerialHandshake handshake = SerialHandshake.None );

        Task DetachAsync();

    }

}