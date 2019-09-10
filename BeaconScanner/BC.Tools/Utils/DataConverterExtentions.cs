using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueCats.Tools.Portable.Util {

    public static class DataConverterExtentions {

        public static string ToHexString(this IEnumerable<byte> bytes, bool reverseBytes = false, string delimeter = "") => DataConverter.ByteArrayToHexString(bytes.ToArray(), reverseBytes, delimeter);

        public static string ToHexString(this UInt16 val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this UInt32 val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this UInt64 val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this Int16 val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this Int32 val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this Int64 val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this Single val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this Double val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, reverseBytes, delimeter);

        public static string ToHexString(this Boolean val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, delimeter);

        public static string ToHexString(this Char val, bool reverseBytes = false, string delimeter = "") => DataConverter.BasicTypeToHexString(val, delimeter);
        
        public static byte[] ToByteArray(this string hexString, bool reverseBytes = false) => DataConverter.HexStringToByteArray(hexString, reverseBytes);

        public static string ToAsciiString (this byte[] bytes, string nonReadableReplacementStr = "" ) => DataConverter.ByteArrayToAsciiString(bytes, nonReadableReplacementStr);
    }

}