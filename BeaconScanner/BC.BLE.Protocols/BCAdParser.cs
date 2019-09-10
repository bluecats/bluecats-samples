using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BlueCats.Bluetooth.BLEProtocols.AdModels.Apple;
using BlueCats.Bluetooth.Core.Models;
using BlueCats.Bluetooth.Core.Models.Enums;
using BlueCats.Bluetooth.Core.Utils;
using BlueCats.Tools.Portable.Util;

using static BlueCats.Bluetooth.BLEProtocols.BCAdConstants;

namespace BlueCats.Bluetooth.BLEProtocols {
    
    public class BCAdParser {
        
        public event EventHandler< IBeaconAdModel > ParsedAppleIBeaconAd;

        public bool Parse( byte[] rawAdData, string bluetoothAddress ) {

            if (rawAdData == null || rawAdData.Length == 0) return false;

            try {
                var adStructures = AdStructureParser.ParseAdData( rawAdData );

                if ( adStructures.ContainsKey( GAPAdType.ManufacturerSpecificData ) ) {

                    var mfrData = adStructures[ GAPAdType.ManufacturerSpecificData ];

                    var appleIBeaconAdData = TryParseAppleIBeacon( mfrData, bluetoothAddress );
                    if ( appleIBeaconAdData != null ) {
                        ParsedAppleIBeaconAd?.Invoke( this, appleIBeaconAdData );
                        return true;
                    }
                }

                Debug.WriteLine( $"Unparsed BLE ad: len={rawAdData.Length} data={rawAdData.ToHexString()}" );

                return false;

            }
            catch ( Exception ex ) {
                Debug.WriteLine( $"Error while parsing BLE ads: {ex.GetBaseException().Message}" );
                return false;
            }
        }

        private IBeaconAdModel TryParseAppleIBeacon( AdStructure adStruct, string bluetoothAddress ) {           
            try {
                // Check length of struct 
                // (AD_APPLE_IBEACON_SIZE - 1 additional ad struct, size & struct ID bytes from this struct)
                if (adStruct.Data.Length != AD_APPLE_IBEACON_SIZE - 5)
                    return null;

                var data = adStruct.Data;
                var id = data.Take(2).ToHexString(true);

                if (!string.Equals(id, APPLE_ID, StringComparison.OrdinalIgnoreCase))
                    return null;

                if (data[2] != 0x02) return null;
                if (data[3] != 0x15) return null; //  length
                
                var proximityUUID = data.Skip(4).Take(16).ToHexString();
                var major = BitConverter.ToUInt16(data.Skip(20).Take(2).Reverse().ToArray(), 0);
                var minor = BitConverter.ToUInt16(data.Skip(22).Take(2).Reverse().ToArray(), 0);
                var measuredPower = (sbyte) data.Last();

                var appleIBeaconData = new IBeaconAdModel {
                    Major = major,
                    Minor = minor,
                    ProximityUUID = proximityUUID,
                    MeasuredPower = measuredPower,
					BluetoothAddress = bluetoothAddress
				};

                Debug.WriteLine(
                    $"Parsed AppleIBeaconAdData: " +
                    $"maj={appleIBeaconData.Major} " +
                    $"min={appleIBeaconData.Minor} " +
                    $"prox={appleIBeaconData.ProximityUUID} " +
                    $"txpwr={appleIBeaconData.MeasuredPower}" 
                );

                return appleIBeaconData;

            } catch (Exception ex) {
                Debug.WriteLine($"Error while parsing AppleIBeacon ad: {ex.GetBaseException().Message}");
                return null;
            }
        }

    }

}