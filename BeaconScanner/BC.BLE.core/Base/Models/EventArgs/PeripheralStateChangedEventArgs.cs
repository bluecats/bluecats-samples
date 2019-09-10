using BlueCats.Bluetooth.Core.Base.Models.Enums;

namespace BlueCats.Bluetooth.Core.Base.Models.EventArgs {

    public class PeripheralStateChangedEventArgs : System.EventArgs {

        public PeripheralStateChangedEventArgs(PeripheralState newState) {
            NewState = newState;
        }

        public PeripheralState NewState { get; private set; }
    }

}