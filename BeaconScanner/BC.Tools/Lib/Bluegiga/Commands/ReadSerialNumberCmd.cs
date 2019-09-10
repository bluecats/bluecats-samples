using System.Linq;
using System.Threading.Tasks;

using BlueCats.Tools.Portable.IO;
using BlueCats.Tools.Portable.Lang.CommandPattern;

namespace BlueCats.Tools.Portable.Lib.Bluegiga.Commands {

    public class ReadSerialNumberCmd : IAsyncCommandWithResult<byte[]> {

        public ReadSerialNumberCmd( ISerialDevice serialDevice ) {
            _serialDevice = serialDevice;
        }

        private readonly ISerialDevice _serialDevice;
        private byte[] _serialNumber;

        public async Task<byte[]> ExecuteAsync() {

            using ( var api = new BGLibApi( _serialDevice ) ) {
                _serialNumber = new[] {
                    await api.RegReadAsync( 0x780e ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0x780f ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0x7810 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0x7811 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0x7812 ).ConfigureAwait( false ),
                    await api.RegReadAsync( 0x7813 ).ConfigureAwait( false )
                };
            }

            return _serialNumber.Reverse().ToArray();
        }

        Task IAsyncCommand.ExecuteAsync() {
            return ExecuteAsync();
        }

    }

}