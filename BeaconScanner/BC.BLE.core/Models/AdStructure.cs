using System.Collections.Generic;

using BlueCats.Bluetooth.Core.Models.Enums;

namespace BlueCats.Bluetooth.Core.Models {

    public class AdStructure {

        public GAPAdType Type { get; set; }
        public byte[] Data { get; set; }

    }

}