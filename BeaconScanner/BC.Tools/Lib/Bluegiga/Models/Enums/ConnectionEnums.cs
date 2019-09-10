//
// Bluegiga specific Connection enumerations
//

using System;

namespace BlueCats.Tools.Portable.Lib.Bluegiga.Models.Enums {

    [Flags]
    public enum BGConnectionStatusFlags : byte {

        Connected        = 0x01,
        Encrypted        = 0x02,
        Completed        = 0x04,
        ParametersChange = 0x08
       
    }

}