using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlueCats.Bluetooth.Core.Models;
using BlueCats.Bluetooth.Core.Models.Enums;
using BlueCats.Tools.Portable.Util;



namespace BlueCats.Bluetooth.Core.Utils {

    public class AdStructureParser {

        public static Dictionary<GAPAdType, AdStructure> ParseAdData(byte[] ad) {
            if (ad == null || ad.Length == 0) return null;

            var adStructDict = new Dictionary< GAPAdType, AdStructure >();
            var i = ad.ToList().GetEnumerator();

            try {
                while (i.MoveNext()) {

                    byte len = i.Current;
                    i.MoveNext();

                    byte type = i.Current; // len includes type and data

                    var data = new List<byte>();
                    for (var idx = 0; idx < len - 1; idx++) {
                        i.MoveNext();
                        data.Add(i.Current);
                    }

                    var adStruct = new AdStructure {
                        Type = (GAPAdType) Enum.ToObject(typeof(GAPAdType), type),
                        Data = data.ToArray()
                    };

                    adStructDict.Add(adStruct.Type, adStruct);
                }

            } catch (Exception) {
                Debug.WriteLine( "Warning: Silenced an exception while parsing BLE ad - ill formatted BLE packet");
                return adStructDict;
            }
            finally {
                i.Dispose();
            }

            return adStructDict;
        }

    }

}