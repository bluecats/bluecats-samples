namespace BlueCats.Bluetooth.Core.Base.Models.EventArgs {

    public class PeripheralDiscoveredEventArgs : System.EventArgs {

        public Peripheral Peripheral { get; set; }
        public byte[] AdvertisementData { get; set; }
        public byte[] ScanResponseData { get; set; }
        public bool IsConnectable { get; set; }
        public sbyte RSSI { get; set; }

    }

}