namespace BlueCats.Bluetooth.Core.Base.Models.EventArgs {

    public class PeripheralDisconnectedEventArgs : System.EventArgs {

        public Peripheral Peripheral { get; set; }
        public string Error { get; set; }

    }

}