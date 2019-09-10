using System.Text.RegularExpressions;

namespace BlueCats.Tools.Portable.Util {

    public static class DataParser {

        static DataParser() {
            _hexStringDelimiterRegex = new Regex(@"(\$|0x|0X|,|-|:|x|X)(?!(\$|0x|0X|,|-|:|x|X))", 
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            _whiteSpaceRegex = new Regex(@"\s+", 
                RegexOptions.Compiled);
        }

        private static readonly Regex _hexStringDelimiterRegex;
        private static readonly Regex _whiteSpaceRegex;

        public static string ParseHexString(string hexString) {
            if (string.IsNullOrEmpty(hexString))
                return null;

            var whiteSpaceStrippedHex = _whiteSpaceRegex.Replace(hexString, "");
            var delimiterStrippedHex = _hexStringDelimiterRegex.Replace(whiteSpaceStrippedHex, "");
            
            if (delimiterStrippedHex.Equals(string.Empty)) return null;

            foreach (char c in delimiterStrippedHex)
                if (!IsHexChar(c)) return null;

            var hasAnEvenCharacterCount = (delimiterStrippedHex.Length % 2) == 0;
            return hasAnEvenCharacterCount ? delimiterStrippedHex : null;
        }

        public static bool IsValidHexString(string hexString, bool allowPartiallyFormedHexString = false) {

            if (string.IsNullOrEmpty(hexString))
                return false;

            // strip white space
            var whiteSpaceStrippedHex = _whiteSpaceRegex.Replace(hexString, "");
            // strip delimiters
            var delimiterStrippedHex = _hexStringDelimiterRegex.Replace(whiteSpaceStrippedHex, "");
            
            if (delimiterStrippedHex.Equals(string.Empty))
                return allowPartiallyFormedHexString;


            // check for hex chars only
            foreach (char c in delimiterStrippedHex)
                if (!IsHexChar(c)) return false;

            // if allowTrailingNibble is false
            if (allowPartiallyFormedHexString) return true;

            var hasAnEvenCharacterCount = (delimiterStrippedHex.Length % 2) == 0;
            return hasAnEvenCharacterCount;
        }

        public static bool IsHexChar(char c) {
            return   (( c >= 'A' && c <= 'F' ) 
                   || ( c >= 'a' && c <= 'f' ) 
                   || ( c >= '0' && c <= '9' ));
        }

        public static bool IsAlphaNumeric( string str ) {
            var regex = new Regex( @"^[a-zA-Z0-9\s,]*$" );
            return regex.IsMatch( str );
        }

    }

}