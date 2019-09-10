using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BlueCats.Tools.Portable.Util {

    public static class DataConverter {

        public static string ByteArrayToHexString(byte[] bytes, bool reverseBytes = false, string delimiter = "") {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();

            var hex = BitConverter.ToString(bytes);
            if ( !delimiter.Equals( "-", StringComparison.OrdinalIgnoreCase ) )
                hex = hex.Replace( "-", delimiter );

            return hex;
        }

        public static string ByteArrayToAsciiString(byte[] bytes, char nonReadableReplacementChar = '.') {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            const int READABLE_ASCII_START = 0x20;
            const int READABLE_ASCII_END = 0x7E;

            foreach (byte b in bytes) {
                sb.Append((b >= READABLE_ASCII_START && b <= READABLE_ASCII_END) 
                    ? (char) b 
                    : nonReadableReplacementChar);
            }
            return sb.ToString();
        }

        public static string ByteArrayToAsciiString(byte[] bytes, string nonReadableReplacementString = "") {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            const int READABLE_ASCII_START = 0x20;
            const int READABLE_ASCII_END = 0x7E;

            foreach (byte b in bytes) {
                sb.Append((b >= READABLE_ASCII_START && b <= READABLE_ASCII_END) 
                    ? ( (char) b ).ToString()
                    : nonReadableReplacementString );
            }
            return sb.ToString();
        }

        public static byte[] HexStringToByteArray(string hexString, bool reverseBytes = false) {
            if ( string.IsNullOrEmpty( hexString ) ) return null;
            if ( hexString.Length == 1 ) return null;

            try {
                hexString = hexString.Replace( " ", "" );
                hexString = hexString.Replace( "-", "" );
                hexString = hexString.Replace( ":", "" );
                hexString = hexString.ToUpperInvariant();

                var bytes = new byte[hexString.Length / 2];
                int bl = bytes.Length;
                for ( int i = 0; i < bl; ++i ) {

                    var hexStrIdx = 2 * i;
                    if ( hexString[ hexStrIdx ] > 'F'
                         || ( hexString[ hexStrIdx ] < 'A' && hexString[ hexStrIdx ] > '9' )
                         || hexString[ hexStrIdx ] < '0' )
                        return new byte[0];
                    if ( hexString[ hexStrIdx + 1 ] > 'F'
                         || ( hexString[ hexStrIdx + 1 ] < 'A' && hexString[ hexStrIdx + 1 ] > '9' )
                         || hexString[ hexStrIdx + 1 ] < '0' )
                        return new byte[0];

                    bytes[ i ] = (byte) ( ( hexString[ hexStrIdx ] > '9' ? hexString[ hexStrIdx ] - 0x37 : hexString[ hexStrIdx ] - 0x30 ) << 4 );
                    bytes[ i ] |= (byte) ( hexString[ hexStrIdx + 1 ] > '9' ? hexString[ hexStrIdx + 1 ] - 0x37 : hexString[ hexStrIdx + 1 ] - 0x30 );
                }

                if ( reverseBytes )
                    bytes = bytes.Reverse().ToArray();
                return bytes;
            }
            catch {
                return null;
            }
        }

        public static string BasicTypeToHexString(UInt16 val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(UInt32 val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(UInt64 val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Int16 val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Int32 val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Int64 val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Single val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Double val, bool reverseBytes = false, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            if (reverseBytes)
                bytes = bytes.Reverse().ToArray();
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Boolean val, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static string BasicTypeToHexString(Char val, string delimiter = "") {
            var bytes = BitConverter.GetBytes(val);
            var hexString = BitConverter.ToString(bytes).Replace("-", delimiter);
            return hexString;
        }

        public static T FromBytes< T >( byte[] bytes, bool isBytesLittleEndian, IDictionary< string, int > arrayLengthMap = null ) where T : new() {
            // Currently supports classes and structs composed of only fields of basic 
            // data types: UInt16, Uint32, & byte or arrays of those basic data types 

            var bytesCopy = new byte [ bytes.Length ];
            bytes.CopyTo( bytesCopy, 0 );

            // object ype used instead of T, because it applies "boxing" to structs, enabling
            // them to be passed by reference into reflection methods
            object resultStruct = new T(); 
            var bytesIdx = 0;
            var fieldInfos = typeof( T ).GetFields();

            // Synthesize an object of a given type from next bytes
            Func< Type, object > synthesizeObjectFromNextBytes = type => {

                if ( bytesIdx >= bytesCopy.Length )
                    throw new Exception( "Size of struct exceeds the number of bytes given" );

                if ( ReferenceEquals( type, typeof( byte ) ) ) {
                    var result = bytesCopy[ bytesIdx ];
                    bytesIdx++;
                    return result;
                }
                if ( ReferenceEquals( type, typeof( UInt16 ) ) ) {
                    if ( isBytesLittleEndian != BitConverter.IsLittleEndian )
                        Array.Reverse( bytesCopy, bytesIdx, sizeof( UInt16 ) );
                    var result = BitConverter.ToUInt16( bytesCopy, bytesIdx );
                    bytesIdx += sizeof( UInt16 );
                    return result;
                }
                if ( ReferenceEquals( type, typeof( UInt32 ) ) ) {
                    if ( isBytesLittleEndian != BitConverter.IsLittleEndian )
                        Array.Reverse( bytesCopy, bytesIdx, sizeof( UInt32 ) );
                    var result = BitConverter.ToUInt32( bytesCopy, bytesIdx );
                    bytesIdx += sizeof( UInt32 );
                    return result;
                }
                return null;
            };

            foreach ( var fieldInfo in fieldInfos ) {
                var fieldType = fieldInfo.FieldType;
                if ( !fieldType.IsArray ) {
                    var fieldValue = synthesizeObjectFromNextBytes( fieldType );
                    fieldInfo.SetValue( resultStruct, fieldValue );
                }
                else {
                    if ( arrayLengthMap == null )
                        throw new Exception( "Missing the arrayLengthMap for array length definitions" );
                    var fieldElementType = fieldType.GetElementType();
                    var arrayFieldLength = arrayLengthMap[ fieldInfo.Name ];
                    var objectArray = new object[ arrayFieldLength ];
                    
                    for ( int i = 0; i < arrayFieldLength; i++ ) {
                        objectArray[ i ] = synthesizeObjectFromNextBytes( fieldElementType );
                    }

                    if ( ReferenceEquals( fieldElementType, typeof( byte ) ) ) {
                        fieldInfo.SetValue( resultStruct, objectArray.Select( obj => (byte) obj ).ToArray() );
                    }
                    else if ( ReferenceEquals( fieldElementType, typeof( UInt16 ) ) ) {
                        fieldInfo.SetValue( resultStruct, objectArray.Select( obj => (UInt16) obj ).ToArray() );
                    }
                    else if ( ReferenceEquals( fieldElementType, typeof( UInt32 ) ) ) {
                        fieldInfo.SetValue( resultStruct, objectArray.Select( obj => (UInt32) obj ).ToArray() );
                    }                    
                }
            }
            return (T) resultStruct;
        }

    }

}